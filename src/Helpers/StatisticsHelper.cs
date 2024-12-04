// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Aire.Sdk.Models.Statistics;
using Azure.Data.Tables;

namespace Aire.Memory.Helpers;

public static class StatisticsHelper
{
    private static readonly string[] EntityPropertyFilter = ["RowKey", "PartitionKey", "odata.etag", "ETag", "Timestamp"];
    private static readonly string[] ModelReadOnlyFields = ["id", "ts", "event_name", "client_id"];

    public static (string pk, string rk) GenerateTableKeys(DateTime dateTime, string eventName, string? id = null)
    {
        string pk = $"{eventName}_{dateTime:yyyyMMdd}";
        string rk = $"{id ?? Guid.NewGuid().ToString()}";
        return (pk, rk);
    }

    public static string GenerateEventPartitionFilter(string eventName, DateTime from, DateTime? to = null)
    {
        List<string> filters = [$"PartitionKey ge '{eventName}_{from:yyyyMMdd}'"];
        if (to.HasValue)
            filters.Add($"PartitionKey lt '{eventName}_{to.Value.AddDays(1):yyyyMMdd}'");
        else
            filters.Add($"PartitionKey lt '{eventName}_{from.AddDays(1):yyyyMMdd}'");

        return string.Join(" and ", filters);
    }

    public static void MergeEventDataToEntity(TableEntity entity, StatisticsEvent data)
    {
        foreach (var key in data.Keys)
        {
            // Skip read-only properties and entity properties
            if (EntityPropertyFilter.Contains(key) || ModelReadOnlyFields.Contains(key))
            {
                continue;
            }

            entity[key] = data[key];
        }
    }

    public static StatisticsEvent EventEntityToModel(TableEntity entity)
    {
        var model = new StatisticsEvent()
        {
            Id = Guid.Parse(entity.RowKey),
        };

        foreach (var key in entity.Keys)
        {
            // Skip entity properties
            if (EntityPropertyFilter.Contains(key))
                continue;

            model[key] = entity[key];
        }

        return model;
    }
}
