using Bms.Domain.Calculations;
using Xunit;

namespace Bms.UnitTests.Calculations;

public class FormulaExpressionTests
{
    private static Dictionary<string, double> Values(params (string Key, double Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void ActivePower_FromVoltageCurrentAndPowerFactor()
    {
        var formula = FormulaExpression.Parse("voltage * current * powerFactor / 1000");

        Assert.Equal(["voltage", "current", "powerFactor"], formula.Variables);
        Assert.Equal(2.622, formula.Evaluate(Values(("voltage", 230), ("current", 12), ("powerFactor", 0.95)))!.Value, 9);
    }

    [Theory]
    [InlineData("2 + 3 * 4", 14)]
    [InlineData("(2 + 3) * 4", 20)]
    [InlineData("10 - 4 - 3", 3)]
    [InlineData("12 / 3 / 2", 2)]
    [InlineData("-2 * -3", 6)]
    [InlineData("x - -1", 6)]
    [InlineData("0.5 * x", 2.5)]
    public void Arithmetic_FollowsPrecedenceAndLeftAssociativity(string text, double expected)
    {
        Assert.Equal(expected, FormulaExpression.Parse(text + " + 0 * x").Evaluate(Values(("x", 5)))!.Value, 9);
    }

    [Fact]
    public void MissingInput_YieldsNoValue()
    {
        var formula = FormulaExpression.Parse("voltage * current");

        Assert.Null(formula.Evaluate(Values(("voltage", 230))));
    }

    [Fact]
    public void DivisionByZero_YieldsNoValue()
    {
        Assert.Null(FormulaExpression.Parse("a / b").Evaluate(Values(("a", 1), ("b", 0))));
    }

    [Fact]
    public void Variables_AreMatchedCaseInsensitivelyAndListedOnce()
    {
        var formula = FormulaExpression.Parse("Voltage * voltage");

        Assert.Single(formula.Variables);
        Assert.Equal(4, formula.Evaluate(Values(("VOLTAGE", 2)))!.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1 + 2")]
    [InlineData("voltage *")]
    [InlineData("(voltage * current")]
    [InlineData("voltage current")]
    [InlineData("voltage ^ 2")]
    [InlineData("1.2.3 * x")]
    [InlineData("Math.Pow(x, 2)")]
    public void Invalid_IsRejected(string text)
    {
        Assert.False(FormulaExpression.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void TooLong_IsRejected()
    {
        Assert.False(FormulaExpression.TryParse(string.Join(" + ", Enumerable.Repeat("x", 100)), out _, out _));
    }

    [Fact]
    public void Render_IsFullyParenthesisedWithFloatLiterals()
    {
        var formula = FormulaExpression.Parse("voltage * current * powerFactor / 1000");

        Assert.Equal("(((v[voltage] * v[current]) * v[powerFactor]) / 1000.0)", formula.Render(k => $"v[{k}]"));
    }
}
