namespace Dok.Application.Abstractions;

public interface IDebtsService
{
    Task<DebtsResult> GetAsync(Plate plate, CancellationToken ct);
}
