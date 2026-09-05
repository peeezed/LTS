using FluentAssertions;
using LTS.Application.RomaniaOneClick;

namespace LTS.Tests.RomaniaOneClick;

public class RomaniaOneClickDateParserTests
{
    [Fact]
    public void Parses_klgs_iso_datetime_format_into_the_calendar_date()
    {
        var result = RomaniaOneClickDateParser.Parse("2023-06-21T00:00:00.000000Z");

        result.Should().Be(new DateOnly(2023, 6, 21));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_input_yields_no_date(string? value)
    {
        RomaniaOneClickDateParser.Parse(value).Should().BeNull();
    }

    [Fact]
    public void Unparseable_input_yields_no_date_rather_than_throwing()
    {
        RomaniaOneClickDateParser.Parse("not-a-date").Should().BeNull();
    }
}
