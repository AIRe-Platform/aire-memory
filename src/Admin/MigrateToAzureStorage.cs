using System.Web.Http;
using Aire.Memory;
using Aire.Memory.Models;
using Aire.Sdk.Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Logging;

// TODO: Remove after migration
public class MigrateToAzureStorage
{
    private readonly BlobContainerClient _chatlogs;
    private readonly BlobContainerClient _questionnaires;
    private readonly BlobContainerClient _questionnaireResults;

    private readonly ILogger<MigrateToAzureStorage> _log;
    private readonly DatabaseContext _db;
    private readonly ITableStorageService _storage;

    public MigrateToAzureStorage(
        ILogger<MigrateToAzureStorage> log,
        IAzureClientFactory<BlobServiceClient> clientFactory,
        DatabaseContext db,
        ITableStorageService storage)
    {
        var client = clientFactory.CreateClient("blob-client");

        _chatlogs = client.GetBlobContainerClient("chatlogs");
        _questionnaires = client.GetBlobContainerClient("questionnaires");
        _questionnaireResults = client.GetBlobContainerClient("questionnaire-results");

        _log = log;
        _db = db;
        _storage = storage;
    }

    [Function("MigrateToAzureStorage")]
    [OpenApiIgnore]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Admin, "get", Route = "migration/database")] HttpRequest req)
    {
        try
        {
            // Migrate chat histories
            {
                _log.LogWarning("Starting to migrate chatlogs now!");
                await _chatlogs.CreateIfNotExistsAsync(publicAccessType: PublicAccessType.None);

                foreach (var l in _db.ChatLogs)
                {
                    var ent = new ChatLogEntity
                    {
                        PartitionKey = l.UserId.ToString(),
                        RowKey = l.Id.ToString(),
                    };

                    var blobData = BinaryData.FromString(l.EncryptedChatLog!);
                    var client = _chatlogs.GetBlobClient(l.Id.ToString());
                    await client.UploadAsync(blobData, overwrite: true);

                    var response = await _storage.UpsertAsync(ent);
                    if (!response)
                        throw new Exception($"Failed to migrate chatlog '{l.Id}'");

                    _log.LogInformation("Migrated chatlog {id}", l.Id);
                }
            }

            // Migrate questionnaires
            {
                _log.LogWarning("Starting to migrate questionnaires now!");
                await _questionnaires.CreateIfNotExistsAsync(publicAccessType: PublicAccessType.None);

                foreach (var q in _db.Questionnaires)
                {
                    var ent = new QuestionnaireEntity
                    {
                        PartitionKey = q.Id.ToString(),
                        RowKey = q.Id.ToString(),
                        EmbeddingId = q.EmbeddingId.ToString(),
                        Keywords = string.Join(",", q.Keywords!),
                        Lang = q.Lang,
                        Name = q.Name
                    };

                    var blobData = BinaryData.FromString(q.Content!);
                    var client = _questionnaires.GetBlobClient(q.Id.ToString());
                    await client.UploadAsync(blobData, overwrite: true);

                    var response = await _storage.UpsertAsync(ent);
                    if (!response)
                        throw new Exception($"Failed to migrate questionnaire '{q.Id}'");

                    _log.LogInformation("Migrated questionnaire {id}", q.Id);
                }
            }

            // Migrate questionnaire results
            {
                _log.LogWarning("Starting to migrate questionnaire results now!");
                await _questionnaireResults.CreateIfNotExistsAsync(publicAccessType: PublicAccessType.None);

                foreach (var r in _db.QuestionnaireResults)
                {
                    var ent = new QuestionnaireResultsEntity
                    {
                        PartitionKey = r.UserId.ToString(),
                        RowKey = r.Id.ToString(),
                        QuestionnaireId = r.QuestionnaireId.ToString(),
                    };

                    var blobData = BinaryData.FromString(r.EncryptedQuestionnaireResults!);
                    var client = _questionnaireResults.GetBlobClient(r.Id.ToString());
                    await client.UploadAsync(blobData, overwrite: true);

                    var response = await _storage.UpsertAsync(ent);
                    if (!response)
                        throw new Exception($"Failed to migrate questionnaire result '{r.Id}'");

                    _log.LogInformation("Migrated questionnaire result {id}", r.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogError("Encountered a problem during migration. Stopping!");
            _log.LogCritical(ex.Message);
            return new InternalServerErrorResult();
        }

        _log.LogWarning("Migration complete!");
        return new OkResult();
    }
}
