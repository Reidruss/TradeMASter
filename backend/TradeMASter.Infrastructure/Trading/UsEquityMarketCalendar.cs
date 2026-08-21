using TradeMASter.Core.Interfaces;

namespace TradeMASter.Infrastructure.Trading;

public sealed class UsEquityMarketCalendar : IUsMarketCalendar
{
    public bool IsRegularSession(DateTime utcNow)
    {
        var eastern = ToEastern(utcNow);
        if (eastern.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || IsHoliday(eastern.Date)) return false;
        return eastern.TimeOfDay >= TimeSpan.FromHours(9.5) && eastern.TimeOfDay < TimeSpan.FromHours(16);
    }

    public string DescribeClosure(DateTime utcNow)
    {
        var eastern = ToEastern(utcNow);
        if (eastern.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return "U.S. equity market is closed for the weekend.";
        if (IsHoliday(eastern.Date)) return "U.S. equity market is closed for a scheduled exchange holiday.";
        return "Outside the regular U.S. equity session (9:30 AM–4:00 PM ET).";
    }

    internal static bool IsHoliday(DateTime date)
    {
        var year = date.Year;
        var holidays = new HashSet<DateTime>
        {
            Observed(new DateTime(year, 1, 1)),
            NthWeekday(year, 1, DayOfWeek.Monday, 3),
            NthWeekday(year, 2, DayOfWeek.Monday, 3),
            EasterSunday(year).AddDays(-2),
            LastWeekday(year, 5, DayOfWeek.Monday),
            Observed(new DateTime(year, 6, 19)),
            Observed(new DateTime(year, 7, 4)),
            NthWeekday(year, 9, DayOfWeek.Monday, 1),
            NthWeekday(year, 11, DayOfWeek.Thursday, 4),
            Observed(new DateTime(year, 12, 25))
        };
        // New Year's Day can be observed on December 31 of the prior calendar year.
        holidays.Add(Observed(new DateTime(year + 1, 1, 1)));
        return holidays.Contains(date.Date);
    }

    private static DateTime ToEastern(DateTime utcNow)
    {
        TimeZoneInfo eastern;
        try { eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
        catch (TimeZoneNotFoundException) { eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), eastern);
    }

    private static DateTime Observed(DateTime date) => date.DayOfWeek switch
    {
        DayOfWeek.Saturday => date.AddDays(-1),
        DayOfWeek.Sunday => date.AddDays(1),
        _ => date
    };

    private static DateTime NthWeekday(int year, int month, DayOfWeek day, int occurrence)
    {
        var date = new DateTime(year, month, 1);
        var offset = ((int)day - (int)date.DayOfWeek + 7) % 7;
        return date.AddDays(offset + 7 * (occurrence - 1));
    }

    private static DateTime LastWeekday(int year, int month, DayOfWeek day)
    {
        var date = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        var offset = ((int)date.DayOfWeek - (int)day + 7) % 7;
        return date.AddDays(-offset);
    }

    private static DateTime EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateTime(year, month, day);
    }
}
