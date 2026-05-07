namespace Dok.Application.Abstractions;

public interface IDebtsCalculator
{
    Task<CalculatorResult> CalculateAsync(Plate plate, CancellationToken ct);
}
