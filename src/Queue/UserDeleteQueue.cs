// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


using Aire.Memory.Models;
using Aire.Memory.Services;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Aire.Memory.Queue;

public class UserDeleteQueue(MemoryStorageService storageService, ILogger<UserDeleteQueue> log)
{
    [Function(nameof(UserDeleteQueue))]
    public async Task Run([QueueTrigger(AireConstants.Queues.UserDelete, Connection = "StorageConnectionString")] UserDeleteOptions options)
    {
        log.LogInformation($"Begin deleting data of user '{options.UserId}'");

        var storage = await storageService.GetTableStorageService(options.Platform, options.Target);

        // Clear chat logs
        {
            log.LogInformation("Searching for chat logs...");
            var chatlogs_query = await storage.QueryAsync<ChatLogEntity>(x => x.PartitionKey == options.UserId);
            var chatlogs = await chatlogs_query.ToListAsync();
            foreach (var chat in chatlogs)
            {
                log.LogInformation($"Deleting chat log '{chat.RowKey}'...");
                var delete = await storage.DeleteAsync(chat);
                if (!delete)
                {
                    log.LogCritical("Failed to delete chat log {id}", chat.RowKey);
                    throw new Exception("Failed to delete data");
                }
            }
        }

        // Clear questionnaire results
        {
            log.LogInformation("Searching for questionnaire results...");
            var results_query = await storage.QueryAsync<QuestionnaireResultsEntity>(x => x.PartitionKey == options.UserId);
            var results = await results_query.ToListAsync();
            foreach (var result in results)
            {
                log.LogInformation($"Deleting questionnaire result '{result.RowKey}'...");
                var delete = await storage.DeleteAsync(result);
                if (!delete)
                {
                    log.LogCritical("Failed to delete questionnaire results {id}", result.RowKey);
                    throw new Exception("Failed to delete data");
                }
            }
        }

        // Clear statistics if anonymization is not allowed
        if (!options.Anonymize)
        {
            log.LogWarning("User data anonymization is not allowed. Deleting statistics data related to the user.");
            var stats = await storage.GetTableClient(AireConstants.Tables.Statistics);
            string filter = $"user_id eq '{options.UserId}'";
            var query = stats.QueryAsync<TableEntity>(filter);
            var queryResults = await query.ToListAsync();
            foreach (var x in queryResults)
            {
                await stats.DeleteEntityAsync(x);
            }
        }

        log.LogInformation("Tasks completed.");
    }
}
