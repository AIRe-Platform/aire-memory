// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
using Aire.Memory.Models;
using Aire.Sdk.AspNetCore;
using Aire.Sdk.Auth;
using Aire.Sdk.Models.Resources;
using InternalErrorResult = System.Web.Http.InternalServerErrorResult;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Aire.Memory.Services;
using Aire.Sdk.Auth.Extensions;

namespace Aire.Memory.Api;

public class Reminders_v1
{
    private readonly BlobContainerClient _reminders;
    private readonly MemoryStorageService _storageService;
    private readonly IJwtTokenService _jwt;

    public Reminders_v1(BlobServiceClient blobs, MemoryStorageService storageService, IJwtTokenService jwt)
    {
        _reminders = blobs.GetBlobContainerClient(AireConstants.Blobs.Reminders);
        _reminders.CreateIfNotExists(publicAccessType: PublicAccessType.None);

        _storageService = storageService;
        _jwt = jwt;
    }

    [Function("GetReminders_v1")]
    [OpenApiOperation("getReminders", ["Reminders"], Summary = "Get reminders")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(List<Reminder>), Description = "List of reminders")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "Not found")]
    [OpenApiParameter("include_active", In = ParameterLocation.Query, Type = typeof(bool), Required = false, Description = "Include already seen reminders")]
    public async Task<IActionResult> GetScheduledEvents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/reminders")] HttpRequest req,
        FunctionContext context,
        [FromQuery(Name = "include_inactive")] bool includeInactive = false)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadReminders))
            return new ForbiddenResult();

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var query = await tables
            .QueryAsync<ReminderEntity>(x =>
                x.PartitionKey == auth!.UserId &&
                (includeInactive || !x.ReadTimestamp.HasValue));

        var list = await query.ToListAsync();
        var reminders = new List<Reminder>();

        foreach (var ent in list)
        {
            var content = await ent.GetContentFromBlob(_reminders, auth!.UserKey);
            var e = ent.ToModel();
            e.Content = content;
            reminders.Add(e);
        }

        return new ObjectResult(reminders);
    }

    [Function("GetReminderById_v1")]
    [OpenApiOperation("getReminderById", tags: ["Reminders"], Summary = "Get reminder by ID")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Reminder identifier", Required = true, In = ParameterLocation.Path)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Reminder), Description = "Reminder object")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "Not found")]
    public async Task<IActionResult> GetReminderById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/reminder/{id}")] HttpRequest req,
        FunctionContext context,
        string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadReminders))
            return new ForbiddenResult();

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var entity = await tables.RetrieveAsync<ReminderEntity>(auth!.UserId, id);
        if (entity == null)
            return new NotFoundResult();

        var reminder = entity.ToModel();
        reminder.Content = await entity.GetContentFromBlob(_reminders, auth.UserKey);

        return new ObjectResult(reminder);
    }

    [Function("CreateReminder_v1")]
    [OpenApiOperation("createReminder", ["Reminders"], Summary = "Create a reminder")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiRequestBody("application/json", typeof(Reminder), Description = "New reminder", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Reminder), Description = "Saved reminder")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> CreateReminder(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/reminder")] HttpRequest req,
        FunctionContext context)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteReminders))
            return new ForbiddenResult();

        var reminder = await req.ReadJson<Reminder>();
        if (reminder == null ||
            reminder.Content == null ||
            reminder.ReadTimestamp.HasValue)
        {
            return new BadRequestResult();
        }

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var entity = new ReminderEntity(reminder, auth.UserId);
        await entity.SaveContentToBlob(_reminders, reminder.Content, auth.UserKey);

        var result = await tables.UpsertAsync(entity);
        if (!result)
            return new InternalErrorResult();

        reminder.Id = Guid.Parse(entity.Id());
        return new ObjectResult(reminder);
    }

    [Function("EditReminder_v1")]
    [OpenApiOperation("editReminder", ["Reminders"], Summary = "Edit existing reminder")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Reminder identifier", Required = true, In = ParameterLocation.Path)]
    [OpenApiRequestBody("application/json", typeof(Reminder), Description = "An event", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Reminder), Description = "Edited reminder")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "Not found")]
    public async Task<IActionResult> EditReminder(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "v1/reminder/{id}")] HttpRequest req,
        FunctionContext context,
        string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteReminders))
            return new ForbiddenResult();

        if (string.IsNullOrWhiteSpace(id))
            return new BadRequestResult();

        var reminder = await req.ReadJson<Reminder>();
        if (reminder == null)
            return new BadRequestResult();

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var entity = await tables.RetrieveAsync<ReminderEntity>(auth.UserId, id);
        if (entity == null)
            return new NotFoundResult();

        entity.TriggerTimestamp = reminder.TriggerTimestamp;
        entity.ReadTimestamp = reminder.ReadTimestamp;

        if (reminder.Content != null)
            await entity.SaveContentToBlob(_reminders, reminder.Content, auth.UserKey);

        var update = await tables.UpsertAsync(entity);
        if (!update)
            return new InternalErrorResult();

        return new ObjectResult(entity);
    }

    [Function("DeleteReminder_v1")]
    [OpenApiOperation("deleteReminder", ["Reminders"], Summary = "Delete reminder")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Reminder identifier", Required = true)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NoContent, Description = "Success")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid id")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "Not found")]
    public async Task<IActionResult> DeleteReminder(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v1/reminder/{id}")] HttpRequest req,
        FunctionContext context,
        string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.DeleteReminders))
            return new ForbiddenResult();

        if (string.IsNullOrWhiteSpace(id))
            return new BadRequestResult();

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var result = await tables.DeleteAsync<ReminderEntity>(auth.UserId, id);
        if (result)
            return new NoContentResult();
        else
            return new NotFoundResult();
    }
}
