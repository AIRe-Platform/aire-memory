using Aire.Sdk.Auth.Models;
using Aire.Sdk.Auth.Roles;
using Aire.Sdk.Auth.Scopes;
using Aire.Sdk.Auth.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;

namespace Aire.Memory.Api
{
    public class ChatHistory_v1
    {
        private readonly IJwtTokenService _jwt;
        private readonly ILogger _log;

        public ChatHistory_v1(IJwtTokenService jwt, ILoggerFactory loggerFactory)
        {
            _jwt = jwt;
            _log = loggerFactory.CreateLogger<ChatHistory_v1>();
        }

        [Function("GetChatHistory_v1")]
        [OpenApiOperation(operationId: "GetChatHistory", tags: ["chat-history"], Description = "Get a list of chat logs or a particular chat log")]
        public async Task<IActionResult> GetChatHistory(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "v1/chat-history/{id?}")] HttpRequest req,
            FunctionContext context,
            string? id = null)
        {
            var auth = context.Features.Get<JwtAuthFeature>();
            if(!_jwt.CheckAuthorization(auth, AireRoles.User, AireScopes.ReadChatHistory))
                return new UnauthorizedResult();

            return await Task.FromResult(new NotFoundResult());
        }

        [Function("PostChatHistory_v1")]
        [OpenApiOperation(operationId: "PostChatHistory", tags: ["chat-history"], Description = "Store new chat log")]
        public async Task<IActionResult> PostChatHistory(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "v1/chat-history/{id?}")] HttpRequest req,
            FunctionContext context,
            string? id = null)
        {
            var auth = context.Features.Get<JwtAuthFeature>();
            if(!_jwt.CheckAuthorization(auth, AireRoles.User, AireScopes.WriteChatHistory))
                return new UnauthorizedResult();

            return await Task.FromResult(new NotFoundResult());
        }

        [Function("PutChatHistory_v1")]
        [OpenApiOperation(operationId: "PutChatHistory", tags: ["chat-history"], Description = "Edit existing chat log")]
        public async Task<IActionResult> PutChatHistory(
            [HttpTrigger(AuthorizationLevel.Function, "put", Route = "v1/chat-history/{id}")] HttpRequest req,
            FunctionContext context,
            string id)
        {
            var auth = context.Features.Get<JwtAuthFeature>();
            if(!_jwt.CheckAuthorization(auth, AireRoles.User, AireScopes.WriteChatHistory))
                return new UnauthorizedResult();

            return await Task.FromResult(new NotFoundResult());
        }

        [Function("DeleteChatHistory_v1")]
        [OpenApiOperation(operationId: "DeleteChatHistory", tags: ["chat-history"], Description = "Wipe a chat log")]
        public async Task<IActionResult> DeleteChatHistory(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "v1/chat-history/{id}")] HttpRequest req,
            FunctionContext context,
            string id)
        {
            var auth = context.Features.Get<JwtAuthFeature>();
            if(!_jwt.CheckAuthorization(auth, AireRoles.User, AireScopes.DeleteChatHistory))
                return new UnauthorizedResult();

            return await Task.FromResult(new NotFoundResult());
        }
    }
}
