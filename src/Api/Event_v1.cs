// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Aire.Memory.Models;
using Aire.Sdk.AspNetCore;
using Aire.Sdk.Auth;
using Aire.Sdk.Azure;
using Aire.Sdk.Models.Resources;
using InternalErrorResult = System.Web.Http.InternalServerErrorResult;
using Aire.Memory.Helpers;
using Aire.Sdk.Auth.Extensions;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.Web.Http;

namespace Aire.Memory.Api;

public class ScheduledEvent_v1
{
    private readonly BlobContainerClient _events;
    private readonly ITableStorageService _tables;
    private readonly IJwtTokenService _jwt;
    private readonly ILogger _log;

    public ScheduledEvent_v1(BlobServiceClient blobs, ITableStorageService tables, IJwtTokenService jwt, ILogger<ScheduledEvent_v1> log)
    {
        _events = blobs.GetBlobContainerClient(AireConstants.Blobs.Events);
        _events.CreateIfNotExists(publicAccessType: PublicAccessType.None);

        _tables = tables;
        _jwt = jwt;
        _log = log;
    }

    [Function("GetScheduledEvents_v1")]
    [OpenApiOperation(
        operationId: "getScheduledEvents",
        tags: ["ScheduledEvents"],
        Summary = "Get scheduled events")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(List<ScheduledEvent>), Description = "List of scheduled event objects")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    public async Task<IActionResult> GetScheduledEvents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/events")] HttpRequest req,
        FunctionContext context)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null && !req.IsServiceRequest())
            return new UnauthorizedResult();

        bool access_stats = _jwt.CheckAuthorization(auth, AireScopes.ScheduledEvent);
        var query = await _tables.QueryAsync<ScheduledEventEntity>(x => x.PartitionKey == auth!.UserId);
        var list = await query.ToListAsync();
        var events = new List<ScheduledEvent>();

        foreach (var item in list)
        {
            var content = await item.GetContentFromBlob(_events, auth!.UserKey);
            var e = item.ToModel();
            e.Content = content;
            events.Add(e);
        }

        return new ObjectResult(events);
    }

    [Function("PostEvent_v1")]
    [OpenApiOperation(
        operationId: "postEvent",
        tags: ["Events"],
        Summary = "Store new event")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiRequestBody("application/json", typeof(ScheduledEvent), Description = "An event", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ScheduledEvent), Description = "Saved event")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> PostScheduledEvent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/events")] HttpRequest req,
        FunctionContext context)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteScheduledEvent))
            return new ForbiddenResult();

        var scheduledEvent = await req.ReadJson<ScheduledEvent>();
        if (scheduledEvent == null || scheduledEvent.Content == null)
            return new BadRequestResult();

        var entity = new ScheduledEventEntity(scheduledEvent, auth.UserId);
        await entity.SaveContentToBlob(_events, scheduledEvent.Content, auth.UserKey);
        var result = await _tables.UpsertAsync(entity);
        if (!result)
            return new InternalErrorResult();

        return new ObjectResult(scheduledEvent);
    }

    [Function("PutEvent_v1")]
    [OpenApiOperation(
        operationId: "putEvent",
        tags: ["Events"],
        Summary = "Edit existing event")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Event identifier", Required = true)]
    [OpenApiRequestBody("application/json", typeof(ScheduledEvent), Description = "An event", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ScheduledEvent), Description = "Saved event")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> PutScheduledEvent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/events/{id}")] HttpRequest req,
        FunctionContext context,
        string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteScheduledEvent))
            return new ForbiddenResult();

        if (string.IsNullOrWhiteSpace(id))
            return new BadRequestResult();

        var scheduledEvent = await req.ReadJson<ScheduledEvent>();
        if (scheduledEvent == null || scheduledEvent.Content == null)
            return new BadRequestResult();

        var scheduledEventEntity = await _tables.RetrieveAsync<ScheduledEventEntity>(auth.UserId, id);
        if (scheduledEventEntity == null)
            return new NotFoundResult();

        scheduledEventEntity.TriggerTimestamp = scheduledEvent.TriggerTimestamp;
        scheduledEventEntity.ReadTimestamp = scheduledEvent.ReadTimestamp;

        await scheduledEventEntity.SaveContentToBlob(_events, scheduledEvent.Content, auth.UserKey);

        var update = await _tables.UpsertAsync(scheduledEventEntity);
        if (!update)
            return new InternalServerErrorResult();

        return new ObjectResult(scheduledEvent);
    }
}
