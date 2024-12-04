// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using Aire.Memory.Helpers;
using Aire.Memory.Models;
using Aire.Sdk.AspNetCore;
using Aire.Sdk.Auth;
using Aire.Sdk.Azure;
using Aire.Sdk.Models.Statistics;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace Aire.Memory.Api;

public class Statistics_v1
{
    private readonly ITableStorageService _storage;
    private readonly TableClient _stats;
    private readonly IJwtTokenService _jwt;
    private readonly ILogger<Statistics_v1> _log;

    public Statistics_v1(ITableStorageService storage, TableServiceClient tableClient, IJwtTokenService jwt, ILogger<Statistics_v1> log)
    {
        _storage = storage;
        _stats = tableClient.GetTableClient(AireConstants.Tables.Statistics);
        _stats.CreateIfNotExists();
        _jwt = jwt;
        _log = log;
    }

    [Function("GetStatisticsInfo_v1")]
    [OpenApiOperation(
        operationId: "getStatisticsInfo_v1",
        tags: ["Statistics"],
        Summary = "Get statistics info")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("from", Description = "Start datetime", In = ParameterLocation.Query, Required = true)]
    [OpenApiParameter("to", Description = "End datetime", In = ParameterLocation.Query, Required = false)]
    [OpenApiParameter("eventNamePrefix", Description = "Event name filter", In = ParameterLocation.Query, Required = false)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(List<StatisticsEventInfo>), Description = "List of event info")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Missing or invalid query parameters")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> GetStatisticsInfo(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/stats")] HttpRequest req,
        FunctionContext context,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? eventNamePrefix)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadStatistics))
            return new ForbiddenResult();

        if (!from.HasValue)
            return new BadRequestResult();

        var filter = StatisticsEntity.CreateFilter(from.Value, to, eventNamePrefix);
        var query = await _storage.QueryAsync<StatisticsEntity>(filter);
        var results = query.Select(x => x.ToModel());

        return new OkObjectResult(results);
    }

    [Function("QueryStatisticsEvents_v1")]
    [OpenApiOperation(
        operationId: "queryStatisticsEvents",
        tags: ["Statistics"],
        Summary = "Query statistics events")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("events", Description = "Comma-separated list of event names", In = ParameterLocation.Query, Required = true)]
    [OpenApiParameter("from", Description = "Start datetime", In = ParameterLocation.Query, Required = true)]
    [OpenApiParameter("to", Description = "End datetime", In = ParameterLocation.Query, Required = false)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(List<object>), Description = "List of event objects")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Missing or invalid query parameters")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> QueryStatisticsEvents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/stats/events")] HttpRequest req,
        FunctionContext context,
        [FromQuery] string? events,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? end)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadStatistics))
            return new ForbiddenResult();

        var eventNames = events?.Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        if (eventNames.Length == 0 || !from.HasValue)
            return new BadRequestResult();

        var results = new List<StatisticsEvent>();

        foreach (var e in eventNames)
        {
            var filter = StatisticsHelper.GenerateEventPartitionFilter(e, from.Value, end);
            var query = await _stats.QueryAsync<TableEntity>(filter).ToListAsync();
            var models = query.Select(StatisticsHelper.EventEntityToModel);
            results.AddRange(models);
        }

        return new OkObjectResult(results);
    }

    [Function("PostStatisticsEvent_v1")]
    [OpenApiOperation(
        operationId: "postStatisticsEvent",
        tags: ["Statistics"],
        Summary = "Post new statistics event")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(object), Description = "Event object")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid payload")]
    [OpenApiResponseWithoutBody(HttpStatusCode.UnprocessableEntity, Description = "Missing required fields")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    [OpenApiRequestBody("application/json", typeof(object), Description = "A new event object", Required = true)]
    public async Task<IActionResult> PostStatisticsEvent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/stats/event")] HttpRequest req,
        FunctionContext context)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteStatistics))
            return new ForbiddenResult();

        var data = await req.ReadJson<StatisticsEvent>();
        if (data == null)
            return new BadRequestResult();

        if (string.IsNullOrWhiteSpace(data.EventName) || !data.Timestamp.HasValue)
            return new UnprocessableEntityResult();

        var (pk, rk) = StatisticsHelper.GenerateTableKeys(data.Timestamp.Value, data.EventName);
        var ent = new TableEntity(data)
        {
            PartitionKey = pk,
            RowKey = rk
        };

        await _stats.AddEntityAsync(ent);

        // Update info entity
        {
            var keys = StatisticsEntity.CreateTableKeys(data.Timestamp.Value, data.EventName);
            var info = await _storage.RetrieveAsync<StatisticsEntity>(keys.pk, keys.rk);
            info ??= new StatisticsEntity(data.EventName);
            info.Count += 1;
            await _storage.UpsertAsync(info);
        }

        var model = StatisticsHelper.EventEntityToModel(ent);
        return new OkObjectResult(model);
    }

    [Function("UpdateStatisticsEvent_v1")]
    [OpenApiOperation(
        operationId: "updateStatisticsEvent",
        tags: ["Statistics"],
        Summary = "Update statistics event")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Event identifier", In = ParameterLocation.Path, Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(object), Description = "Updated event object")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The event does not exist")]
    [OpenApiResponseWithoutBody(HttpStatusCode.UnprocessableEntity, Description = "Missing required fields")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Conflict, Description = "Identifier mismatch")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    [OpenApiRequestBody("application/json", typeof(object), Description = "Existing event object", Required = true)]
    public async Task<IActionResult> UpdateStatisticsEvent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "v1/stats/event/{id}")] HttpRequest req,
        FunctionContext context,
        string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteStatistics))
            return new ForbiddenResult();

        var data = await req.ReadJson<StatisticsEvent>();
        if (data == null)
            return new BadRequestResult();

        if (string.IsNullOrWhiteSpace(data.EventName) || !data.Timestamp.HasValue)
            return new UnprocessableEntityResult();

        if (data.Id.HasValue && data.Id.Value.ToString() != id)
            return new ConflictResult();

        var (pk, rk) = StatisticsHelper.GenerateTableKeys(data.Timestamp.Value, data.EventName, id);
        var query = await _stats.GetEntityIfExistsAsync<TableEntity>(pk, rk);
        if (!query.HasValue)
            return new NotFoundResult();

        var ent = query.Value!;
        StatisticsHelper.MergeEventDataToEntity(ent, data);

        await _stats.UpsertEntityAsync(ent, TableUpdateMode.Replace);

        var model = StatisticsHelper.EventEntityToModel(ent);
        return new OkObjectResult(model);
    }
}
