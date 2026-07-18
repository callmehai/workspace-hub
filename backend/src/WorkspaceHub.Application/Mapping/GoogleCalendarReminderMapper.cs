using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Mapping;

/// <summary>
/// Google Calendar API chỉ nhận reminder dạng minutes trước start.
/// All-day = start 00:00 ngày đó; UI Google "N ngày/tuần trước lúc HH:mm"
/// → minutes = N×unitMinutes − timeOfDayMinutes.
/// </summary>
public static class GoogleCalendarReminderMapper
{
    private const int MinutesPerDay = 1440;
    private const int MinutesPerWeek = 10080;

    public static int ToGoogleMinutes(ReminderUnit unit, int offsetValue, string? timeOfDay, bool allDayStyle = false)
    {
        var value = Math.Max(0, offsetValue);
        var baseMinutes = unit switch
        {
            ReminderUnit.Minutes => value,
            ReminderUnit.Hours => value * 60,
            ReminderUnit.Days => value * MinutesPerDay,
            ReminderUnit.Weeks => value * MinutesPerWeek,
            _ => value
        };

        // Timed events always use a plain minute offset; timeOfDay only belongs to all-day UI.
        if (!allDayStyle || unit is ReminderUnit.Minutes or ReminderUnit.Hours)
            return baseMinutes;

        var tod = ParseTimeOfDayMinutes(timeOfDay);
        if (tod <= 0)
            return baseMinutes;

        // "1 week before at 14:00" = 10080 - 840 = 9240.
        return Math.Max(0, baseMinutes - tod);
    }

    /// <param name="allDayStyle">
    /// true = decode kiểu Google all-day UI (Days/Weeks + timeOfDay).
    /// false = timed event — giữ nguyên Minutes.
    /// </param>
    public static (int OffsetValue, ReminderUnit OffsetUnit, string? TimeOfDay) FromGoogleMinutes(
        int minutes,
        bool allDayStyle = false)
    {
        minutes = Math.Max(0, minutes);

        if (!allDayStyle)
            return (minutes, ReminderUnit.Minutes, null);

        if (minutes == 0)
            return (0, ReminderUnit.Days, null);

        // Decode all-day Google UI: minutes = days×1440 − todMin (0 ≤ tod < 1440).
        int days;
        int todMinutes;
        if (minutes % MinutesPerDay == 0)
        {
            days = minutes / MinutesPerDay;
            todMinutes = 0;
        }
        else
        {
            days = (minutes + MinutesPerDay - 1) / MinutesPerDay; // ceil
            todMinutes = days * MinutesPerDay - minutes;
        }

        var timeOfDay = todMinutes > 0 ? FormatTimeOfDay(todMinutes) : null;

        if (days % 7 == 0 && days >= 7)
            return (days / 7, ReminderUnit.Weeks, timeOfDay);

        return (days, ReminderUnit.Days, timeOfDay);
    }

    private static int ParseTimeOfDayMinutes(string? timeOfDay)
    {
        if (string.IsNullOrWhiteSpace(timeOfDay))
            return 0;

        var parts = timeOfDay.Trim().Split(':');
        if (parts.Length < 2)
            return 0;
        if (!int.TryParse(parts[0], out var hour) || !int.TryParse(parts[1], out var minute))
            return 0;
        if (hour is < 0 or > 23 || minute is < 0 or > 59)
            return 0;

        return hour * 60 + minute;
    }

    private static string FormatTimeOfDay(int todMinutes)
    {
        todMinutes = Math.Clamp(todMinutes, 0, MinutesPerDay - 1);
        return $"{todMinutes / 60:D2}:{todMinutes % 60:D2}";
    }
}
