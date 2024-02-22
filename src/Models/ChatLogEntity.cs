using System.Security.Cryptography;
using Aire.Sdk.Helpers;
using Aire.Sdk.Models.Chat;

namespace Aire.Memory.Models
{
    public class ChatLogEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? UserId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? EncryptedChatLog { get; set; }

        public ChatLog? GetChatLog(string userKey)
        {
            var key = Convert.FromBase64String(userKey);
            var parts = EncryptedChatLog?.Split(".");

            if (parts == null || parts.Length != 2)
                return null;

            var cipherText = parts[0];
            var iv = Convert.FromBase64String(parts[1]);
            var json = cipherText.DecryptString(key, iv);

            var chat = json?.JsonToObject<ChatLog>();

            // Backwards-compatibility with message lists
            if (chat == null)
            {
                var messages = json?.JsonToObject<List<ChatMessage>>();
                if (messages != null)
                {
                    chat = new ChatLog { Messages = messages };
                }
            }

            return chat;
        }

        public void SetChatLog(string userKey, ChatLog chat)
        {
            var json = chat.ObjectToJson();
            var key = Convert.FromBase64String(userKey);
            var iv = RandomNumberGenerator.GetBytes(16);
            EncryptedChatLog = $"{json.EncryptString(key, iv)}.{Convert.ToBase64String(iv)}";
        }
    }
}
