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
using Aire.Sdk.Auth;
using Aire.Sdk.Models.Resources;
using Aire.Sdk.Azure;
using System.Web.Http;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Azure;
using Azure.Storage.Blobs.Models;

namespace Aire.Memory.Api;

public class QuestionnaireResults_v1
{
    private readonly BlobContainerClient _blobs;
    private readonly ITableStorageService _storage;
    private readonly IJwtTokenService _jwt;
    private readonly ILogger _log;

    public QuestionnaireResults_v1(
        IAzureClientFactory<BlobServiceClient> blobClientFactory,
        ITableStorageService storage, IJwtTokenService jwt, ILoggerFactory loggerFactory)
    {
        _blobs = blobClientFactory
            .CreateClient("blob-client")
            .GetBlobContainerClient("questionnaire-results");
        _blobs.CreateIfNotExists(publicAccessType: PublicAccessType.None);

        _storage = storage;
        _jwt = jwt;
        _log = loggerFactory.CreateLogger<QuestionnaireResults_v1>();
    }

    [Function("GetQuestionnaireResults_v1")]
    [OpenApiOperation(
        operationId: "getQuestionnaireResults",
        tags: ["Questionnaire Results"],
        Summary = "Retrieve questionnaire results")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Questionnaire identifier", In = ParameterLocation.Path, Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(List<QuestionnaireResults>), Description = "List of questionnaire results")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid param")]
    public async Task<IActionResult> GetQuestionnaireResults(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/questionnaire-results/{id}")] HttpRequest req,
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

        var query = await _storage
            .QueryAsync<QuestionnaireResultsEntity>(x => x.PartitionKey == auth.UserId && x.QuestionnaireId == id);

        var results = await query.ToListAsync();
        var asyncList = results.ToAsyncEnumerable();
        var list = await asyncList
            .SelectAwait(async x => await x.ToModelAsync(_blobs, auth.UserKey))
            .ToListAsync();

        return new OkObjectResult(list);
    }

    [Function("PostQuestionnaireResults_v1")]
    [OpenApiOperation(
        operationId: "postQuestionnaireResults",
        tags: ["Questionnaire Results"],
        Summary = "Store new questionnaire results")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiRequestBody("application/json", typeof(QuestionnaireResults), Description = "A questionnaire results", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(QuestionnaireResults), Description = "Saved questionnaire results")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> PostQuestionnaireResults(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/questionnaire-results")] HttpRequest req,
        FunctionContext context)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteChatHistory))
            return new ForbiddenResult();

        var results = await req.ReadJson<QuestionnaireResults>();
        if (results == null)
            return new BadRequestResult();

        results.Id = Guid.NewGuid().ToString();
        results.Timestamp = DateTime.UtcNow;
        var entity = new QuestionnaireResultsEntity(auth.UserId, results.Id)
        {
            QuestionnaireId = results.QuestionnaireId,
            Timestamp = results.Timestamp
        };
        await entity.SaveResults(_blobs, results, auth.UserKey);

        var add = await _storage.UpsertAsync(entity);
        if (!add)
            return new InternalServerErrorResult();

        return new OkObjectResult(results);
    }

    [Function("DeleteQuestionnaireResults_v1")]
    [OpenApiOperation(
        operationId: "deleteQuestionnaireResults",
        tags: ["Questionnaire Results"],
        Summary = "Retrieve questionnaire results")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Questionnaire identifier", In = ParameterLocation.Path, Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(List<QuestionnaireResults>), Description = "List of questionnaire results")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid param")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "Results not found")]
    public async Task<IActionResult> DeleteQuestionnaireResults(
    [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v1/questionnaire-results/{id}")] HttpRequest req,
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

        var entity = await _storage.RetrieveAsync<QuestionnaireResultsEntity>(auth.UserId, id);
        if (entity == null)
            return new NotFoundResult();

        await _blobs.DeleteBlobIfExistsAsync(entity.Id());

        var delete = await _storage.DeleteAsync(entity);
        if (!delete)
            return new InternalServerErrorResult();

        return new NoContentResult();
    }
}
