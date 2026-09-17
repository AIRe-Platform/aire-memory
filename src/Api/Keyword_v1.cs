// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


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
using Aire.Sdk.Models.Resources;
using InternalErrorResult = System.Web.Http.InternalServerErrorResult;
using Aire.Memory.Helpers;
using Aire.Sdk.Auth.Extensions;
using Aire.Memory.Services;
using Aire.Sdk.Platform;

namespace Aire.Memory.Api;

public class Keyword_v1(MemoryStorageService storageService, IJwtTokenService jwt, ILogger<Keyword_v1> log)
{
    private readonly MemoryStorageService _storageService = storageService;
    private readonly IJwtTokenService _jwt = jwt;
    private readonly ILogger _log = log;

    [Function("QueryKeywords_v1")]
    [OpenApiOperation("queryKeywords", ["Keywords"], Summary = "Query keywords")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter(AirePlaformConstants.AireServiceKeyHeader,
        In = ParameterLocation.Header,
        Description = "Service authentication key",
        Required = false)]
    [OpenApiParameter(AirePlaformConstants.AireServicePlatformHeader,
        In = ParameterLocation.Header,
        Description = "Service authentication platform",
        Required = false)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(List<Keyword>), Description = "List of keyword objects")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> QueryKeywords(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/keywords")] HttpRequest req,
        FunctionContext context,
        [FromQuery] string? search)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        string? platform = null;
        if (auth != null)
        {
            if (!_jwt.CheckAuthorization(auth, AireScopes.ReadKeywords))
                return new ForbiddenResult();
            platform = auth.Platform;
        }
        else
        {
            if (!req.IsServiceRequest())
                return new UnauthorizedResult();
            platform = req.GetServiceRequestPlatform();
        }

        search = KeywordHelper.Sanitize(search);

        if (platform == null)
            return new UnauthorizedResult();

        var tables = await _storageService.GetTableStorageService(platform, req.GetTargetService());

        var query = await tables.All<KeywordValueEntity>();
        var results = query.Where(x => x.RowKey!.Contains(search));

        var list = results.Select(x =>
        {
            var model = x.ToModel();
            return model;
        });

        return new ObjectResult(list);
    }

    [Function("GetKeyword_v1")]
    [OpenApiOperation("getKeyword", ["Keywords"], Summary = "Get keyword stats")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
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
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadKeywords))
            return new ForbiddenResult();

        keyword = KeywordHelper.Sanitize(keyword);
        if (keyword.Length < 2)
            return new NotFoundResult();

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var pk = KeywordValueEntity.PartitionFromValue(keyword)!;
        var entity = await tables.RetrieveAsync<KeywordValueEntity>(pk, keyword);

        if (entity == null)
            return new NotFoundResult();

        var model = entity.ToModel();
        return new ObjectResult(model);
    }

    [Function("CreateKeyword_v1")]
    [OpenApiOperation("createKeyword", ["Keywords"], Summary = "Create a keyword")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
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
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteKeywords))
            return new ForbiddenResult();

        var body = await req.ReadJson<KeywordCreateRequest>();
        if (body == null)
            return new BadRequestResult();

        string keyword = KeywordHelper.Sanitize(body.Value);
        if (keyword.Length < 2)
            return new BadRequestResult();

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var pk = KeywordValueEntity.PartitionFromValue(keyword)!;
        var entity = await tables.RetrieveAsync<KeywordValueEntity>(pk, keyword);
        if (entity != null)
            return new ConflictResult();

        entity = new KeywordValueEntity(keyword);
        var result = await tables.UpsertAsync(entity);
        if (!result)
            return new InternalErrorResult();

        return new NoContentResult();
    }

    [Function("DeleteKeyword_v1")]
    [OpenApiOperation("deleteKeyword", ["Keywords"], Summary = "Delete an unused keyword")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
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
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.DeleteKeywords))
            return new ForbiddenResult();

        var pk = KeywordValueEntity.PartitionFromValue(keyword);
        if (pk == null)
            return new BadRequestResult();

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var entity = await tables.RetrieveAsync<KeywordValueEntity>(pk, keyword);
        if (entity == null)
            return new NotFoundResult();

        if (entity.QuestionnaireCount > 0 || entity.ContentCount > 0)
        {
            _log.LogWarning($"Will not delete keyword in use: '{keyword}'");
            return new BadRequestResult();
        }

        await tables.DeleteAsync<KeywordValueEntity>(pk, keyword);
        return new NoContentResult();
    }

    [Function("EditKeyword_v1")]
    [OpenApiOperation("EditKeyword", ["Keywords"], Summary = "Edit a keyword")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiRequestBody("application/json", typeof(KeywordCreateRequest), Description = "Edit Keyword request")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NoContent, Description = "Success")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The keyword does not exist")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid keyword")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> EditKeyword(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "v1/keyword/{keyword}")] HttpRequest req,
        FunctionContext context,
        string keyword)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteKeywords))
            return new ForbiddenResult();

        var pk = KeywordValueEntity.PartitionFromValue(keyword);
        if (pk == null)
            return new BadRequestResult();

        var body = await req.ReadJson<Keyword>();
        if (body == null)
            return new BadRequestResult();

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var entity = await tables.RetrieveAsync<KeywordValueEntity>(pk, keyword);
        if (entity == null)
            return new NotFoundResult();

        entity = new KeywordValueEntity(body);
        var result = await tables.UpsertAsync(entity);
        if (!result)
            return new InternalErrorResult();

        return new NoContentResult();
    }
}
