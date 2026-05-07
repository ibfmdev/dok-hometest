namespace Dok.Domain.Exceptions;

public sealed class InvalidPlateException(string? raw)
    : DomainException($"Invalid plate format: {raw ?? "<null>"}")
{
    public string? Raw { get; } = raw;
}
