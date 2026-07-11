using System.Text.Json.Nodes;

namespace WorkspaceHub.Application.Mapping;

/// <summary>Merge metadata khi sync cập nhật event — giữ key app-only, còn lại lấy từ Google.</summary>
public static class CalendarSyncMetadataMerge
{
    public static string MergeForUpdate(string? existingMetadataJson, string syncedMetadataJson)
    {
        var synced = JsonNode.Parse(syncedMetadataJson)?.AsObject() ?? new JsonObject();

        if (!string.IsNullOrWhiteSpace(existingMetadataJson))
        {
            var existing = JsonNode.Parse(existingMetadataJson)?.AsObject();
            if (existing?.TryGetPropertyValue("calendarType", out var calendarType) == true
                && !synced.ContainsKey("calendarType"))
            {
                synced["calendarType"] = calendarType?.DeepClone();
            }
        }

        // JsonNode keys are already camelCase from mapper — no naming policy needed.
        return synced.ToJsonString();
    }
}
