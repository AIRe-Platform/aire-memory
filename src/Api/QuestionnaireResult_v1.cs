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
using System.Web.Http;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Aire.Memory.Services;
using Aire.Sdk.Auth.Extensions;

namespace Aire.Memory.Api;

public class QuestionnaireResults_v1
{
    private readonly BlobContainerClient _blobs;
    private readonly MemoryStorageService _storageService;
    private readonly IJwtTokenService _jwt;

    public QuestionnaireResults_v1(BlobServiceClient blobs, MemoryStorageService storageService, IJwtTokenService jwt)
    {
        _blobs = blobs.GetBlobContainerClient(AireConstants.Blobs.QuestionnaireResults);
        _blobs.CreateIfNotExists(publicAccessType: PublicAccessType.None);

        _storageService = storageService;
        _jwt = jwt;
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
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadChatHistory))
            return new ForbiddenResult();

        if (string.IsNullOrWhiteSpace(id))
            return new BadRequestResult();

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var query = await tables
            .QueryAsync<QuestionnaireResultsEntity>(x => x.PartitionKey == auth.UserId && x.QuestionnaireId == id);

        var results = await query.ToListAsync();
        var list = await results
            .ToAsyncEnumerable()
            .Select(async (QuestionnaireResultsEntity x, CancellationToken ct) => await x.ToModelAsync(_blobs, auth.UserKey))
            .ToListAsync();

        return new OkObjectResult(list);
    }

    [Function("PostQuestionnaireResults_v1")]
    [OpenApiOperation("postQuestionnaireResults", ["Questionnaire Results"], Summary = "Store new questionnaire results")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
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
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteChatHistory))
            return new ForbiddenResult();

        var results = await req.ReadJson<QuestionnaireResults>();
        if (results == null)
            return new BadRequestResult();

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        results.Id = Guid.NewGuid().ToString();
        results.Timestamp = DateTime.UtcNow;
        var entity = new QuestionnaireResultsEntity(auth.UserId, results.Id)
        {
            QuestionnaireId = results.QuestionnaireId,
            Timestamp = results.Timestamp
        };
        await entity.SaveToBlob(_blobs, results, auth.UserKey);

        var add = await tables.UpsertAsync(entity);
        if (!add)
            return new InternalServerErrorResult();

        return new OkObjectResult(results);
    }

    [Function("DeleteQuestionnaireResults_v1")]
    [OpenApiOperation("deleteQuestionnaireResults", ["Questionnaire Results"], Summary = "Retrieve questionnaire results")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
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
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.DeleteChatHistory))
            return new ForbiddenResult();

        if (string.IsNullOrWhiteSpace(id))
            return new BadRequestResult();

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var entity = await tables.RetrieveAsync<QuestionnaireResultsEntity>(auth.UserId, id);
        if (entity == null)
            return new NotFoundResult();

        await _blobs.DeleteBlobIfExistsAsync(entity.Id());

        var delete = await tables.DeleteAsync(entity);
        if (!delete)
            return new InternalServerErrorResult();

        return new NoContentResult();
    }
}
