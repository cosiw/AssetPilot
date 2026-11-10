using AssetPilot.Core.Common;

namespace AssetPilot.Core.Tests;

public sealed class NumericParserTests
{
    [Theory]
    [InlineData("1234.56", 1234.56)]
    [InlineData("-42", -42)]
    [InlineData(null, 0)]
    public void ToDecimal_parses_invariant_values_and_treats_null_as_zero(string? value, decimal expected)
    {
        Assert.Equal(expected, NumericParser.ToDecimal(value));
    }

    [Theory]
    [InlineData("42", 42)]
    [InlineData("-7", -7)]
    [InlineData(null, 0)]
    public void ToInt32_parses_invariant_values_and_treats_null_as_zero(string? value, int expected)
    {
        Assert.Equal(expected, NumericParser.ToInt32(value));
    }
}
