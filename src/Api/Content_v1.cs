using System.Net;
using System.Web.Http;
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
using Aire.Sdk.Platform.Clients;
using Aire.Sdk.Azure;
using Aire.Sdk.Helpers;

namespace Aire.Memory.Api;

public class Content_v1
{
    private readonly ITableStorageService _storage;
    private readonly IJwtTokenService _jwt;
    private readonly IAireClientFactory _clientFactory;
    private readonly ILogger _log;

    public Content_v1(
        ITableStorageService storage,
        IJwtTokenService jwt,
        IAireClientFactory clientFactory,
        ILogger<Content_v1> log)
    {
        _storage = storage;
        _jwt = jwt;
        _clientFactory = clientFactory;
        _log = log;
    }

    [Function("GetContents_v1")]
    [OpenApiOperation(
        operationId: "getContents",
        tags: ["content"],
        Summary = "Get a list of contents")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(List<Content>), Description = "List of contents")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> GetContents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/contents")] HttpRequest req,
        FunctionContext context)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadContent))
             return new ForbiddenResult();
       
        var all = await _storage.All<ContentEntity>();
        var list = all.Select(x => x.ToModel()).ToList();
        
        return new ObjectResult(list);
    }

    

    [Function("PostContent_v1")]
    [OpenApiOperation(
        operationId: "postContent",
        tags: ["questionnaire"],
        Summary = "Store new content")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiRequestBody("application/json", typeof(Content), Description = "A new content", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Content), Description = "Saved content")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> PostContent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/content")] HttpRequest req,
        FunctionContext context)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();
        
        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteContent))
            return new ForbiddenResult();

        var content = await req.ReadJson<Content>();
        if (content == null)
            return new BadRequestResult();

        content.Id = Guid.NewGuid();
        var entity = new ContentEntity(content);

        var add = await _storage.UpsertAsync(entity);
        if (!add)
            return new InternalServerErrorResult();

        return new ObjectResult(content);
    }


    [Function("PutContent_v1")]
    [OpenApiOperation(
            operationId: "putContent",
            tags: ["Content"],
            Summary = "Edit existing Content")]
    [OpenApiSecurity(
            schemeName: "bearer_auth",
            schemeType: SecuritySchemeType.Http,
            Scheme = OpenApiSecuritySchemeType.Bearer,
            BearerFormat = "JWT",
            Description = "User token")]
    [OpenApiParameter("id", Description = "Content identifier", Required = true)]
    [OpenApiRequestBody("application/json", typeof(Content), Description = "Content", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Content), Description = "Content")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The Content was not found")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body or param")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    public async Task<IActionResult> PutContent(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "v1/content/{id}")] HttpRequest req,
            FunctionContext context,
            string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteContent))
            return new UnauthorizedResult();

        if (!Guid.TryParse(id, out Guid contentId))
            return new BadRequestResult();

        var content = await req.ReadJson<Content>();
        if (content == null)
            return new BadRequestResult();

        var entity = await _storage.RetrieveAsync<ContentEntity>(id);
        if (entity == null)
            return new NotFoundResult();

        // Update entity data
        if (content.Name != null)
            entity.Name = content.Name;

        if (content.Description != null)
            entity.Description =  content.Description;

        if (content.Hidden != null)
            entity.Hidden = content.Hidden;

        if (content.Type != null)
            entity.Type = content.Type;

        if (content.Url != null)
            entity.Url = content.Url;

        if (content.ViewsCount != null)
            entity.ViewsCount = content.ViewsCount;

        if (content.ViewersRating != null)
            entity.ViewersRating = content.ViewersRating;

        if (content.InjuredType != null)
            entity.InjuredType = content.InjuredType;

        if (content.Age != null)
            entity.Age = content.Age;

        if (content.Gender != null)
            entity.Gender = content.Gender;

        // Apply edits

        var save = await _storage.UpsertAsync(entity);
        if (!save)
            return new InternalServerErrorResult();

        return new ObjectResult(content);
    }


    [Function("DeleteContent_v1")]
    [OpenApiOperation(
        operationId: "deleteContentWithId",
        tags: ["content"],
        Summary = "Delete a content")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Content identifier", In = ParameterLocation.Path, Required = true)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NoContent, Description = "Operation was successful")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The content was not found.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid parameter")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> DeleteContent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v1/content/{id}")] HttpRequest req,
        FunctionContext context,
        string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.DeleteContent))
            return new ForbiddenResult();

        if (!Guid.TryParse(id, out Guid contentId))
            return new BadRequestResult();

        var entity = await _storage.RetrieveAsync<ContentEntity>(id);
        if (entity == null)
            return new NotFoundResult();
    
        var delete = await _storage.DeleteAsync(entity);
        if (!delete)
            return new InternalServerErrorResult();
        
        return new NoContentResult();
    }
}
