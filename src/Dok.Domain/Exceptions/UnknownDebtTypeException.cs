namespace Dok.Domain.Exceptions;

public sealed class UnknownDebtTypeException(string type)
    : DomainException($"Unknown debt type: {type}")
{
    public string Type { get; } = type;
}
