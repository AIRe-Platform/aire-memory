using System.Security.Cryptography;
using Aire.Sdk.Helpers;
using Aire.Sdk.Models.Resources;

namespace Aire.Memory.Models.SQL
{
    [Obsolete("Will be removed after migration to Azure Storage")]
    public class QuestionnaireResultsEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? UserId { get; set; }
        public Guid? QuestionnaireId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? EncryptedQuestionnaireResults { get; set; }

        public QuestionnaireResults? GetQuestionnaireResults(string userKey)
        {
            var key = Convert.FromBase64String(userKey);
            var parts = EncryptedQuestionnaireResults?.Split(".");

            if (parts == null || parts.Length != 2)
                return null;

            var cipherText = parts[0];
            var iv = Convert.FromBase64String(parts[1]);
            var json = cipherText.DecryptString(key, iv);
            return json?.JsonToObject<QuestionnaireResults>();
        }

        public void SetQuestionnaireResults(string userKey, QuestionnaireResults questionnaireResults)
        {
            var json = questionnaireResults.ObjectToJson();
            var key = Convert.FromBase64String(userKey);
            var iv = RandomNumberGenerator.GetBytes(16);
            EncryptedQuestionnaireResults = $"{json.EncryptString(key, iv)}.{Convert.ToBase64String(iv)}";
        }

        public QuestionnaireResults ToModel(string userKey)
        {
            var questionnaireResultsContent = GetQuestionnaireResults(userKey);
            return new QuestionnaireResults
            {
                Id = Id.ToString(),
                QuestionnaireId = QuestionnaireId.ToString(),
                Timestamp = Timestamp,
                Answers = questionnaireResultsContent?.Answers,
                Summary = questionnaireResultsContent?.Summary,
                Prompts = questionnaireResultsContent?.Prompts
            };
        }
    }
}
