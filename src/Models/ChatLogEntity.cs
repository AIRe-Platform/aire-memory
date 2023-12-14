using System.Security.Cryptography;
using Aire.Sdk.Helpers;

namespace Aire.Memory.Models
{
    public class ChatLogEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? UserId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? EncryptedChatLog { get; set; }

        public List<ChatMessage>? GetChatLog(string userKey)
        {
            var key = Convert.FromBase64String(userKey);
            var parts = EncryptedChatLog?.Split(".");

            if(parts == null || parts.Length != 2)
                return null;
            
            var cipherText = parts[0];
            var iv = Convert.FromBase64String(parts[1]);
            var json = cipherText.DecryptString(key, iv);
            return json?.JsonToObject<List<ChatMessage>>();
        }

        public void SetChatLog(string userKey, List<ChatMessage> chat)
        {
            var json = chat.ObjectToJson();
            var key = Convert.FromBase64String(userKey);
            var iv = RandomNumberGenerator.GetBytes(16);
            EncryptedChatLog = $"{json.EncryptString(key, iv)}.{Convert.ToBase64String(iv)}";
        }
    }
}