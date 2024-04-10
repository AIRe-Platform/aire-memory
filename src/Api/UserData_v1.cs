using System.Net;
using Aire.Memory.Models;
using Aire.Sdk.Auth;
using Aire.Sdk.Helpers;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace Aire.Memory.Api;

public class UserData_v1
{
    private readonly QueueClient _queue;
    private readonly IJwtTokenService _jwt;
    private readonly ILogger _log;

    public UserData_v1(IAzureClientFactory<QueueServiceClient> clientFactory, IJwtTokenService jwt, ILoggerFactory loggerFactory)
    {
        _queue = clientFactory
            .CreateClient("queue-client")
            .GetQueueClient(AireConstant.UserDeleteQueue);
        _queue.CreateIfNotExists();

        _jwt = jwt;
        _log = loggerFactory.CreateLogger<ChatHistory_v1>();
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
    public async Task<IActionResult> DeleteUserData(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v1/user-data")] HttpRequest req,
        FunctionContext context,
        [FromQuery] bool? anonymize)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        var requiredScopes = new AireScopes([
            AireScopes.DeleteChatHistory
        ]);

        if (!_jwt.CheckAuthorization(auth, requiredScopes: requiredScopes))
            return new UnauthorizedResult();

        var deleteOptions = new UserDeleteOptions
        {
            UserId = auth!.User,
            Anonymize = anonymize ?? false
        };

        var result = await _queue.SendMessageAsync(deleteOptions.ObjectToJson());
        _log.LogInformation($"Queued user data for deletetion. MessageId: {result.Value.MessageId}");

        return new NoContentResult();
    }
}