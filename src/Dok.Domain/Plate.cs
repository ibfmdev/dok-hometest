using System.Text.RegularExpressions;
using Dok.Domain.Exceptions;

namespace Dok.Domain;

public readonly record struct Plate
{
    private static readonly Regex Mercosul = new(@"^[A-Z]{3}\d[A-Z]\d{2}$", RegexOptions.Compiled);
    private static readonly Regex Antiga   = new(@"^[A-Z]{3}\d{4}$",         RegexOptions.Compiled);

    public string Value { get; }

    private Plate(string value) => Value = value;

    public static Plate Parse(string? raw)
    {
        var normalized = raw?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!Mercosul.IsMatch(normalized) && !Antiga.IsMatch(normalized))
            throw new InvalidPlateException(raw);
        return new Plate(normalized);
    }

    public static bool TryParse(string? raw, out Plate plate)
    {
        try
        {
            plate = Parse(raw);
            return true;
        }
        catch (InvalidPlateException)
        {
            plate = default;
            return false;
        }
    }

    public string Masked() => Value.Length >= 3 ? $"{Value[..3]}****" : "****";

    public override string ToString() => Value;
}
