namespace Dok.Domain.Abstractions;

public interface IDebtProviderChain
{
    Task<IReadOnlyList<Debt>> FetchDebtsAsync(Plate plate, CancellationToken ct);
}
