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
using Azure.Storage.Queues;

namespace Aire.Memory.Api;

public class Document_v1
{
    private readonly MemoryStorageService _storageService;
    private readonly IJwtTokenService _jwt;
    private readonly BlobContainerClient _blobs;
    private readonly IAirePlatformService _platform;
    private readonly IAireClientFactory _clientFactory;
    private readonly IAireModuleSettingsService _moduleConfigService;
    private readonly QueueClient _documentQueue;
    private readonly ILogger _log;

    public Document_v1(
        BlobServiceClient blobs,
        QueueServiceClient queues,
        MemoryStorageService storageService,
        IJwtTokenService jwt,
        IAirePlatformService platformService,
        IAireClientFactory clientFactory,
        IAireModuleSettingsService moduleConfigService,
        ILogger<Content_v1> log)
    {
        _blobs = blobs.GetBlobContainerClient(AireConstants.Blobs.Documents);
        _blobs.CreateIfNotExists(publicAccessType: PublicAccessType.None);

        _documentQueue = queues.GetQueueClient(AireConstants.Queues.DocumentEmbed);
        _documentQueue.CreateIfNotExists();

        _storageService = storageService;
        _jwt = jwt;
        _platform = platformService;
        _clientFactory = clientFactory;
        _moduleConfigService = moduleConfigService;
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
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadDocument))
            return new ForbiddenResult();

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var all = await tables.All<DocumentEntity>();
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
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadDocument))
            return new ForbiddenResult();

        if (string.IsNullOrWhiteSpace(id))
            return new BadRequestResult();

        var tables = await _storageService.GetTableStorageService(auth.Platform, req.GetTargetService());

        var entity = await tables.RetrieveAsync<DocumentEntity>(id);
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
    [OpenApiResponseWithoutBody(HttpStatusCode.UnprocessableEntity, Description = "Invalid document type")]
    [OpenApiResponseWithoutBody(HttpStatusCode.RequestEntityTooLarge, Description = "Document is too large")]
    public async Task<IActionResult> PostDocument(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/document")] HttpRequest req,
        FunctionContext context)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteDocument))
            return new ForbiddenResult();

        var formData = await req.ReadFormAsync();
        if (formData == null)
            return new BadRequestResult();

        formData.TryGetValue("metadata", out var jsonMetadata);

        var metadata = jsonMetadata.ToString().JsonToObject<DocumentMetadata>();
        if (metadata == null || metadata.Source.HasValue || req.Form.Files.Count != 1)
            return new BadRequestResult();

        var targetService = req.GetTargetService();
        var tables = await _storageService.GetTableStorageService(auth.Platform, targetService);

        // Will queue the doc for processing
        metadata.Status = DocumentStatus.Queued;

        // Create entity
        var file = req.Form.Files[0];
        var entity = new DocumentEntity(metadata);

        if (!ContentHelper.IsValidContentType(ContentType.Document, file))
            return new UnprocessableEntityResult();

        if (file.Length > AireConstants.Limits.DocumentSizeLimit)
            return new StatusCodeResult((int)HttpStatusCode.RequestEntityTooLarge);

        entity.FileName ??= file.FileName;

        // Create embedding
        var model = entity.ToModel();

        // Store blob
        await BlobHelper.UploadBlobAsync(file, _blobs, entity.Id());

        // Insert entity
        {
            var result = await tables.UpsertAsync(entity);
            if (!result)
                return new InternalServerErrorResult();
        }

        model.Url = SasHelper.GenerateSasUriString(_blobs, entity.Id());

        // Queue document for processing

        var embedding = new DocumentEmbedding
        {
            DocumentId = entity.Id(),
            Platform = auth.Platform,
            ServiceId = targetService
        };

        {
            var result = await _documentQueue.SendMessageAsync(embedding.ObjectToJson());
            _log.LogInformation($"Queued document for processing. MessageId: {result.Value.MessageId}");
        }

        return new OkObjectResult(model);
    }

    [Function("ReprocessDocument_v1")]
    [OpenApiOperation("reprocessDocument", ["Documents"], Summary = "Retry processing a document")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NoContent, Description = "Requeued or already processed")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Locked, Description = "Already in progress or queued")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "Document does not exist")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> ReprocessDocument(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/document/{id}/reprocess")] HttpRequest req,
        FunctionContext context,
        string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteDocument))
            return new ForbiddenResult();

        var targetService = req.GetTargetService();
        var tables = await _storageService.GetTableStorageService(auth.Platform, targetService);

        var entity = await tables.RetrieveAsync<DocumentEntity>(id);
        if (entity == null)
            return new NotFoundResult();

        var model = entity.ToModel();
        if (model.Status == DocumentStatus.Processing || model.Status == DocumentStatus.Queued)
            return new StatusCodeResult((int)HttpStatusCode.Locked);

        if (model.Status == DocumentStatus.Processed)
            return new NoContentResult();

        entity.Status = DocumentStatus.Queued.ObjectToJson();
        await tables.UpsertAsync(entity);

        var embedding = new DocumentEmbedding
        {
            DocumentId = entity.Id(),
            Platform = auth.Platform,
            ServiceId = targetService
        };

        {
            var result = await _documentQueue.SendMessageAsync(embedding.ObjectToJson());
            _log.LogInformation($"Queued document for processing. MessageId: {result.Value.MessageId}");
        }

        return new NoContentResult();
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
        if (auth?.Platform == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.DeleteDocument) || auth.Platform == null)
            return new ForbiddenResult();

        if (!Guid.TryParse(id, out Guid _))
            return new BadRequestResult();

        var targetService = req.GetTargetService();
        var tables = await _storageService.GetTableStorageService(auth.Platform, targetService);

        var entity = await tables.RetrieveAsync<DocumentEntity>(id);
        if (entity == null)
            return new NotFoundResult();

        await _blobs.DeleteBlobIfExistsAsync(entity.Id());

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

            bool result = await aiService.DeleteDocumentEmbedding(aiDatabase, entity.EmbeddingId);
            if (!result)
            {
                _log.LogCritical("Failed to delete content embedding");
                return new InternalServerErrorResult();
            }
        }

        var delete = await tables.DeleteAsync(entity);
        if (!delete)
            return new InternalServerErrorResult();

        return new NoContentResult();
    }
}
