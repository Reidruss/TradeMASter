using FluentAssertions;
using TradeMASter.Infrastructure.Trading;
using Xunit;

namespace TradeMASter.Tests.Infrastructure;

public sealed class UsEquityMarketCalendarTests
{
    private readonly UsEquityMarketCalendar _calendar = new();

    [Fact]
    public void RegularWeekdaySession_IsOpen()
    {
        _calendar.IsRegularSession(new DateTime(2026, 8, 20, 15, 0, 0, DateTimeKind.Utc)).Should().BeTrue();
    }

    [Theory]
    [InlineData(2026, 7, 3, 15)] // Independence Day observed
    [InlineData(2026, 12, 25, 15)]
    [InlineData(2026, 8, 22, 15)] // Saturday
    [InlineData(2026, 8, 20, 13)] // Before 9:30 ET
    public void HolidayWeekendAndOutsideHours_AreClosed(int year, int month, int day, int utcHour)
    {
        _calendar.IsRegularSession(new DateTime(year, month, day, utcHour, 0, 0, DateTimeKind.Utc)).Should().BeFalse();
    }
}
