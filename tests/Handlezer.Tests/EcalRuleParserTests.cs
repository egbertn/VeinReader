using Handlezer.Api.Application;
using Xunit;

namespace Handlezer.Tests;

public sealed class EcalRuleParserTests
{
    [Fact]
    public void ParseUnderstandsDateTimeAndWeekdayRanges()
    {
        var parser = new EcalRuleParser();
        var rule = parser.Parse("allow; dates=2026-08-01..2026-08-31; time=08:00..18:00; weekdays=Mon-Fri");

        Assert.True(rule.Allow);
        Assert.Equal(new DateOnly(2026, 8, 1), rule.StartDate);
        Assert.Equal(new DateOnly(2026, 8, 31), rule.EndDate);
        Assert.Equal(new TimeOnly(8, 0), rule.StartTime);
        Assert.Equal(new TimeOnly(18, 0), rule.EndTime);
        Assert.Contains(DayOfWeek.Monday, rule.Days);
        Assert.Contains(DayOfWeek.Friday, rule.Days);
    }

    [Fact]
    public void ParseUnderstandsDenyRule()
    {
        // Een "deny" regel zonder verdere clausules moet Allow = false opleveren.
        // De service gebruikt dit om altijd toegang te weigeren ongeacht tijdstip.
        var parser = new EcalRuleParser();
        var rule = parser.Parse("deny");

        Assert.False(rule.Allow);
    }

    [Fact]
    public void ParseUnderstandsIndividualCommaSeperatedWeekdays()
    {
        // Komma-gescheiden weekdagen (geen bereik) moeten elk afzonderlijk worden opgenomen.
        var parser = new EcalRuleParser();
        var rule = parser.Parse("allow; weekdays=Mon,Wed,Fri");

        Assert.Contains(DayOfWeek.Monday, rule.Days);
        Assert.Contains(DayOfWeek.Wednesday, rule.Days);
        Assert.Contains(DayOfWeek.Friday, rule.Days);
        Assert.Equal(3, rule.Days.Count);
    }

    [Fact]
    public void ParseUnderstandsSingleDateWithoutRange()
    {
        // Een enkele datum zonder ".." geeft een periode van precies één dag.
        var parser = new EcalRuleParser();
        var rule = parser.Parse("allow; dates=2026-12-25");

        Assert.Equal(new DateOnly(2026, 12, 25), rule.StartDate);
        Assert.Equal(new DateOnly(2026, 12, 25), rule.EndDate);
    }

    [Fact]
    public void ParseThrowsArgumentExceptionForUnknownClause()
    {
        // Een onbekende clausulesleutel moet een ArgumentException gooien
        // zodat configuratiefouten vroeg worden ontdekt.
        var parser = new EcalRuleParser();

        Assert.Throws<ArgumentException>(() => parser.Parse("allow; location=office"));
    }
}