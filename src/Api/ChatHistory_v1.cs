using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;

namespace Aire.Memory.Api
{
    public class ChatHistory_v1
    {
        private readonly ILogger _log;

        public ChatHistory_v1(ILoggerFactory loggerFactory)
        {
            _log = loggerFactory.CreateLogger<ChatHistory_v1>();
        }

        [Function("GetChatHistory_v1")]
        [OpenApiOperation(operationId: "GetChatHistory", tags: ["chat-history"], Description = "Get user chat history")]
        public async Task<IActionResult> GetChatHistory(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "v1/chat-history/{id?}")] HttpRequest req,
            FunctionContext context,
            string? id)
        {
            return await Task.FromResult(new NotFoundResult());
        }
    }
}
