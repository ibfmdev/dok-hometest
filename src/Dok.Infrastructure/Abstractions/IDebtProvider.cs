namespace Dok.Infrastructure.Abstractions;

public interface IDebtProvider
{
    string Name { get; }
    Task<IReadOnlyList<Debt>> FetchAsync(Plate plate, CancellationToken ct);
}
