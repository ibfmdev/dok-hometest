using Dok.Domain.Exceptions;

namespace Dok.Domain;

public static class DebtTypeMapper
{
    public static DebtType Parse(string? raw) => (raw?.Trim().ToUpperInvariant()) switch
    {
        "IPVA"  => DebtType.Ipva,
        "MULTA" => DebtType.Multa,
        "LICENCIAMENTO" => DebtType.Licenciamento,
        null or "" => throw new UnknownDebtTypeException(raw ?? "<null>"),
        var other  => throw new UnknownDebtTypeException(other),
    };

    public static string ToWire(DebtType type) => type switch
    {
        DebtType.Ipva  => "IPVA",
        DebtType.Multa => "MULTA",
        DebtType.Licenciamento => "LICENCIAMENTO",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unmapped DebtType"),
    };
}
