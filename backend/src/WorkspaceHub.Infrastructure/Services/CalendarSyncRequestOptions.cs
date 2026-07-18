namespace WorkspaceHub.Infrastructure.Services;

public sealed record CalendarSyncRequestOptions(
    bool SingleEvents,
    IReadOnlyList<string> EventTypes,
    DateTimeOffset? TimeMin,
    string? SyncToken)
{
    private static readonly string[] DefaultEventTypes = ["default"];

    public static CalendarSyncRequestOptions ForInitialSync(DateTimeOffset now)
        => new(
            SingleEvents: true,
            EventTypes: DefaultEventTypes,
            TimeMin: now.AddMonths(-3),
            SyncToken: null);

    public static CalendarSyncRequestOptions ForIncrementalSync(string syncToken)
        => new(
            SingleEvents: true,
            EventTypes: DefaultEventTypes,
            TimeMin: null,
            SyncToken: syncToken);
}
