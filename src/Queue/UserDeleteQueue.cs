// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


using Aire.Memory.Models;
using Aire.Sdk.Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Aire.Memory.Queue;

public class UserDeleteQueue
{
    private readonly ITableStorageService _storage;
    private readonly TableClient _stats;
    private readonly ILogger<UserDeleteQueue> _log;

    public UserDeleteQueue(ITableStorageService storage, TableServiceClient tableClient, ILogger<UserDeleteQueue> log)
    {
        _storage = storage;
        _stats = tableClient.GetTableClient(AireConstants.Tables.Statistics);
        _stats.CreateIfNotExists();
        _log = log;
    }

    [Function(nameof(UserDeleteQueue))]
    public async Task Run([QueueTrigger(AireConstants.Queues.UserDelete, Connection = "StorageConnectionString")] UserDeleteOptions options)
    {
        _log.LogInformation($"Begin deleting data of user '{options.UserId}'");

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
            foreach (var result in results)
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

        // Clear statistics if anonymization is not allowed
        if (!options.Anonymize)
        {
            _log.LogWarning("User data anonymization is not allowed. Deleting statistics data related to the user.");
            string filter = $"user_id eq '{options.UserId}'";
            await _stats
                .QueryAsync<TableEntity>(filter)
                .ForEachAsync(async x => await _stats.DeleteEntityAsync(x));
        }

        _log.LogInformation("Tasks completed.");
    }
}
