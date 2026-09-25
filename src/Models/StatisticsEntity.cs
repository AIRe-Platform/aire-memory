// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


using Aire.Memory.Helpers;
using Aire.Sdk.Azure;
using Aire.Sdk.Models.Statistics;

namespace Aire.Memory.Models;

/// <summary>
/// PartitionKey: yyyyMMdd
/// RowKey: eventName
/// </summary>
[EntityTable("Statistics")]
public class StatisticsEntity : BaseTableEntity
{
    public int Count { get; set; }

    public string GetEventName()
    {
        return RowKey ?? "";
    }

    public StatisticsEntity() { }
    public StatisticsEntity(string eventName)
    {
        PartitionKey = DateTime.UtcNow.ToString("yyyyMMdd");
        RowKey = StatisticsHelper.EscapeEventName(eventName);
    }

    public static string CreateFilter(DateTime from, DateTime? to = null, string? eventNamePrefix = null)
    {
        List<string> filters = [$"PartitionKey ge '{from:yyyyMMdd}'"];
        if (to.HasValue)
            filters.Add($"PartitionKey lt '{to.Value.AddDays(1):yyyyMMdd}'");
        else
            filters.Add($"PartitionKey lt '{from.AddDays(1):yyyyMMdd}'");

        if (!string.IsNullOrWhiteSpace(eventNamePrefix))
        {
            string prefix = StatisticsHelper.EscapeEventName(eventNamePrefix);
            char last = (char)(prefix.Last() + 1);
            string end = prefix[..^1] + last;
            filters.Add($"RowKey ge '{prefix}' and RowKey lt '{end}'");
        }

        return string.Join(" and ", filters);
    }

    public static (string pk, string rk) CreateTableKeys(DateTime date, string eventName)
    {
        return ($"{date:yyyyMMdd}", StatisticsHelper.EscapeEventName(eventName));
    }

    public StatisticsEventInfo ToModel()
    {
        return new StatisticsEventInfo
        {
            EventName = GetEventName(),
            EventCount = Count,
        };
    }
}
