// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


using Aire.Memory.Models;
using Aire.Memory.Services;
using Aire.Sdk.Helpers;
using Aire.Sdk.Models.Platform;
using Aire.Sdk.Models.Resources;
using Aire.Sdk.Platform;
using Aire.Sdk.Platform.Clients;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Queues.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Aire.Memory.Queue;

public class DocumentEmbeddingQueue
{
    private readonly MemoryStorageService _storageService;
    private readonly BlobContainerClient _docs;
    private readonly IAirePlatformService _platform;
    private readonly IAireClientFactory _clientFactory;
    private readonly IAireModuleSettingsService _moduleConfigService;
    private readonly ILogger<DocumentEmbeddingQueue> _log;

    public DocumentEmbeddingQueue(
        BlobServiceClient blobs,
        MemoryStorageService storageService,
        IAirePlatformService platformService,
        IAireClientFactory clientFactory,
        IAireModuleSettingsService moduleConfigService,
        ILogger<DocumentEmbeddingQueue> log)
    {
        _docs = blobs.GetBlobContainerClient(AireConstants.Blobs.Documents);
        _docs.CreateIfNotExists(publicAccessType: PublicAccessType.None);

        _storageService = storageService;
        _platform = platformService;
        _clientFactory = clientFactory;
        _moduleConfigService = moduleConfigService;
        _log = log;
    }

    [Function(nameof(DocumentEmbeddingQueue))]
    public async Task Run(
        [QueueTrigger(AireConstants.Queues.DocumentEmbed, Connection = "StorageConnectionString")] QueueMessage queueMessage)// DocumentEmbedding embedding)
    {
        var embedding = queueMessage.Body.ToObjectFromJson<DocumentEmbedding>();
        if (embedding == null)
        {
            _log.LogCritical("Failed to deserialize queue message");
            throw new ApplicationException("Failed to deserialize");
        }

        _log.LogInformation($"Begin processing document '{embedding.DocumentId}'");

        var storage = await _storageService.GetTableStorageService(embedding.Platform, embedding.ServiceId);

        // Find document entity
        var entity = await storage.RetrieveAsync<DocumentEntity>(embedding.DocumentId);
        if (entity == null)
        {
            _log.LogWarning($"Document no longer exists. Ignoring.");
            return;
        }

        var model = entity.ToModel();
        if (model.Status == DocumentStatus.Queued)
        {
            entity.Status = DocumentStatus.Processing.ObjectToJson();
            await storage.UpsertAsync(entity); // Update status
        }

        if (model.Status == DocumentStatus.Processed)
        {
            _log.LogWarning("The document has already been processed. Ignoring.");
            return;
        }

        // Check if failure has occurred too many times
        if (queueMessage.DequeueCount == 5)
        {
            _log.LogError("Marking document status as failed due to multiple failures");
            entity.Status = DocumentStatus.Failure.ObjectToJson();
            await storage.UpsertAsync(entity);
            _log.LogWarning("Proceeding to try one last time!");
        }

        // Find platform AI module

        var aiModule = await _platform.GetPlatformModule(embedding.Platform, ModuleType.AI, null);
        if (aiModule == null)
        {
            _log.LogCritical("Default AI module not configured");
            throw new ApplicationException("Invalid configuration");
        }

        // Create AI service client

        var aiService = await _clientFactory.CreateAiClient(aiModule, asService: true);
        var aiDatabase = await _moduleConfigService.Get<string>(
            embedding.Platform, ModuleType.Memory, embedding.ServiceId,
            ModuleSettings.Memory_VectorDbName);

        if (aiDatabase == null)
        {
            _log.LogCritical("Missing '{key}' module configuration", ModuleSettings.Memory_VectorDbName);
            throw new ApplicationException("Invalid configuration");
        }

        // Get blob
        var blobId = entity.Id();
        var blobClient = _docs.GetBlockBlobClient(blobId);

        if (!await blobClient.ExistsAsync())
        {
            _log.LogError("The document blob does not exist! Treating this as an instant failure.");
            entity.Status = DocumentStatus.Failure.ObjectToJson();
            await storage.UpsertAsync(entity);
            return;
        }

        // Create embeddings
        {
            var blobProps = await blobClient.GetPropertiesAsync();
            using var blobStream = await blobClient.OpenReadAsync();
            var file = new FormFile(blobStream, 0, blobStream.Length, model.Title ?? blobId, model.FileName ?? blobId)
            {
                Headers = new HeaderDictionary(),
                ContentType = blobProps.Value.ContentType
            };

            var embedResult = await aiService.CreateDocumentEmbedding(aiDatabase, file, model);
            var embedId = embedResult?.Ids?.FirstOrDefault();
            if (embedId == null)
            {
                _log.LogCritical("Failed to create embeddings");
                throw new ApplicationException("Embedding failed");
            }

            entity.EmbeddingId = embedId;
            entity.Status = DocumentStatus.Processed.ObjectToJson();

            await storage.UpsertAsync(entity);
        }

        _log.LogInformation($"Finished processing document '{embedding.DocumentId}'");
    }
}
