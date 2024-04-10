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
using Aire.Sdk.Models.Chat;
using Aire.Sdk.Auth;
using Aire.Sdk.Azure;
using System.Web.Http;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Azure;
using Azure.Storage.Blobs.Models;

namespace Aire.Memory.Api;

public class ChatHistory_v1
{
    private readonly BlobContainerClient _chatlogs;
    private readonly ITableStorageService _storage;
    private readonly IJwtTokenService _jwt;
    private readonly ILogger _log;

    public ChatHistory_v1(
        IAzureClientFactory<BlobServiceClient> blobClientFactory,
        ITableStorageService storage, IJwtTokenService jwt, ILoggerFactory loggerFactory)
    {
        _chatlogs = blobClientFactory
            .CreateClient("blob-client")
            .GetBlobContainerClient("chatlogs");
        _chatlogs.CreateIfNotExists(publicAccessType: PublicAccessType.None);

        _storage = storage;
        _jwt = jwt;
        _log = loggerFactory.CreateLogger<ChatHistory_v1>();
    }

    [Function("GetChatHistory_v1")]
    [OpenApiOperation(
        operationId: "getChatHistory",
        tags: ["Chat History"],
        Summary = "Get a list of chat logs")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(List<ChatLogMetadata>), Description = "List of chat metadata objects")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> GetChatHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/chat-history")] HttpRequest req,
        FunctionContext context)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadChatHistory))
            return new ForbiddenResult();

        var query = await _storage.QueryAsync<ChatLogEntity>(x => x.PartitionKey == auth.UserId);

        var logs = await query.ToListAsync();
        var list = logs.Select(x => new ChatLogMetadata
        {
            Id = x.Id(),
            Time = x.Timestamp
        }).ToList();

        return new ObjectResult(list);
    }

    [Function("GetChatHistoryWithId_v1")]
    [OpenApiOperation(
        operationId: "getChatHistoryWithId",
        tags: ["Chat History"],
        Summary = "Retrieve a chat log")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Chat log identifier", In = ParameterLocation.Path, Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ChatLog), Description = "List of chat messages and chat state")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The chat log was not found.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid param")]
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

        var entity = await _storage.RetrieveAsync<ChatLogEntity>(auth.UserId, id);
        if (entity == null)
            return new NotFoundResult();

        var chat = await entity.GetFromBlob(_chatlogs, auth.UserKey);
        if (chat == null)
            return new NotFoundResult();

        return new ObjectResult(chat);
    }

    [Function("PostChatHistory_v1")]
    [OpenApiOperation(
        operationId: "postChatHistory",
        tags: ["Chat History"],
        Summary = "Store new chat log")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiRequestBody("application/json", typeof(ChatLog), Description = "List of chat messages and chat state", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ChatLogMetadata), Description = "Chat log metadata")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body")]
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

        var add = await _storage.UpsertAsync(entity);
        if (!add)
            return new InternalServerErrorResult();

        var metadata = new ChatLogMetadata
        {
            Id = entity.Id(),
            Time = entity.Timestamp
        };
        return new ObjectResult(metadata);
    }

    [Function("PutChatHistory_v1")]
    [OpenApiOperation(
        operationId: "putChatHistory",
        tags: ["Chat History"],
        Summary = "Edit existing chat log")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Chat log identifier", Required = true)]
    [OpenApiRequestBody("application/json", typeof(ChatLog), Description = "List of chat messages and chat state", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ChatLogMetadata), Description = "Chat log metadata")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The chat log was not found")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body or param")]
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

        var chatlog = await _storage.RetrieveAsync<ChatLogEntity>(auth.UserId, id);
        if (chatlog == null)
            return new NotFoundResult();

        await chatlog.SaveToBlob(_chatlogs, chat, auth.UserKey);

        var update = await _storage.UpsertAsync(chatlog);
        if (!update)
            return new InternalServerErrorResult();

        var metadata = new ChatLogMetadata
        {
            Id = chatlog.Id(),
            Time = chatlog.Timestamp
        };

        return new ObjectResult(metadata);
    }

    [Function("DeleteChatHistory_v1")]
    [OpenApiOperation(
        operationId: "deleteChatHistory",
        tags: ["Chat History"],
        Summary = "Delete entire chat history")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NoContent, Description = "The chatlogs were removed successfully")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    public async Task<IActionResult> DeleteChatHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v1/chat-history")] HttpRequest req,
        FunctionContext context)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.DeleteChatHistory))
            return new ForbiddenResult();

        var query = await _storage.QueryAsync<ChatLogEntity>(x => x.PartitionKey == auth.UserId);
        var history = await query.ToListAsync();

        foreach (var chat in history)
        {
            await _chatlogs.DeleteBlobIfExistsAsync(chat.Id());

            var delete = await _storage.DeleteAsync(chat);
            if (!delete)
                return new InternalServerErrorResult();
        }

        return new NoContentResult();
    }

    [Function("DeleteChatHistoryWithId_v1")]
    [OpenApiOperation(
        operationId: "deleteChatHistoryWithId",
        tags: ["Chat History"],
        Summary = "Delete a chat log")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Chat log identifier", In = ParameterLocation.Path, Required = true)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NoContent, Description = "The chatlog(s) removed successfully")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The chat log was not found")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid param")]
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

        var chat = await _storage.RetrieveAsync<ChatLogEntity>(auth.UserId, id);
        if (chat == null)
            return new NotFoundResult();

        await _chatlogs.DeleteBlobIfExistsAsync(chat.Id());

        var delete = await _storage.DeleteAsync(chat);
        if (!delete)
            return new InternalServerErrorResult();

        return new NoContentResult();
    }
}
