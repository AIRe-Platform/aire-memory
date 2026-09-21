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
using Aire.Sdk.Helpers;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Aire.Memory.Helpers;
using Aire.Sdk.Platform.Clients;
using Aire.Sdk.Models.Platform;
using Aire.Sdk.Platform;
using Aire.Memory.Services;
using Aire.Sdk.Auth.Extensions;

namespace Aire.Memory.Api;

public class Content_v1
{
    private readonly MemoryStorageService _storageService;
    private readonly IJwtTokenService _jwt;
    private readonly BlobContainerClient _blobs;
    private readonly IAirePlatformService _platform;
    private readonly IAireClientFactory _clientFactory;
    private readonly IAireModuleSettingsService _moduleConfigService;
    private readonly ILogger _log;

    public Content_v1(
        BlobServiceClient blobs,
        MemoryStorageService storageService,
        IJwtTokenService jwt,
        IAirePlatformService platformService,
        IAireClientFactory clientFactory,
        IAireModuleSettingsService moduleConfigService,
        ILogger<Content_v1> log)
    {
        _blobs = blobs.GetBlobContainerClient(AireConstants.Blobs.Contents);
        _blobs.CreateIfNotExists(publicAccessType: PublicAccessType.None);

        _storageService = storageService;
        _jwt = jwt;
        _platform = platformService;
        _clientFactory = clientFactory;
        _moduleConfigService = moduleConfigService;
        _log = log;
    }

    [Function("GetContents_v1")]
    [OpenApiOperation("getContents", ["Content"], Summary = "Get a list of content")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
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
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadContent))
            return new ForbiddenResult();

        var storage = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var all = await storage.All<ContentEntity>();
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
    [OpenApiOperation("getContentWithId", ["Content"], Summary = "Retrieve a content")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Content identifier", In = ParameterLocation.Path, Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Content), Description = "A content")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The content was not found or is hidden")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid param")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> GetContentWithId(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/content/{id}")] HttpRequest req,
        FunctionContext context,
        string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadContent))
            return new ForbiddenResult();

        if (string.IsNullOrWhiteSpace(id))
            return new BadRequestResult();

        bool showHidden = _jwt.CheckAuthorization(auth, AireScopes.WriteContent);
        var storage = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var entity = await storage.RetrieveAsync<ContentEntity>(id);
        if (entity == null || (entity.Hidden == true && !showHidden))
            return new NotFoundResult();

        var model = entity.ToModel();
        if (model.Type.IsBlobType())
        {
            model.Url = SasHelper.GenerateSasUriString(_blobs, entity.Id());
        }

        // Use the helper method to get the thumbnail URL
        model.ThumbnailUrl = await BlobHelper.GenerateThumbnailUrlIfExists(_blobs, entity.Id());

        return new OkObjectResult(model);
    }


    [Function("SearchContent_v1")]
    [OpenApiOperation("searchContent", ["Content"], Summary = "Search for content")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("query",
        CollectionDelimiter = OpenApiParameterCollectionDelimiterType.Comma,
        In = ParameterLocation.Query,
        Required = true,
        Description = "List of keywords separated by commas")]
    [OpenApiParameter("lang", In = ParameterLocation.Query, Required = false, Description = "Language filter")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(List<Content>), Description = "List of found contents")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "No results")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Missing query")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> SearchContent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/content")] HttpRequest req,
        FunctionContext context,
        [FromQuery] string query,
        [FromQuery] string? lang = null)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth?.Platform == null)
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

        var storage = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var queryByKeywords = await storage.QueryAsync<KeywordIndexEntity>(filter);
        var list = new List<Content>();
        await foreach (var index in queryByKeywords)
        {
            var entity = await storage.RetrieveAsync<ContentEntity>(index.RowKey!);
            if (entity == null)
            {
                _log.LogWarning($"Content '{index.RowKey}' no longer exists.");
                continue;
            }

            if (lang != null && entity.Language != lang)
            {
                if (entity.Language == null)
                    _log.LogWarning($"Content '{index.RowKey}' is missing language tag.");
                else
                    continue;
            }

            if (entity.Hidden == true)
            {
                // Skip hidden content
                continue;
            }

            var model = entity.ToModel();
            if (model.Type != ContentType.URL)
            {
                model.Url = SasHelper.GenerateSasUriString(_blobs, entity.Id());
            }

            list.Add(model);
        }

        return new OkObjectResult(list);
    }

    [Function("PostContent_v1")]
    [OpenApiOperation("postContent", ["Content"], Summary = "Store new content")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiRequestBody("application/json", typeof(Content), Description = "A new content", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Content), Description = "Saved content")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    [OpenApiResponseWithoutBody(HttpStatusCode.UnprocessableEntity, Description = "Invalid content type")]
    [OpenApiResponseWithoutBody(HttpStatusCode.RequestEntityTooLarge, Description = "Uploaded content is too large")]
    public async Task<IActionResult> PostContent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/content")] HttpRequest req,
        FunctionContext context)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteContent) || auth.Platform == null)
            return new ForbiddenResult();

        var targetService = req.GetTargetService();
        var storage = await _storageService.GetTableStorageService(auth.Platform, targetService);

        var formData = await req.ReadFormAsync();
        if (formData == null)
            return new BadRequestResult();

        formData.TryGetValue("json", out var json);

        var content = json.ToString().JsonToObject<Content>();
        if (content == null || content.Id.HasValue || !content.Type.HasValue)
            return new BadRequestResult();

        var aiModule = await _platform.GetPlatformModule(auth.Platform, ModuleType.AI, null);
        if (aiModule == null)
        {
            _log.LogCritical("Default AI module not configured");
            return new InternalServerErrorResult();
        }

        var aiService = await _clientFactory.CreateAiClient(aiModule, asService: true);
        var aiDatabase = await _moduleConfigService.Get<string>(
            auth.Platform, ModuleType.Memory, req.GetTargetService(),
            ModuleSettings.Memory_VectorDbName);

        if (aiDatabase == null)
        {
            _log.LogCritical("Missing '{key}' module configuration", ModuleSettings.Memory_VectorDbName);
            return new InternalServerErrorResult();
        }

        // Create entity and store blob if present
        var entity = new ContentEntity(content);

        // Iterate over form files to separate main content and thumbnail
        if (req.Form.Files.Count > 0)
        {
            var thumbnail = req.Form.Files.FirstOrDefault(x => x.Name == "thumbnail");
            var blob = req.Form.Files.FirstOrDefault(x => x.Name != "thumbnail");
            // Ignore extra files

            if (thumbnail != null)
            {
                if (thumbnail.Length > AireConstants.Limits.ThumbnailSizeLimit)
                    return new StatusCodeResult((int)HttpStatusCode.RequestEntityTooLarge);

                entity.ThumbnailFileName = thumbnail.FileName;
                content.ThumbnailUrl = await BlobHelper.UploadThumbnail(thumbnail, _blobs, entity.Id());
            }

            if (blob != null)
            {
                if (!content.Type.IsBlobType())
                    return new BadRequestResult();

                if (!BlobHelper.IsValidContentType(content.Type.Value, blob))
                    return new UnprocessableEntityResult();

                // Handle main content file upload if the content type is blob-based
                if (blob.Length > AireConstants.Limits.ContentSizeLimit)
                    return new StatusCodeResult((int)HttpStatusCode.RequestEntityTooLarge);

                entity.FileName = blob.FileName;
                content.Url = await BlobHelper.UploadBlobAsync(blob, _blobs, entity.Id());
            }
        }

        // Either just uploaded blob url or url provided with content required
        if (string.IsNullOrEmpty(content.Url))
        {
            return new BadRequestResult();
        }

        var keywords = KeywordHelper.Sanitize(content.Keywords ?? []);
        entity.Keywords = string.Join(",", keywords);

        var model = entity.ToModel();
        {
            var embedResult = await aiService.CreateContentEmbedding(aiDatabase, model);
            var embedId = embedResult?.Ids?.FirstOrDefault();
            if (embedId == null)
            {
                _log.LogCritical("Failed to create embeddings for the questionnaire");
                return new InternalServerErrorResult();
            }
            entity.EmbeddingId = embedId;
        }

        // Insert content entity
        var result = await storage.UpsertAsync(entity);
        if (!result)
            return new InternalServerErrorResult();

        // Update keyword index
        await KeywordHelper.UpdateKeywords(storage, ResourceTypes.Content, entity.Id(), [], keywords);

        if (model.Type != ContentType.URL)
            model.Url = SasHelper.GenerateSasUriString(_blobs, entity.Id());

        return new OkObjectResult(model);
    }


    [Function("PutContent_v1")]
    [OpenApiOperation("putContent", ["Content"], Summary = "Edit existing content")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Content identifier", Required = true)]
    [OpenApiRequestBody("application/json", typeof(Content), Description = "Content", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Content), Description = "Content")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The Content was not found")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body or param")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.UnprocessableEntity, Description = "Invalid content type")]
    [OpenApiResponseWithoutBody(HttpStatusCode.RequestEntityTooLarge, Description = "Uploaded content is too large")]
    public async Task<IActionResult> PutContent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "v1/content/{id}")] HttpRequest req,
        FunctionContext context,
        string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteContent) || auth.Platform == null)
            return new ForbiddenResult();

        if (!Guid.TryParse(id, out Guid _))
            return new BadRequestResult();

        var aiModule = await _platform.GetPlatformModule(auth.Platform, ModuleType.AI, null);
        if (aiModule == null)
        {
            _log.LogCritical("Default AI module not configured");
            return new InternalServerErrorResult();
        }

        var targetService = req.GetTargetService();
        var storage = await _storageService.GetTableStorageService(auth.Platform, targetService);

        var aiService = await _clientFactory.CreateAiClient(aiModule, asService: true);
        var aiDatabase = await _moduleConfigService.Get<string>(
            auth.Platform, ModuleType.Memory, targetService,
            ModuleSettings.Memory_VectorDbName);

        if (aiDatabase == null)
        {
            _log.LogCritical("Missing '{key}' module configuration", ModuleSettings.Memory_VectorDbName);
            return new InternalServerErrorResult();
        }

        var entity = await storage.RetrieveAsync<ContentEntity>(id);
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

        if (content.Language != null)
            entity.Language = content.Language;

        if (content.Hidden.HasValue)
            entity.Hidden = content.Hidden;

        if (content.AddThumbnail.HasValue)
            entity.AddThumbnail = content.AddThumbnail;

        if (content.Copyright != null)
            entity.Copyright = content.Copyright;

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

        string[]? keywords = null;
        if (content.Keywords != null)
        {
            keywords = KeywordHelper.Sanitize(content.Keywords);
            entity.Keywords = string.Join(",", keywords);
        }

        // Handle uploads
        if (req.Form.Files.Count > 0)
        {
            var thumbnail = req.Form.Files.FirstOrDefault(x => x.Name == "thumbnail");
            var blob = req.Form.Files.FirstOrDefault(x => x.Name != "thumbnail");
            // Ignore any extra files

            if (thumbnail != null)
            {
                if (thumbnail.Length > AireConstants.Limits.ThumbnailSizeLimit)
                    return new StatusCodeResult((int)HttpStatusCode.RequestEntityTooLarge);

                await BlobHelper.RemoveThumbnailIfExists(_blobs, entity.Id());

                entity.ThumbnailFileName = thumbnail.FileName;
                content.ThumbnailFileName = await BlobHelper.UploadThumbnail(thumbnail, _blobs, entity.Id());
            }

            if (blob != null)
            {
                if (!content.Type.HasValue || !content.Type.IsBlobType())
                    return new BadRequestResult();

                if (!BlobHelper.IsValidContentType(content.Type.Value, blob))
                    return new UnprocessableEntityResult();

                if (blob.Length > AireConstants.Limits.ContentSizeLimit)
                    return new StatusCodeResult((int)HttpStatusCode.RequestEntityTooLarge);

                await _blobs.DeleteBlobIfExistsAsync(entity.Id());

                entity.FileName = blob.FileName;
                await BlobHelper.UploadBlobAsync(blob, _blobs, entity.Id());
            }
        }

        // Delete existing thumbnail if URL set to empty (no new thumbnail uploaded)
        if (content.ThumbnailUrl == "")
        {
            await BlobHelper.RemoveThumbnailIfExists(_blobs, entity.Id());
            entity.ThumbnailFileName = "";
        }

        // Update embedding
        {
            if (entity.EmbeddingId != null)
            {
                bool result = await aiService.DeleteContentEmbedding(aiDatabase, entity.EmbeddingId);
                if (!result)
                {
                    _log.LogCritical("Failed to delete content embedding");
                    return new InternalServerErrorResult();
                }
            }

            var embed = await aiService.CreateContentEmbedding(aiDatabase, entity.ToModel());
            var embedId = embed?.Ids?.FirstOrDefault();
            if (embedId == null)
            {
                _log.LogCritical("Failed to create embedding for the content");
                return new InternalServerErrorResult();
            }
            entity.EmbeddingId = embedId;
        }

        // Apply edits
        {
            var result = await storage.UpsertAsync(entity);
            if (!result)
                return new InternalServerErrorResult();
        }

        // Update keyword index
        if (keywords != null)
        {
            await KeywordHelper.UpdateKeywords(
                storage, ResourceTypes.Content, entity.Id(), original.Keywords ?? [], keywords);
        }

        return new OkObjectResult(entity.ToModel());
    }

    [Function("DeleteContent_v1")]
    [OpenApiOperation("deleteContent", ["Content"], Summary = "Delete content")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
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
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.DeleteContent) || auth.Platform == null)
            return new ForbiddenResult();

        if (!Guid.TryParse(id, out Guid _))
            return new BadRequestResult();

        var targetService = req.GetTargetService();
        var storage = await _storageService.GetTableStorageService(auth.Platform, targetService);

        var entity = await storage.RetrieveAsync<ContentEntity>(id);
        if (entity == null)
            return new NotFoundResult();

        var content = entity.ToModel();
        if (content.Type.IsBlobType())
        {
            await _blobs.DeleteBlobIfExistsAsync(entity.Id());
        }

        await BlobHelper.RemoveThumbnailIfExists(_blobs, entity.Id());

        if (entity.EmbeddingId != null)
        {
            var aiModule = await _platform.GetPlatformModule(auth.Platform, ModuleType.AI, null);
            if (aiModule == null)
            {
                _log.LogCritical("Default AI module not configured");
                return new InternalServerErrorResult();
            }

            var aiService = await _clientFactory.CreateAiClient(aiModule, asService: true);
            var aiDatabase = await _moduleConfigService.Get<string>(
                auth.Platform, ModuleType.Memory, targetService,
                ModuleSettings.Memory_VectorDbName);

            if (aiDatabase == null)
            {
                _log.LogCritical("Missing '{key}' module configuration", ModuleSettings.Memory_VectorDbName);
                return new InternalServerErrorResult();
            }

            bool result = await aiService.DeleteContentEmbedding(aiDatabase, entity.EmbeddingId);
            if (!result)
            {
                _log.LogCritical("Failed to delete content embedding");
                return new InternalServerErrorResult();
            }
        }

        var delete = await storage.DeleteAsync(entity);
        if (!delete)
            return new InternalServerErrorResult();

        // Update keyword index
        await KeywordHelper.UpdateKeywords(
            storage, ResourceTypes.Content, entity.Id(), content.Keywords ?? [], []);

        return new NoContentResult();
    }

    [Function("GetContentRating_v1")]
    [OpenApiOperation("getContentRating", ["Content"], Summary = "Get user's content rating")]
    [OpenApiSecurity(
            schemeName: "bearer_auth",
            schemeType: SecuritySchemeType.Http,
            Scheme = OpenApiSecuritySchemeType.Bearer,
            BearerFormat = "JWT",
            Description = "User token")]
    [OpenApiParameter("id", Description = "Content identifier", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ContentRating), Description = "Content rating entity")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The content was not found")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body or param")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    public async Task<IActionResult> GetContentRatingVote(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/content/{id}/rating")] HttpRequest req,
            FunctionContext context,
            string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.RateContent))
            return new ForbiddenResult();

        if (!Guid.TryParse(id, out Guid _))
            return new BadRequestResult();

        var storage = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var vote = await storage.RetrieveAsync<ContentVoteEntity>(auth!.UserId, id);
        var rating = new ContentRating()
        {
            Vote = vote?.Value ?? 0
        };

        return new OkObjectResult(rating);
    }

    [Function("PostContentRating_v1")]
    [OpenApiOperation("postContentRatingVote", ["Content"], Summary = "Cast user's content rating vote")]
    [OpenApiSecurity(
            schemeName: "bearer_auth",
            schemeType: SecuritySchemeType.Http,
            Scheme = OpenApiSecuritySchemeType.Bearer,
            BearerFormat = "JWT",
            Description = "User token")]
    [OpenApiParameter("id", Description = "Content identifier", Required = true)]
    [OpenApiRequestBody("application/json", typeof(ContentRating), Description = "Content rating", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Content), Description = "Updated content model")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "Content was not found")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body or param")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> PostContentRatingVote(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/content/{id}/rating")] HttpRequest req,
            FunctionContext context,
            string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.RateContent))
            return new ForbiddenResult();

        if (!Guid.TryParse(id, out Guid _))
            return new BadRequestResult();

        var rating = await req.ReadJson<ContentRating>();
        if (rating == null || !rating.Vote.HasValue)
            return new BadRequestResult();

        var storage = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var entity = await storage.RetrieveAsync<ContentEntity>(id);
        if (entity == null)
            return new NotFoundResult();

        var vote = await storage.RetrieveAsync<ContentVoteEntity>(auth!.UserId, id);
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

        var voteUpdate = await storage.UpsertAsync(vote);
        if (!voteUpdate)
            return new InternalServerErrorResult();

        var contentUpdate = await storage.UpsertAsync(entity);
        if (!contentUpdate)
            return new InternalServerErrorResult();

        return new OkObjectResult(entity.ToModel());
    }

    [Function("PostContentView_v1")]
    [OpenApiOperation("postContentView", ["Content"], Summary = "Increment content view count")]
    [OpenApiSecurity(
            schemeName: "bearer_auth",
            schemeType: SecuritySchemeType.Http,
            Scheme = OpenApiSecuritySchemeType.Bearer,
            BearerFormat = "JWT",
            Description = "User token")]
    [OpenApiParameter("id", Description = "Content identifier", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Content), Description = "Updated content model")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "Content was not found")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body or param")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> PostContentView(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/content/{id}/views")] HttpRequest req,
            FunctionContext context,
            string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadContent))
            return new UnauthorizedResult();

        if (!Guid.TryParse(id, out Guid _))
            return new BadRequestResult();

        var storage = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var entity = await storage.RetrieveAsync<ContentEntity>(id);
        if (entity == null)
            return new NotFoundResult();

        entity.Views += 1;

        var result = await storage.UpsertAsync(entity);
        if (!result)
            return new InternalServerErrorResult();

        return new OkObjectResult(entity.ToModel());
    }
}
