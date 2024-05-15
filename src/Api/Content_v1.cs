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
using Aire.Sdk.Azure;
using Aire.Sdk.Helpers;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Aire.Memory.Helpers;

namespace Aire.Memory.Api;

public class Content_v1
{
    private readonly ITableStorageService _storage;
    private readonly IJwtTokenService _jwt;
    private readonly ILogger _log;
    private readonly BlobContainerClient _blobs;

    public Content_v1(
        BlobServiceClient blobs,
        ITableStorageService storage,
        IJwtTokenService jwt,
        ILogger<Content_v1> log)
    {
        _blobs = blobs.GetBlobContainerClient(AireConstants.Blobs.Contents);
        _blobs.CreateIfNotExists(publicAccessType: PublicAccessType.None);

        _storage = storage;
        _jwt = jwt;
        _log = log;

    }

    [Function("GetContents_v1")]
    [OpenApiOperation(
        operationId: "getContents",
        tags: ["content"],
        Summary = "Get a list of content")]
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
        var list = all.Select(x =>
        {
            var model = x.ToModel();

            if (model.Type != ContentType.URL)
            {
                var blobSasBuilder = new BlobSasBuilder()
                {
                    BlobContainerName = AireConstants.Blobs.Contents,
                    ExpiresOn = DateTime.UtcNow.AddMinutes(15),
                };

                BlobClient blobClient = _blobs.GetBlobClient(x.Id());
                blobSasBuilder.SetPermissions(BlobSasPermissions.Read);

                var sasUri = blobClient.GenerateSasUri(blobSasBuilder);
                model.Url = sasUri.AbsoluteUri;
            }

            return model;
        });

        return new ObjectResult(list);
    }

    [Function("GetContentWithId_v1")]
    [OpenApiOperation(
        operationId: "getContentWithId",
        tags: ["content"],
        Summary = "Retrieve a content")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Content identifier", In = ParameterLocation.Path, Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Content), Description = "A content")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The content was not found.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid param")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> GetContenteWithId(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/content/{id}")] HttpRequest req,
        FunctionContext context,
        string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadContent))
            return new ForbiddenResult();

        if (string.IsNullOrWhiteSpace(id))
            return new BadRequestResult();

        var entity = await _storage.RetrieveAsync<ContentEntity>(id);
        if (entity == null)
            return new NotFoundResult();

        var model = entity.ToModel();
        return new ObjectResult(model);
    }

    [Function("SearchContent_v1")]
    [OpenApiOperation(
        operationId: "searchContent",
        tags: ["content"],
        Summary = "Search for content"
    )]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("query",
        CollectionDelimiter = OpenApiParameterCollectionDelimiterType.Comma,
        In = ParameterLocation.Query,
        Required = true,
        Description = "List of keywords separated by commas")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(List<Content>), Description = "List of found contents")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "No results")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Missing query")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> SearchContent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/content")] HttpRequest req,
        FunctionContext context,
        [FromQuery] string query)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadContent))
            return new ForbiddenResult();

        if (string.IsNullOrWhiteSpace(query))
            return new BadRequestResult();

        var queryWords = query.Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var all = await _storage.All<ContentEntity>();
        var list = new List<Content>();
        all.ForEach(x =>
        {
            var model = x.ToModel();

            if (model.Keywords != null && model.Keywords.Any(item => queryWords.Contains(item)))
            {
                if (model.Type != ContentType.URL)
                {
                    var blobSasBuilder = new BlobSasBuilder()
                    {
                        BlobContainerName = AireConstants.Blobs.Contents,
                        ExpiresOn = DateTime.UtcNow.AddMinutes(15),
                    };

                    BlobClient blobClient = _blobs.GetBlobClient(x.Id());
                    blobSasBuilder.SetPermissions(BlobSasPermissions.Read);

                    var sasUri = blobClient.GenerateSasUri(blobSasBuilder);
                    model.Url = sasUri.AbsoluteUri;
                }
                list.Add(model);
            }
        });

        return new ObjectResult(list);
    }


    [Function("PostContent_v1")]
    [OpenApiOperation(
        operationId: "postContent",
        tags: ["content"],
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

        var formData = await req.ReadFormAsync();
        if (formData == null)
            return new BadRequestResult();

        formData.TryGetValue("json", out var json);

        var content = json.ToString().JsonToObject<Content>();
        if (content == null || content.Id.HasValue || !content.Type.HasValue)
            return new BadRequestResult();

        // Create entity and store blob if present
        // This creates automatically new GUID for the content
        var entity = new ContentEntity(content);

        if (content.Type.Value.IsBlobType())
        {
            if (req.Form.Files.Count != 1)
                return new BadRequestResult();

            // Get a reference to a blob with unique id
            BlobClient blobClient = _blobs.GetBlobClient(entity.Id());
            using var stream = req.Form.Files[0].OpenReadStream();
            await blobClient.UploadAsync(stream);
            content.Url = blobClient.Uri.AbsoluteUri;
        }
        else if (string.IsNullOrEmpty(content.Url))
        {
            return new BadRequestResult();
        }

        // Update keywords and add content to keyword index
        var words = await KeywordHelper.UpdateKeywords(
            _storage,
            ResourceTypes.Content,
            entity.Id(),
            [],
            content.Keywords ?? []);

        entity.Keywords = string.Join(",", words);

        // TODO: Create embedding

        // Insert content entity
        var result = await _storage.UpsertAsync(entity);
        if (!result)
            return new InternalServerErrorResult();

        return new ObjectResult(entity.ToModel());
    }


    [Function("PutContent_v1")]
    [OpenApiOperation(
            operationId: "putContent",
            tags: ["Content"],
            Summary = "Edit existing content")]
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

        if (!Guid.TryParse(id, out Guid _))
            return new BadRequestResult();

        var entity = await _storage.RetrieveAsync<ContentEntity>(id);
        if (entity == null)
            return new NotFoundResult();
        var original = entity.ToModel();

        var formData = await req.ReadFormAsync();
        if (formData == null)
            return new BadRequestResult();

        formData.TryGetValue("json", out var json);

        var content = json.ToString().JsonToObject<Content>();
        if (content == null)
            return new BadRequestResult();

        // Update entity

        if (content.Name != null)
            entity.Name = content.Name;

        if (content.Description != null)
            entity.Description = content.Description;

        if (content.Hidden.HasValue)
            entity.Hidden = content.Hidden;

        if (content.Type.HasValue)
        {
            // Must match the original type
            if (content.Type != original.Type)
                return new BadRequestResult();
        }

        if (content.Type == ContentType.URL)
        {
            if (string.IsNullOrEmpty(content.Url))
                entity.URI = content.Url;
        }

        if (content.ViewsCount != null)
            entity.ViewsCount = content.ViewsCount;

        if (content.ViewersRating != null)
            entity.ViewersRating = content.ViewersRating;

        if (content.Keywords != null)
        {
            // Update keywords and edit content keyword index
            var words = await KeywordHelper.UpdateKeywords(
                _storage, 
                ResourceTypes.Content,
                entity.Id(),
                original.Keywords ?? [], 
                content.Keywords);

            entity.Keywords = string.Join(",", words);
        }

        // TODO: Update embedding

        // Got new blob?
        if (req.Form.Files.Count > 0)
        {
            if (original.Type == ContentType.URL)
                return new BadRequestResult();

            using var stream = req.Form.Files[0].OpenReadStream();
            var blobClient = _blobs.GetBlobClient(entity.Id());
            await blobClient.UploadAsync(stream, true);
        }

        // Apply edits
        var result = await _storage.UpsertAsync(entity);
        if (!result)
            return new InternalServerErrorResult();

        return new ObjectResult(content);
    }


    [Function("DeleteContent_v1")]
    [OpenApiOperation(
        operationId: "deleteContent",
        tags: ["content"],
        Summary = "Delete content")]
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

        if (!Guid.TryParse(id, out Guid _))
            return new BadRequestResult();

        var entity = await _storage.RetrieveAsync<ContentEntity>(id);
        if (entity == null)
            return new NotFoundResult();

        var content = entity.ToModel();
        if (content.Type!.Value.IsBlobType())
        {
            await _blobs.DeleteBlobIfExistsAsync(entity.Id());
        }

        // Update keywords and removw content from keyword index
        await KeywordHelper.UpdateKeywords(
            _storage, 
            ResourceTypes.Content,
            entity.Id(),
            content.Keywords ?? [], 
            []);

        // TODO: Remove embedding

        var delete = await _storage.DeleteAsync(entity);
        if (!delete)
            return new InternalServerErrorResult();

        return new NoContentResult();
    }
}
