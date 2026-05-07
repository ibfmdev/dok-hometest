namespace Dok.Domain.Tests;

public class PlateTests
{
    [Theory]
    [InlineData("ABC1234")]
    [InlineData("ABC1A23")]
    [InlineData("XYZ9Z99")]
    [InlineData("abc1234")]
    [InlineData(" ABC1234 ")]
    public void Parse_with_valid_format_returns_normalized_plate(string raw)
    {
        var plate = Plate.Parse(raw);
        plate.Value.Length.ShouldBe(7);
        plate.Value.ShouldBe(plate.Value.ToUpperInvariant());
    }

    [Theory]
    [InlineData("ABC123")]
    [InlineData("ABC12345")]
    [InlineData("123ABCD")]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_with_invalid_format_throws(string? raw)
    {
        Should.Throw<InvalidPlateException>(() => Plate.Parse(raw));
    }

    [Fact]
    public void Masked_returns_first_3_chars_with_asterisks()
    {
        Plate.Parse("ABC1234").Masked().ShouldBe("ABC****");
    }

    [Fact]
    public void TryParse_returns_false_for_invalid()
    {
        Plate.TryParse("xxx", out _).ShouldBeFalse();
    }
}
