// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


using System.Net;
using Aire.Memory.Models;
using Aire.Sdk.AspNetCore;
using Aire.Sdk.Auth;
using Aire.Sdk.Azure;
using Aire.Sdk.Helpers;
using Aire.Sdk.Models;
using Aire.Sdk.Models.Chat;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace Aire.Memory.Api;

public class UserData_v1
{
    private readonly BlobContainerClient _chatlogs;
    private readonly BlobContainerClient _questionnaires;
    private readonly QueueClient _queue;
    private readonly IJwtTokenService _jwt;
    private readonly ITableStorageService _storage;
    private readonly ILogger _log;

    public UserData_v1(
        BlobServiceClient blobs, QueueServiceClient queues, IJwtTokenService jwt,
        ITableStorageService storage, ILogger<UserData_v1> log)
    {
        _chatlogs = blobs.GetBlobContainerClient(AireConstants.Blobs.ChatLogs);
        _chatlogs.CreateIfNotExists(publicAccessType: PublicAccessType.None);

        _questionnaires = blobs.GetBlobContainerClient(AireConstants.Blobs.QuestionnaireResults);
        _questionnaires.CreateIfNotExists(publicAccessType: PublicAccessType.None);

        _queue = queues.GetQueueClient(AireConstants.Queues.UserDelete);
        _queue.CreateIfNotExists();

        _jwt = jwt;
        _storage = storage;
        _log = log;
    }

    [Function("GetUserData_v1")]
    [OpenApiOperation(
        operationId: "getUserData",
        tags: ["User data"],
        Summary = "Get all personal data collected")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(GDPRDataCollection), Description = "Collected personal data")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> GetUserData(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/user-data")] HttpRequest req,
        FunctionContext context)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        var requiredScopes = new AireScopes([
            AireScopes.ReadChatHistory,
            AireScopes.ReadQuestionnaire
        ]);

        if (!_jwt.CheckAuthorization(auth, requiredScopes: requiredScopes))
            return new ForbiddenResult();

        var chatlogs_entries = await _storage.Partition<ChatLogEntity>(auth.UserId);
        var chatlogs_metadata = chatlogs_entries
            .Select(x => new ChatLogMetadata
            {
                Id = x.Id(),
                Time = x.Timestamp
            })
            .ToList();

        Dictionary<string, ChatLog> chatlogs = [];
        foreach(var entry in chatlogs_entries) {
            var item = await entry.GetFromBlob(_chatlogs, auth.UserKey);
            if(item != null)
                chatlogs.Add(entry.Id(), item);
        }

        var questionnaire_entries = await _storage.Partition<QuestionnaireResultsEntity>(auth.UserId);
        var questionnaires = await questionnaire_entries
            .ToAsyncEnumerable()
            .SelectAwait(async x => await x.GetFromBlob(_questionnaires, auth.UserKey))
            .Where(x => x != null)
            .ToListAsync();

        var data = new GDPRDataCollection
        {
            Chats = chatlogs_metadata,
            Chatlogs = chatlogs!,
            Questionnaires = questionnaires!
        };

        return new OkObjectResult(data);
    }

    [Function("DeleteUserData_v1")]
    [OpenApiOperation(
        operationId: "deleteUserData",
        tags: ["User data"],
        Summary = "Queue ALL user data for deletion (or anonymization)")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NoContent, Description = "Deletion queued")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> DeleteUserData(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v1/user-data")] HttpRequest req,
        FunctionContext context,
        [FromQuery] bool? anonymize)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        var requiredScopes = new AireScopes([
            AireScopes.DeleteChatHistory
        ]);

        if (!_jwt.CheckAuthorization(auth, requiredScopes: requiredScopes))
            return new ForbiddenResult();

        var deleteOptions = new UserDeleteOptions
        {
            UserId = auth.UserId,
            Anonymize = anonymize ?? false
        };

        var result = await _queue.SendMessageAsync(deleteOptions.ObjectToJson());
        _log.LogInformation($"Queued user data for deletetion. MessageId: {result.Value.MessageId}");

        return new NoContentResult();
    }
}