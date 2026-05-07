using System.Globalization;

namespace Dok.Domain;

public readonly record struct Money
{
    public static readonly Money Zero = new(0m);

    public decimal Value { get; }

    private Money(decimal value) => Value = value;

    public static Money Of(decimal v) =>
        new(Math.Round(v, 2, MidpointRounding.AwayFromZero));

    public static Money operator +(Money a, Money b) => Of(a.Value + b.Value);
    public static Money operator -(Money a, Money b) => Of(a.Value - b.Value);
    public static Money operator *(Money a, decimal factor) => Of(a.Value * factor);

    public string ToJsonString() => Value.ToString("F2", CultureInfo.InvariantCulture);

    public override string ToString() => ToJsonString();
}
