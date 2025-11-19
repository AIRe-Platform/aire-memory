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
using Aire.Sdk.Platform.Clients;

namespace Aire.Memory.Api;

public class Document_v1
{
    private readonly ITableStorageService _storage;
    private readonly IJwtTokenService _jwt;
    private readonly BlobContainerClient _blobs;
    private readonly IAireClientFactory _clientFactory;
    private readonly ILogger _log;

    public Document_v1(
        BlobServiceClient blobs,
        ITableStorageService storage,
        IJwtTokenService jwt,
        IAireClientFactory clientFactory,
        ILogger<Content_v1> log)
    {
        _blobs = blobs.GetBlobContainerClient(AireConstants.Blobs.Documents);
        _blobs.CreateIfNotExists(publicAccessType: PublicAccessType.None);

        _storage = storage;
        _jwt = jwt;
        _clientFactory = clientFactory;
        _log = log;
    }

    [Function("GetDocuments_v1")]
    [OpenApiOperation("getDocuments", ["Documents"], Summary = "Get a list of documents")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(List<DocumentMetadata>), Description = "List of document metadata")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> GetDocuments(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/documents")] HttpRequest req,
        FunctionContext context)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadDocument))
            return new ForbiddenResult();

        var all = await _storage.All<DocumentEntity>();
        var list = all.Select(x => x.ToModel()).ToList();
        return new OkObjectResult(list);
    }

    [Function("GetDocumentWithId_v1")]
    [OpenApiOperation("getDocumentWithId", ["Documents"], Summary = "Retrieve a document")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Document identifier", In = ParameterLocation.Path, Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(DocumentMetadata), Description = "Document metadata")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The document was not found.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid param")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> GetDocumentWithId(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/document/{id}")] HttpRequest req,
        FunctionContext context,
        string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadDocument))
            return new ForbiddenResult();

        if (string.IsNullOrWhiteSpace(id))
            return new BadRequestResult();

        var entity = await _storage.RetrieveAsync<DocumentEntity>(id);
        if (entity == null)
            return new NotFoundResult();

        var model = entity.ToModel();
        model.Url = SasHelper.GenerateSasUriString(_blobs, entity.Id());

        return new OkObjectResult(model);
    }

    public class DocumentUploadFormData
    {
        public byte[]? Document { get; set; }
        public DocumentMetadata? Metadata { get; set; }
    }

    [Function("PostDocument_v1")]
    [OpenApiOperation("postDocument", ["Documents"], Summary = "Store new document")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiRequestBody("multipart/form-data", typeof(DocumentUploadFormData), Description = "File upload with metadata", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(DocumentMetadata), Description = "Saved content")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> PostContent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/document")] HttpRequest req,
        FunctionContext context)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteDocument) || auth.Platform == null)
            return new ForbiddenResult();

        var formData = await req.ReadFormAsync();
        if (formData == null)
            return new BadRequestResult();

        formData.TryGetValue("metadata", out var jsonMetadata);

        var metadata = jsonMetadata.ToString().JsonToObject<DocumentMetadata>();
        if (metadata == null || metadata.Source.HasValue || req.Form.Files.Count != 1)
            return new BadRequestResult();

        var aiService = await _clientFactory.CreateAiClient(auth!.JwtEncodedToken);
        if (aiService == null)
        {
            _log.LogCritical("Default AI module not configured");
            return new InternalServerErrorResult();
        }

        // Create entity
        var file = req.Form.Files[0];
        var entity = new DocumentEntity(metadata);
        entity.FileName ??= file.FileName;

        // Create embedding
        var model = entity.ToModel();
        {
            var embedResult = await aiService.CreateDocumentEmbedding(file, model);
            var embedId = embedResult?.Ids?.FirstOrDefault();
            if (embedId == null)
            {
                _log.LogCritical("Failed to create embeddings");
                return new InternalServerErrorResult();
            }
            entity.EmbeddingId = embedId;
        }

        // Store blob
        {
            using var stream = file.OpenReadStream();
            var blobClient = _blobs.GetBlobClient(entity.Id());
            var blobHttpHeader = new BlobHttpHeaders { ContentType = file.ContentType };
            await blobClient.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = blobHttpHeader
            });
        }

        // Insert entity
        var result = await _storage.UpsertAsync(entity);
        if (!result)
            return new InternalServerErrorResult();

        model.Url = SasHelper.GenerateSasUriString(_blobs, entity.Id());
        return new OkObjectResult(model);
    }

    [Function("DeleteDocument_v1")]
    [OpenApiOperation("deleteDocument", ["Documents"], Summary = "Delete document")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Document identifier", In = ParameterLocation.Path, Required = true)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NoContent, Description = "Operation was successful")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The document was not found.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid parameter")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> DeleteDocument(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v1/document/{id}")] HttpRequest req,
        FunctionContext context,
        string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.DeleteDocument))
            return new ForbiddenResult();

        if (!Guid.TryParse(id, out Guid _))
            return new BadRequestResult();

        var entity = await _storage.RetrieveAsync<DocumentEntity>(id);
        if (entity == null)
            return new NotFoundResult();

        await _blobs.DeleteBlobIfExistsAsync(entity.Id());

        if (entity.EmbeddingId != null)
        {
            var aiService = await _clientFactory.CreateAiClient(auth.JwtEncodedToken);
            if (aiService == null)
            {
                _log.LogCritical("Default AI module not configured");
                return new InternalServerErrorResult();
            }

            bool result = await aiService.DeleteDocumentEmbedding(entity.EmbeddingId);
            if (!result)
            {
                _log.LogCritical("Failed to delete content embedding");
                return new InternalServerErrorResult();
            }
        }

        var delete = await _storage.DeleteAsync(entity);
        if (!delete)
            return new InternalServerErrorResult();

        return new NoContentResult();
    }
}
