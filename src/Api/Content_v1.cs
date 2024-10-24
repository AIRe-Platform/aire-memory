// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


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
        var list = new List<Content>();

        foreach (var entity in all)
        {
            var model = entity.ToModel();

            // Use the helper method to get the thumbnail URL
            model.ThumbnailUrl = await BlobHelper.GenerateThumbnailUrlIfExists(_blobs, entity.Id());
    
            list.Add(model);
        }

        return new OkObjectResult(list);
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
    public async Task<IActionResult> GetContentWithId(
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

        if (model.Type.IsBlobType())
        {
            model.Url = SasHelper.GenerateContentUriString(_blobs, entity.Id());
        }

        // Use the helper method to get the thumbnail URL
        model.ThumbnailUrl = await BlobHelper.GenerateThumbnailUrlIfExists(_blobs, entity.Id());

        return new OkObjectResult(model);
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

        var queryWords = query
            .Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(KeywordHelper.Sanitize)
            .Where(x => x.Length > 1)
            .Distinct();

        if (queryWords.Count() < 1)
            return new BadRequestResult();

        string filter = string.Join(" or ", queryWords.Select(x =>
        {
            var pk = KeywordIndexEntity.PartitionForResource(ResourceTypes.Content, x);
            return $"PartitionKey eq '{pk}'";
        }));

        var queryByKeywords = await _storage.QueryAsync<KeywordIndexEntity>(filter);
        var list = new List<Content>();
        await foreach (var index in queryByKeywords)
        {
            var entity = await _storage.RetrieveAsync<ContentEntity>(index.RowKey!);
            if (entity == null)
            {
                _log.LogWarning($"Content '{index.RowKey}' no longer exists.");
                continue;
            }

            var model = entity.ToModel();
            if (model.Type != ContentType.URL)
            {
                model.Url = SasHelper.GenerateContentUriString(_blobs, entity.Id());
            }
            list.Add(model);
        }

        return new OkObjectResult(list);
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
        var entity = new ContentEntity(content);

        // Handle content file if it's a blob type
        if (content.Type.IsBlobType())
        {
            if (req.Form.Files.Count == 0)
                return new BadRequestResult();

            //save the filename
            entity.FileName = req.Form.Files[0].FileName;
            
            // Get a reference to a blob with unique id for the content file
            BlobClient blobClient = _blobs.GetBlobClient(entity.Id());
            using var stream = req.Form.Files[0].OpenReadStream();

            var blobHttpHeader = new BlobHttpHeaders { ContentType = req.Form.Files[0].ContentType };
            await blobClient.UploadAsync(stream, new BlobUploadOptions { HttpHeaders = blobHttpHeader });
        }
        else if (string.IsNullOrEmpty(content.Url))
        {
            return new BadRequestResult();
        }

        // Handle thumbnail
        content.ThumbnailUrl = await BlobHelper.UploadThumbnailIfPresent(formData.Files, _blobs, entity.Id());
        if(content.ThumbnailUrl == "")
            await BlobHelper.RemoveThumbnailIfExists(_blobs, entity.Id());


        // Update keywords and add content to keyword index
        var words = await KeywordHelper.UpdateKeywords(
            _storage,
            ResourceTypes.Content,
            entity.Id(),
            [], // Assuming empty list for now
            content.Keywords ?? []);

        entity.Keywords = string.Join(",", words);

        // Insert content entity
        var result = await _storage.UpsertAsync(entity);
        if (!result)
            return new InternalServerErrorResult();

        var model = entity.ToModel();

        if (model.Type != ContentType.URL)
            model.Url = SasHelper.GenerateContentUriString(_blobs, entity.Id());

        return new OkObjectResult(model);
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

        // Update entity fields
        if (content.Name != null)
            entity.Name = content.Name;

        if (content.Description != null)
            entity.Description = content.Description;

        if (content.Hidden.HasValue)
            entity.Hidden = content.Hidden;

        if (content.AddThumbnail.HasValue)
            entity.AddThumbnail = content.AddThumbnail;

        if (content.Type.HasValue)
        {
            // Must match the original type
            if (content.Type != original.Type)
                return new BadRequestResult();
        }

        if (content.Type == ContentType.URL && !string.IsNullOrEmpty(content.Url))
        {
            entity.URI = content.Url;
        }

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

        // Handle thumbnail upload or removal
        content.ThumbnailUrl = await BlobHelper.UploadThumbnailIfPresent(formData.Files, _blobs, entity.Id());
        if(content.ThumbnailUrl == "")
            await BlobHelper.RemoveThumbnailIfExists(_blobs, entity.Id());

        // Handle other blobs if any
        if (req.Form.Files.Count > 0)
        {
            foreach (var file in req.Form.Files)
            {
                if (file.Name != "thumbnail")
                {
                    entity.FileName = file.FileName;
                    // Upload main content file to blob storage
                    var blobHttpHeader = new BlobHttpHeaders { ContentType = file.ContentType };
                    using var stream = file.OpenReadStream();

                    var blobClient = _blobs.GetBlobClient(entity.Id());
                    await blobClient.UploadAsync(stream, new BlobUploadOptions { HttpHeaders = blobHttpHeader });
                }
            }
        }

        // Apply edits
        var result = await _storage.UpsertAsync(entity);
        if (!result)
            return new InternalServerErrorResult();

        return new OkObjectResult(content);
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
        if (content.Type.IsBlobType())
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

    [Function("GetContentRating_v1")]
    [OpenApiOperation(
            operationId: "getContentRating",
            tags: ["content"],
            Summary = "Get user's content rating")]
    [OpenApiSecurity(
            schemeName: "bearer_auth",
            schemeType: SecuritySchemeType.Http,
            Scheme = OpenApiSecuritySchemeType.Bearer,
            BearerFormat = "JWT",
            Description = "User token")]
    [OpenApiParameter("id", Description = "Content identifier", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ContentRating), Description = "Content rating entity")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The Content was not found")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body or param")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    public async Task<IActionResult> GetContentRatingVote(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/content/{id}/rating")] HttpRequest req,
            FunctionContext context,
            string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.RateContent))
            return new UnauthorizedResult();

        if (!Guid.TryParse(id, out Guid _))
            return new BadRequestResult();

        var vote = await _storage.RetrieveAsync<ContentVoteEntity>(auth!.UserId, id);
        var rating = new ContentRating() {
            Vote = vote?.Value ?? 0
        };

        return new OkObjectResult(rating);
    }

    [Function("PostContentRating_v1")]
    [OpenApiOperation(
            operationId: "postContentRatingVote",
            tags: ["content"],
            Summary = "Cast user's content rating vote")]
    [OpenApiSecurity(
            schemeName: "bearer_auth",
            schemeType: SecuritySchemeType.Http,
            Scheme = OpenApiSecuritySchemeType.Bearer,
            BearerFormat = "JWT",
            Description = "User token")]
    [OpenApiParameter("id", Description = "Content identifier", Required = true)]
    [OpenApiRequestBody("application/json", typeof(ContentRating), Description = "Content rating", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Content), Description = "Updated content model")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The Content was not found")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body or param")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    public async Task<IActionResult> PostContentRatingVote(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/content/{id}/rating")] HttpRequest req,
            FunctionContext context,
            string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.RateContent))
            return new UnauthorizedResult();

        if (!Guid.TryParse(id, out Guid _))
            return new BadRequestResult();

        var rating = await req.ReadJson<ContentRating>();
        if (rating == null || !rating.Vote.HasValue)
            return new BadRequestResult();

        var entity = await _storage.RetrieveAsync<ContentEntity>(id);
        if (entity == null)
            return new NotFoundResult();

        var vote = await _storage.RetrieveAsync<ContentVoteEntity>(auth!.UserId, id);
        if (vote != null)
        {
            if (vote.Value > 0)
                entity.ThumbsUp -= 1;
            else if (vote.Value < 0)
                entity.ThumbsDown -= 1;
        }

        vote ??= new ContentVoteEntity(auth!.UserId, id);

        if (rating.Vote > 0)
        {
            entity.ThumbsUp += 1;
            vote.Value = 1;
        }
        else if (rating.Vote < 0)
        {
            entity.ThumbsDown += 1;
            vote.Value = -1;
        }
        else
        {
            vote.Value = 0;
        }

        var voteUpdate = await _storage.UpsertAsync(vote);
        if (!voteUpdate)
            return new InternalServerErrorResult();

        var contentUpdate = await _storage.UpsertAsync(entity);
        if (!contentUpdate)
            return new InternalServerErrorResult();

        return new OkObjectResult(entity.ToModel());
    }

    [Function("PostContentView_v1")]
    [OpenApiOperation(
            operationId: "postContentView",
            tags: ["content"],
            Summary = "Increment content view count")]
    [OpenApiSecurity(
            schemeName: "bearer_auth",
            schemeType: SecuritySchemeType.Http,
            Scheme = OpenApiSecuritySchemeType.Bearer,
            BearerFormat = "JWT",
            Description = "User token")]
    [OpenApiParameter("id", Description = "Content identifier", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Content), Description = "Updated content model")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The Content was not found")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body or param")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    public async Task<IActionResult> PostContentView(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/content/{id}/views")] HttpRequest req,
            FunctionContext context,
            string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadContent))
            return new UnauthorizedResult();

        if (!Guid.TryParse(id, out Guid _))
            return new BadRequestResult();

        var entity = await _storage.RetrieveAsync<ContentEntity>(id);
        if (entity == null)
            return new NotFoundResult();

        entity.Views += 1;

        var result = await _storage.UpsertAsync(entity);
        if (!result)
            return new InternalServerErrorResult();

        return new OkObjectResult(entity.ToModel());
    }
}
