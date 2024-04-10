using System.Security.Cryptography;
using Aire.Sdk.Helpers;
using Aire.Sdk.Models.Resources;
using Aire.Sdk.Azure;

namespace Aire.Memory.Models;

/// <summary>
/// User ID: PartitionKey
/// Result ID: RowKey
/// </summary>
[EntityTable("QuestionnaireResults")]
public class QuestionnaireResultsEntity : BaseTableEntity
{
    public string? QuestionnaireId { get; set; }
    public string? EncryptedQuestionnaireResults { get; set; }

    public QuestionnaireResultsEntity() { }
    public QuestionnaireResultsEntity(string userId, string? resultId = null)
    {
        PartitionKey = userId;
        RowKey = resultId ?? Guid.NewGuid().ToString();
    }

    public string Id()
    {
        return RowKey ?? "";
    }

    public string UserId()
    {
        return PartitionKey ?? "";
    }

    public QuestionnaireResults? DecryptData(string userKey)
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

    public void EncryptAndSetData(string userKey, QuestionnaireResults questionnaireResults)
    {
        var json = questionnaireResults.ObjectToJson();
        var key = Convert.FromBase64String(userKey);
        var iv = RandomNumberGenerator.GetBytes(16);
        EncryptedQuestionnaireResults = $"{json.EncryptString(key, iv)}.{Convert.ToBase64String(iv)}";
    }

    public QuestionnaireResults ToModel(string userKey)
    {
        var content = DecryptData(userKey);
        return new QuestionnaireResults
        {
            Id = RowKey,
            QuestionnaireId = QuestionnaireId,
            Timestamp = Timestamp.HasValue ? Timestamp.Value.UtcDateTime : null,
            Answers = content?.Answers,
            Summary = content?.Summary,
            Prompts = content?.Prompts
        };
    }
}
