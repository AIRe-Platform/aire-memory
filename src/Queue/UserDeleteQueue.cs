using Aire.Memory.Models;
using Aire.Sdk.Azure;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Aire.Memory.Queue;

public class UserDeleteQueue
{
    private readonly ITableStorageService _storage;
    private readonly ILogger<UserDeleteQueue> _log;

    public UserDeleteQueue(ITableStorageService storage, ILogger<UserDeleteQueue> log)
    {
        _storage = storage;
        _log = log;
    }

    [Function(nameof(UserDeleteQueue))]
    public async Task Run([QueueTrigger(AireConstants.Queues.UserDelete, Connection = "StorageConnectionString")] UserDeleteOptions options)
    {
        _log.LogInformation($"Begin deleting data of user '{options.UserId}'");

        // TODO: Implement data anonymization (decrypt data, save to some place)
        if (options.Anonymize)
        {
            _log.LogWarning("User data anonymization is not yet implemented");
            _log.LogWarning("The data will be deleted");
        }

        // Clear chat logs
        {
            _log.LogInformation("Searching for chat logs...");
            var chatlogs_query = await _storage.QueryAsync<ChatLogEntity>(x => x.PartitionKey == options.UserId);
            var chatlogs = await chatlogs_query.ToListAsync();
            foreach (var chat in chatlogs)
            {
                _log.LogInformation($"Deleting chat log '{chat.RowKey}'...");
                var delete = await _storage.DeleteAsync(chat);
                if (!delete)
                {
                    _log.LogCritical("Failed to delete chat log {id}", chat.RowKey);
                    throw new Exception("Failed to delete data");
                }
            }
        }

        // Clear questionnaire results
        {
            _log.LogInformation("Searching for questionnaire results...");
            var results_query = await _storage.QueryAsync<QuestionnaireResultsEntity>(x => x.PartitionKey == options.UserId);
            var results = await results_query.ToListAsync();
            foreach(var result in results)
            {
                _log.LogInformation($"Deleting questionnaire result '{result.RowKey}'...");
                var delete = await _storage.DeleteAsync(result);
                if (!delete)
                {
                    _log.LogCritical("Failed to delete questionnaire results {id}", result.RowKey);
                    throw new Exception("Failed to delete data");
                }
            }
        }

        _log.LogInformation("Tasks completed.");
    }
}
