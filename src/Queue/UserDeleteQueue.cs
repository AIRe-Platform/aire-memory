// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


using Aire.Memory.Models;
using Aire.Memory.Services;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Aire.Memory.Queue;

public class UserDeleteQueue(BlobServiceClient blobs, MemoryStorageService storageService, ILogger<UserDeleteQueue> log)
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

            var chatlog_blobs = blobs.GetBlobContainerClient(AireConstants.Blobs.ChatLogs);
            await chatlog_blobs.CreateIfNotExistsAsync(publicAccessType: PublicAccessType.None);

            foreach (var chat in chatlogs)
            {
                log.LogInformation($"Deleting chat log '{chat.RowKey}'...");

                await chatlog_blobs.DeleteBlobIfExistsAsync(chat.Id());

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

            var results_blobs = blobs.GetBlobContainerClient(AireConstants.Blobs.QuestionnaireResults);
            await results_blobs.CreateIfNotExistsAsync(publicAccessType: PublicAccessType.None);

            foreach (var result in results)
            {
                log.LogInformation($"Deleting questionnaire result '{result.RowKey}'...");

                await results_blobs.DeleteBlobIfExistsAsync(result.Id());

                var delete = await storage.DeleteAsync(result);
                if (!delete)
                {
                    log.LogCritical("Failed to delete questionnaire results {id}", result.RowKey);
                    throw new Exception("Failed to delete data");
                }
            }
        }

        // Clear reminders
        {
            log.LogInformation("Searching for reminders...");
            var reminders_query = await storage.QueryAsync<ReminderEntity>(x => x.PartitionKey == options.UserId);
            var reminders = await reminders_query.ToListAsync();

            var reminder_blobs = blobs.GetBlobContainerClient(AireConstants.Blobs.Reminders);
            await reminder_blobs.CreateIfNotExistsAsync(publicAccessType: PublicAccessType.None);

            foreach (var reminder in reminders)
            {
                log.LogInformation($"Deleting reminder '{reminder.RowKey}'...");

                await reminder_blobs.DeleteBlobIfExistsAsync(reminder.Id());

                var delete = await storage.DeleteAsync(reminder);
                if (!delete)
                {
                    log.LogCritical("Failed to delete reminder {id}", reminder.RowKey);
                    throw new Exception("Failed to delete data");
                }
            }
        }

        // Clear statistics if anonymization is not allowed
        if (!options.Anonymize)
        {
            log.LogWarning("User data anonymization is not allowed. Deleting statistics data, content votes, and public results related to the user.");

            // Statistics
            {
                log.LogInformation("Searching for statistics...");
                var stats = await storage.GetTableClient(AireConstants.Tables.Statistics);
                string filter = $"user_id eq '{options.UserId}'";
                var query = stats.QueryAsync<TableEntity>(filter);
                var queryResults = await query.ToListAsync();
                foreach (var x in queryResults)
                {
                    await stats.DeleteEntityAsync(x);
                }
                log.LogInformation("{count} statistics events deleted", queryResults.Count);
            }

            // Content votes
            {
                log.LogInformation("Searching for content votes...");
                var votes_query = await storage.QueryAsync<ContentVoteEntity>(x => x.PartitionKey == options.UserId);
                var votes = await votes_query.ToListAsync();

                foreach (var vote in votes)
                {
                    log.LogInformation($"Deleting content vote '{vote.RowKey}'...");
                    var delete = await storage.DeleteAsync(vote);
                    if (!delete)
                    {
                        log.LogCritical("Failed to delete content vote {id}", vote.RowKey);
                        throw new Exception("Failed to delete data");
                    }
                }
            }

            // Public questionnaire results
            {
                log.LogInformation("Searching for questionnaire results...");
                var pub_results_query = await storage.QueryAsync<QuestionnairePublicResultsEntity>(x => x.UserId == options.UserId);
                var pub_results = await pub_results_query.ToListAsync();

                var pub_results_blobs = blobs.GetBlobContainerClient(AireConstants.Blobs.PublicQuestionnaireResults);
                await pub_results_blobs.CreateIfNotExistsAsync(publicAccessType: PublicAccessType.None);

                foreach (var result in pub_results)
                {
                    log.LogInformation($"Deleting public questionnaire result '{result.RowKey}'...");

                    await pub_results_blobs.DeleteBlobIfExistsAsync(result.Id());

                    var delete = await storage.DeleteAsync(result);
                    if (!delete)
                    {
                        log.LogCritical("Failed to delete public questionnaire results {id}", result.RowKey);
                        throw new Exception("Failed to delete data");
                    }
                }
            }
        }

        log.LogInformation("Tasks completed.");
    }
}
