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
using Aire.Sdk.Auth;
using Aire.Sdk.Azure;
using Aire.Sdk.Models.Resources;
using InternalErrorResult = System.Web.Http.InternalServerErrorResult;
using Aire.Memory.Helpers;
using Aire.Sdk.Auth.Extensions;

namespace Aire.Memory.Api;

public class Keyword_v1
{
    private readonly ITableStorageService _tables;
    private readonly IJwtTokenService _jwt;
    private readonly ILogger _log;

    public Keyword_v1(ITableStorageService tables, IJwtTokenService jwt, ILogger<Keyword_v1> log)
    {
        _tables = tables;
        _jwt = jwt;
        _log = log;
    }

    [Function("QueryKeywords_v1")]
    [OpenApiOperation(
        operationId: "queryKeywords",
        tags: ["Keywords"],
        Summary = "Query keywords")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(List<Keyword>), Description = "List of keyword objects")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    public async Task<IActionResult> QueryKeywords(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/keywords")] HttpRequest req,
        FunctionContext context,
        [FromQuery] string? search)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null && !req.IsServiceRequest())
            return new UnauthorizedResult();

        bool access_stats = _jwt.CheckAuthorization(auth, AireScopes.Keywords);

        string filter = "";
        search = KeywordHelper.Sanitize(search);

        if (search.Length > 0 && search.Length < 2)
        {
            char a = search[0], b = (char)(a + 1);
            filter = $"PartitionKey ge '{a}' and PartitionKey lt '{b}'";
        }
        else if (search.Length >= 2)
        {
            string pk = KeywordValueEntity.PartitionFromValue(search)!;
            string stop = search[..^1] + (char)(search[^1] + 1);
            filter = $"PartitionKey eq '{pk}' and RowKey ge '{search}' and RowKey lt '{stop}'";
        }

        var query = await _tables.QueryAsync<KeywordValueEntity>(filter);
        var results = await query.ToListAsync();
        var list = results.Select(x =>
        {
            var model = x.ToModel();
            if (!access_stats)
                model.Stats = null;
            return model;
        });

        return new ObjectResult(list);
    }

    [Function("GetKeyword_v1")]
    [OpenApiOperation(
        operationId: "getKeyword",
        tags: ["Keywords"],
        Summary = "Get keyword stats")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Keyword), Description = "Keyword details")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The keyword does not exist")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> GetKeyword(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/keyword/{keyword}")] HttpRequest req,
        FunctionContext context,
        string keyword)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.Keywords))
            return new ForbiddenResult();

        keyword = KeywordHelper.Sanitize(keyword);
        if (keyword.Length < 2)
            return new NotFoundResult();

        var pk = KeywordValueEntity.PartitionFromValue(keyword)!;
        var entity = await _tables.RetrieveAsync<KeywordValueEntity>(pk, keyword);

        if (entity == null)
            return new NotFoundResult();

        var model = entity.ToModel();
        return new ObjectResult(model);
    }

    [Function("CreateKeyword_v1")]
    [OpenApiOperation(
        operationId: "createKeyword",
        tags: ["Keywords"],
        Summary = "Create a keyword")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiRequestBody("application/json", typeof(KeywordCreateRequest), Description = "Keyword create request")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NoContent, Description = "Success")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Conflict, Description = "Already exists")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid keyword")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> CreateKeyword(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/keyword")] HttpRequest req,
        FunctionContext context)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.Keywords))
            return new ForbiddenResult();

        var body = await req.ReadJson<KeywordCreateRequest>();
        if(body == null)
            return new BadRequestResult();

        string keyword = KeywordHelper.Sanitize(body.Value);
        if (keyword.Length < 2)
            return new BadRequestResult();

        var pk = KeywordValueEntity.PartitionFromValue(keyword)!;
        var entity = await _tables.RetrieveAsync<KeywordValueEntity>(pk, keyword);
        if (entity != null)
            return new ConflictResult();

        entity = new KeywordValueEntity(keyword);
        var result = await _tables.UpsertAsync(entity);
        if (!result)
            return new InternalErrorResult();

        return new NoContentResult();
    }

    [Function("DeleteKeyword_v1")]
    [OpenApiOperation(
        operationId: "deleteKeyword",
        tags: ["Keywords"],
        Summary = "Delete an unused keyword")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NoContent, Description = "Success")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid request")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The keyword does not exist")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> DeleteKeyword(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v1/keyword/{keyword}")] HttpRequest req,
        FunctionContext context,
        string keyword)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.Keywords))
            return new ForbiddenResult();

        var pk = KeywordValueEntity.PartitionFromValue(keyword);
        if (pk == null)
            return new BadRequestResult();

        var entity = await _tables.RetrieveAsync<KeywordValueEntity>(pk, keyword);
        if (entity == null)
            return new NotFoundResult();

        if (entity.QuestionnaireCount > 0 || entity.ContentCount > 0)
        {
            _log.LogWarning($"Will not delete keyword in use: '{keyword}'");
            return new BadRequestResult();
        }

        await _tables.DeleteAsync<KeywordValueEntity>(pk, keyword);
        return new NoContentResult();
    }
}
