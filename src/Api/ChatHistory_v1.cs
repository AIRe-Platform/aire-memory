using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Aire.Memory.Models;
using Aire.Sdk.AspNetCore;
using Aire.Sdk.Models.Chat;
using Aire.Sdk.Auth;

namespace Aire.Memory.Api;

public class ChatHistory_v1
{
    private readonly DatabaseContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly ILogger _log;

    public ChatHistory_v1(DatabaseContext db, IJwtTokenService jwt, ILoggerFactory loggerFactory)
    {
        _db = db;
        _jwt = jwt;
        _log = loggerFactory.CreateLogger<ChatHistory_v1>();
    }

    [Function("GetChatHistory_v1")]
    [OpenApiOperation(
        operationId: "getChatHistory",
        tags: ["chat-history"],
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

        var list = await _db.ChatLogs
            .Where(x => x.UserId == auth!.User)
            .Select(x => new ChatLogMetadata(x.Id, x.Timestamp))
            .ToListAsync();

        return new ObjectResult(list);
    }

    [Function("GetChatHistoryWithId_v1")]
    [OpenApiOperation(
        operationId: "getChatHistoryWithId",
        tags: ["chat-history"],
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
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid parameter")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
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

        if (!Guid.TryParse(id, out Guid chatId))
            return new BadRequestResult();

        var ent = await _db.ChatLogs
            .Where(x => x.UserId == auth!.User && x.Id == chatId)
            .FirstOrDefaultAsync();

        var chat = ent?.GetChatLog(auth!.UserKey);
        if (chat == null)
            return new NotFoundResult();

        return new ObjectResult(chat);
    }

    [Function("PostChatHistory_v1")]
    [OpenApiOperation(
        operationId: "postChatHistory",
        tags: ["chat-history"],
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

        var entity = new ChatLogEntity
        {
            UserId = auth!.User,
            Timestamp = DateTime.UtcNow
        };
        entity.SetChatLog(auth!.UserKey, chat);

        var add = await _db.ChatLogs.AddAsync(entity);
        await add.Context.SaveChangesAsync();

        var metadata = new ChatLogMetadata(entity.Id, entity.Timestamp);
        return new ObjectResult(metadata);
    }

    [Function("PutChatHistory_v1")]
    [OpenApiOperation(
        operationId: "putChatHistory",
        tags: ["chat-history"],
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

        if (!Guid.TryParse(id, out Guid chatId))
            return new BadRequestResult();

        var chat = await req.ReadJson<ChatLog>();
        if (chat == null)
            return new BadRequestResult();

        var chatlog = await _db.ChatLogs
            .Where(x => x.Id == chatId && x.UserId == auth!.User)
            .FirstOrDefaultAsync();

        if (chatlog == null)
            return new NotFoundResult();

        chatlog.SetChatLog(auth!.UserKey, chat);
        chatlog.Timestamp = DateTime.UtcNow;

        var update = _db.ChatLogs.Update(chatlog);
        await update.Context.SaveChangesAsync();

        return new ObjectResult(new ChatLogMetadata(chatlog.Id, chatlog.Timestamp));
    }

    [Function("DeleteChatHistory_v1")]
    [OpenApiOperation(
        operationId: "deleteChatHistory",
        tags: ["chat-history"],
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

        var history = await _db.ChatLogs
            .Where(x => x.UserId == auth!.User)
            .ToListAsync();

        foreach (var chat in history)
        {
            _db.ChatLogs.Remove(chat);
        }
        await _db.SaveChangesAsync();

        return new NoContentResult();
    }

    [Function("DeleteChatHistoryWithId_v1")]
    [OpenApiOperation(
        operationId: "deleteChatHistoryWithId",
        tags: ["chat-history"],
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
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Access denied")]
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

        if (!Guid.TryParse(id, out Guid chatId))
            return new BadRequestResult();

        var chat = await _db.ChatLogs
            .Where(x => x.UserId == auth!.User && x.Id == chatId)
            .FirstOrDefaultAsync();

        if (chat == null)
            return new NotFoundResult();

        _db.ChatLogs.Remove(chat);
        await _db.SaveChangesAsync();

        return new NoContentResult();
    }
}
