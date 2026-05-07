namespace Dok.Domain.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData("1.555", "1.56")]
    [InlineData("1.554", "1.55")]
    [InlineData("0", "0.00")]
    [InlineData("1500", "1500.00")]
    [InlineData("255.425", "255.43")]
    public void Of_rounds_HALF_UP_to_2_decimals(string input, string expectedJson)
    {
        var dec = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        Money.Of(dec).ToJsonString().ShouldBe(expectedJson);
    }

    [Fact]
    public void Plus_sums_values_with_rounding()
    {
        var sum = Money.Of(1.50m) + Money.Of(2.25m);
        sum.Value.ShouldBe(3.75m);
    }

    [Fact]
    public void Times_decimal_factor()
    {
        (Money.Of(100m) * 0.95m).Value.ShouldBe(95.00m);
    }

    [Fact]
    public void Zero_is_zero()
    {
        Money.Zero.Value.ShouldBe(0m);
        Money.Zero.ToJsonString().ShouldBe("0.00");
    }
}
