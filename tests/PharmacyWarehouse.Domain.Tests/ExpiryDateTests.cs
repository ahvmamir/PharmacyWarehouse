using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Domain.Tests;

public class ExpiryDateTests
{
    [Theory]
    [InlineData("31/05/2028", "2028-05-31", ExpiryPrecision.Day, CalendarKind.Gregorian, "")]
    [InlineData("*31/05/2028", "2028-05-31", ExpiryPrecision.Day, CalendarKind.Gregorian, "")]
    [InlineData("07/2028", "2028-07-31", ExpiryPrecision.Month, CalendarKind.Gregorian, "MonthOnly")]
    [InlineData("01/02/1408", "2029-04-20", ExpiryPrecision.Day, CalendarKind.Jalali, "ConvertedFromJalali")]
    [InlineData("04/1408", "2029-07-21", ExpiryPrecision.Month, CalendarKind.Jalali, "ConvertedFromJalali,MonthOnly")]
    [InlineData("31/06/2028", "2028-06-30", ExpiryPrecision.Day, CalendarKind.Gregorian, "DayClampedToMonthEnd")]
    [InlineData("31/02/2028", "2028-02-29", ExpiryPrecision.Day, CalendarKind.Gregorian, "DayClampedToMonthEnd")]
    [InlineData("۳۱/۰۵/۲۰۲۸", "2028-05-31", ExpiryPrecision.Day, CalendarKind.Gregorian, "")]
    public void Parse_Valid(string input, string date, ExpiryPrecision precision, CalendarKind calendar, string warnings)
    {
        var r = ExpiryDate.Parse(input);
        Assert.Null(r.Error);
        Assert.NotNull(r.Value);
        Assert.Equal(DateOnly.Parse(date), r.Value!.Value.Date);
        Assert.Equal(precision, r.Value.Value.Precision);
        Assert.Equal(calendar, r.Value.Value.Calendar);
        Assert.Equal(warnings, string.Join(",", r.Warnings));
    }

    [Theory]
    [InlineData("13/062028")]
    [InlineData("30/04/02030")]
    [InlineData("30/09/0209")]
    [InlineData("2027/10")]
    [InlineData("4ماه پس از مصرف")]
    [InlineData("32/01/2028")]
    [InlineData("00/2028")]
    [InlineData("13/2028")]
    [InlineData("00/01/2028")]
    [InlineData("31/12/1899")]
    [InlineData("12/1899")]
    [InlineData("01/01/1500")]
    public void Parse_Invalid(string input)
    {
        var r = ExpiryDate.Parse(input);
        Assert.Null(r.Value);
        Assert.False(string.IsNullOrWhiteSpace(r.Error));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    public void Parse_EmptyIsNotError(string? input)
    {
        var r = ExpiryDate.Parse(input);
        Assert.Null(r.Value);
        Assert.Null(r.Error);
    }

    [Fact]
    public void FromJalali_EsfandOfLeapYearHas30Days()
    {
        var v = ExpiryDate.FromJalali(1408, 12, null);
        Assert.Equal("۳۰/۱۲/۱۴۰۸", ExpiryDate.Jalali(v));
    }

    [Fact]
    public void Display_UsesEnteredCalendarAndPrecision()
    {
        Assert.Equal("۰۴/۱۴۰۸", ExpiryDate.Display(ExpiryDate.FromJalali(1408, 4, null)));
        Assert.Equal("07/2028", ExpiryDate.Display(ExpiryDate.FromGregorian(2028, 7, null)));
        Assert.Equal("31/05/2028", ExpiryDate.Display(ExpiryDate.FromGregorian(2028, 5, 31)));
    }

    [Fact]
    public void Conversions_FullFormat()
    {
        Assert.Equal("۱۱/۰۳/۱۴۰۷", ExpiryDate.Jalali(ExpiryDate.FromGregorian(2028, 5, 31)));
        Assert.Equal("20/04/2029", ExpiryDate.Gregorian(ExpiryDate.FromJalali(1408, 2, 1)));
    }
}
