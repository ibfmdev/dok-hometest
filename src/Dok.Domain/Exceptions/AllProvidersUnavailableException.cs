namespace Dok.Domain.Exceptions;

public sealed class AllProvidersUnavailableException(IReadOnlyList<Exception> failures)
    : DomainException($"All {failures.Count} providers failed")
{
    public IReadOnlyList<Exception> Failures { get; } = failures;
}
