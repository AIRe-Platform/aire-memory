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
using Aire.Sdk.Models.Chat;
using Aire.Sdk.Auth;
using System.Web.Http;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Aire.Memory.Services;
using Aire.Sdk.Auth.Extensions;

namespace Aire.Memory.Api;

public class ChatHistory_v1
{
    private readonly BlobContainerClient _chatlogs;
    private readonly MemoryStorageService _storageService;
    private readonly IJwtTokenService _jwt;

    public ChatHistory_v1(BlobServiceClient blobs, MemoryStorageService storageService, IJwtTokenService jwt)
    {
        _chatlogs = blobs.GetBlobContainerClient(AireConstants.Blobs.ChatLogs);
        _chatlogs.CreateIfNotExists(publicAccessType: PublicAccessType.None);

        _storageService = storageService;
        _jwt = jwt;
    }

    [Function("GetChatHistory_v1")]
    [OpenApiOperation("getChatHistory", ["Chat History"], Summary = "Get a list of chat logs")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(List<ChatLogMetadata>), Description = "List of chat metadata objects")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Missing platform authentication")]
    public async Task<IActionResult> GetChatHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/chat-history")] HttpRequest req,
        FunctionContext context)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadChatHistory))
            return new ForbiddenResult();

        if (auth.Platform == null)
            return new BadRequestResult();

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());
        var query = await tables.QueryAsync<ChatLogEntity>(x => x.PartitionKey == auth.UserId);

        var logs = await query.ToListAsync();
        var list = logs.Select(x => new ChatLogMetadata
        {
            Id = x.Id(),
            Time = x.Timestamp?.UtcDateTime
        }).ToList();

        return new ObjectResult(list);
    }

    [Function("GetChatHistoryWithId_v1")]
    [OpenApiOperation("getChatHistoryWithId", ["Chat History"], Summary = "Retrieve a chat log")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Chat log identifier", In = ParameterLocation.Path, Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ChatLog), Description = "List of chat messages and chat state")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The chat log was not found.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid param or missing platform authentication")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> GetChatHistoryWithId(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/chat-history/{id}")] HttpRequest req,
        FunctionContext context,
        string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadChatHistory))
            return new ForbiddenResult();

        if (string.IsNullOrWhiteSpace(id))
            return new BadRequestResult();

        if (auth.Platform == null)
            return new BadRequestResult();

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var entity = await tables.RetrieveAsync<ChatLogEntity>(auth.UserId, id);
        if (entity == null)
            return new NotFoundResult();

        var chat = await entity.GetFromBlob(_chatlogs, auth.UserKey);
        if (chat == null)
            return new NotFoundResult();

        chat.Metadata = new ChatLogMetadata
        {
            Id = id,
            Time = entity.Timestamp?.UtcDateTime
        };

        return new ObjectResult(chat);
    }

    [Function("PostChatHistory_v1")]
    [OpenApiOperation("postChatHistory", ["Chat History"], Summary = "Store new chat log")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiRequestBody("application/json", typeof(ChatLog), Description = "List of chat messages and chat state", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ChatLogMetadata), Description = "Chat log metadata")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body or missing platform authentication")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> PostChatHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/chat-history")] HttpRequest req,
        FunctionContext context)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteChatHistory))
            return new ForbiddenResult();

        var chat = await req.ReadJson<ChatLog>();
        if (chat == null)
            return new BadRequestResult();

        var entity = new ChatLogEntity(auth.UserId);
        await entity.SaveToBlob(_chatlogs, chat, auth.UserKey);

        if (auth.Platform == null)
            return new BadRequestResult();

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var add = await tables.UpsertAsync(entity);
        if (!add)
            return new InternalServerErrorResult();

        var metadata = new ChatLogMetadata
        {
            Id = entity.Id(),
            Time = entity.Timestamp?.UtcDateTime
        };
        return new ObjectResult(metadata);
    }

    [Function("PutChatHistory_v1")]
    [OpenApiOperation("putChatHistory", ["Chat History"], Summary = "Edit existing chat log")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Chat log identifier", Required = true)]
    [OpenApiRequestBody("application/json", typeof(ChatLog), Description = "List of chat messages and chat state", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ChatLogMetadata), Description = "Chat log metadata")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The chat log was not found")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body or param, or missing platform authentication")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> PutChatHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "v1/chat-history/{id}")] HttpRequest req,
        FunctionContext context,
        string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteChatHistory))
            return new ForbiddenResult();

        if (string.IsNullOrWhiteSpace(id))
            return new BadRequestResult();

        var chat = await req.ReadJson<ChatLog>();
        if (chat == null)
            return new BadRequestResult();

        if (auth.Platform == null)
            return new BadRequestResult();

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var chatlog = await tables.RetrieveAsync<ChatLogEntity>(auth.UserId, id);
        if (chatlog == null)
            return new NotFoundResult();

        await chatlog.SaveToBlob(_chatlogs, chat, auth.UserKey);

        var update = await tables.UpsertAsync(chatlog);
        if (!update)
            return new InternalServerErrorResult();

        var metadata = new ChatLogMetadata
        {
            Id = chatlog.Id(),
            Time = chatlog.Timestamp?.UtcDateTime
        };

        return new ObjectResult(metadata);
    }

    [Function("DeleteChatHistory_v1")]
    [OpenApiOperation("deleteChatHistory", ["Chat History"], Summary = "Delete entire chat history")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NoContent, Description = "The chatlogs were removed successfully")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Missing platform authentication")]
    public async Task<IActionResult> DeleteChatHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v1/chat-history")] HttpRequest req,
        FunctionContext context)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.DeleteChatHistory))
            return new ForbiddenResult();

        if (auth.Platform == null)
            return new BadRequestResult();

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var query = await tables.QueryAsync<ChatLogEntity>(x => x.PartitionKey == auth.UserId);
        var history = await query.ToListAsync();

        foreach (var chat in history)
        {
            await _chatlogs.DeleteBlobIfExistsAsync(chat.Id());

            var delete = await tables.DeleteAsync(chat);
            if (!delete)
                return new InternalServerErrorResult();
        }

        return new NoContentResult();
    }

    [Function("DeleteChatHistoryWithId_v1")]
    [OpenApiOperation("deleteChatHistoryWithId", ["Chat History"], Summary = "Delete a chat log")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Chat log identifier", In = ParameterLocation.Path, Required = true)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NoContent, Description = "The chatlog(s) removed successfully")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The chat log was not found")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid param or missing platform authentication")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> DeleteChatHistoryWithId(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v1/chat-history/{id}")] HttpRequest req,
        FunctionContext context,
        string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.DeleteChatHistory))
            return new ForbiddenResult();

        if (string.IsNullOrWhiteSpace(id))
            return new BadRequestResult();

        if (auth.Platform == null)
            return new BadRequestResult();

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var chat = await tables.RetrieveAsync<ChatLogEntity>(auth.UserId, id);
        if (chat == null)
            return new NotFoundResult();

        await _chatlogs.DeleteBlobIfExistsAsync(chat.Id());

        var delete = await tables.DeleteAsync(chat);
        if (!delete)
            return new InternalServerErrorResult();

        return new NoContentResult();
    }
}
