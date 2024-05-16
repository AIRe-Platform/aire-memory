using Aire.Memory.Models;
using Aire.Sdk.Azure;

namespace Aire.Memory.Helpers;

public static class KeywordHelper
{
    public static string Sanitize(string? keyword)
    {
        return keyword?.Replace("'", "").ToLower().Trim() ?? "";
    }

    public static string[] Sanitize(IEnumerable<string> keywords)
    {
        return keywords
            .Select(Sanitize)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
    }

    public static async Task<List<string>> UpdateKeywords(
        ITableStorageService storage,
        string resourceType,
        string resourceId,
        IEnumerable<string> oldKeywords,
        IEnumerable<string> newKeywords)
    {
        var oldSanitized = Sanitize(oldKeywords);
        var newSanitized = Sanitize(newKeywords);

        var decrease = oldSanitized.Where(x => !newSanitized.Contains(x));
        var increase = newSanitized.Where(x => !oldSanitized.Contains(x));

        // Update keyword value entries

        foreach (var keyword in decrease)
        {
            var pk = KeywordValueEntity.PartitionFromValue(keyword);
            if (pk == null)
                continue;

            var entity = await storage.RetrieveAsync<KeywordValueEntity>(pk, keyword);
            if (entity == null)
                entity = new KeywordValueEntity(keyword);

            var stats = entity.Stats ?? [];
            stats[resourceType] = Math.Max(0, stats.GetValueOrDefault(resourceType, 1) - 1);
            entity.Stats = stats;

            await storage.UpsertAsync(entity);
        }

        foreach (var keyword in increase)
        {
            var pk = KeywordValueEntity.PartitionFromValue(keyword);
            if (pk == null)
                continue;

            var entity = await storage.RetrieveAsync<KeywordValueEntity>(pk, keyword);
            if (entity == null)
                entity = new KeywordValueEntity(keyword);

            var stats = entity.Stats ?? [];
            stats[resourceType] = stats.GetValueOrDefault(resourceType, 0) + 1;
            entity.Stats = stats;

            await storage.UpsertAsync(entity);
        }

        // Update index entries

        foreach (var keyword in decrease)
        {
            var pk = KeywordIndexEntity.PartitionForResource(resourceType, keyword);
            if (pk == null)
                continue;
            await storage.DeleteAsync<KeywordIndexEntity>(pk, resourceId);
        }

        foreach (var keyword in increase)
        {
            var pk = KeywordIndexEntity.PartitionForResource(resourceType, keyword);
            if (pk == null)
                continue;
            var indexEntity = new KeywordIndexEntity(resourceType, resourceId, keyword);
            await storage.UpsertAsync(indexEntity);
        }

        return [.. newSanitized];
    }
}