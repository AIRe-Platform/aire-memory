using Aire.Memory.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aire.Memory.Queue
{
    public class UserDeleteQueue
    {
        private readonly DatabaseContext _db;
        private readonly ILogger<UserDeleteQueue> _log;

        public UserDeleteQueue(DatabaseContext db, ILogger<UserDeleteQueue> log)
        {
            _db = db;
            _log = log;
        }

        [Function(nameof(UserDeleteQueue))]
        public async Task Run([QueueTrigger(AireConstant.UserDeleteQueue, Connection = "StorageConnectionString")] UserDeleteOptions options)
        {
            _log.LogInformation($"Begin deleting data of user '{options.UserId}'");
            
            // TODO: Implement data anonymization (decrypt data, save to some place)
            if(options.Anonymize)
            {
                _log.LogWarning("User data anonymization is not yet implemented");
                _log.LogWarning("The data will be deleted");
            }

            _log.LogInformation("Searching for chat logs...");

            var history = await _db.ChatLogs
                .Where(x => x.UserId == options.UserId)
                .ToListAsync();

            foreach (var chat in history)
            {
                _log.LogInformation($"Found chat log: '{chat.Id}'");
                _db.ChatLogs.Remove(chat);
            }

            _log.LogInformation("The data is now being deleted...");
            await _db.SaveChangesAsync();

            _log.LogInformation("Tasks completed.");
        }
    }
}
