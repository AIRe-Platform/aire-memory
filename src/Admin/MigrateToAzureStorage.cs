using System.Web.Http;
using Aire.Memory;
using Aire.Memory.Models;
using Aire.Sdk.Azure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;

// TODO: Remove after migration
public class MigrateToAzureStorage
{
    private readonly ILogger<MigrateToAzureStorage> _log;
    private readonly DatabaseContext _db;
    private readonly ITableStorageService _storage;

    public MigrateToAzureStorage(ILogger<MigrateToAzureStorage> log, DatabaseContext db, ITableStorageService storage)
    {
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

                foreach (var l in _db.ChatLogs)
                {
                    var ent = new ChatLogEntity
                    {
                        PartitionKey = l.UserId.ToString(),
                        RowKey = l.Id.ToString(),
                        EncryptedChatLog = l.EncryptedChatLog
                    };

                    var response = await _storage.UpsertAsync(ent);
                    if (!response)
                        throw new Exception($"Failed to migrate chatlog '{l.Id}'");

                    _log.LogInformation("Migrated chatlog {id}", l.Id);
                }
            }

            // Migrate questionnaires
            {
                _log.LogWarning("Starting to migrate questionnaires now!");

                foreach (var q in _db.Questionnaires)
                {
                    var ent = new QuestionnaireEntity
                    {
                        PartitionKey = q.Id.ToString(),
                        RowKey = q.Id.ToString(),
                        Content = q.Content,
                        EmbeddingId = q.EmbeddingId.ToString(),
                        Keywords = string.Join(",", q.Keywords!),
                        Lang = q.Lang,
                        Name = q.Name
                    };

                    var response = await _storage.UpsertAsync(ent);
                    if (!response)
                        throw new Exception($"Failed to migrate questionnaire '{q.Id}'");

                    _log.LogInformation("Migrated questionnaire {id}", q.Id);
                }
            }

            // Migrate questionnaire results
            {
                _log.LogWarning("Starting to migrate questionnaire results now!");

                foreach (var r in _db.QuestionnaireResults)
                {
                    var ent = new QuestionnaireResultsEntity
                    {
                        PartitionKey = r.UserId.ToString(),
                        RowKey = r.Id.ToString(),
                        EncryptedQuestionnaireResults = r.EncryptedQuestionnaireResults,
                        QuestionnaireId = r.QuestionnaireId.ToString(),
                    };

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
