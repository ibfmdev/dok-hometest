using Dok.Application.Abstractions;

namespace Dok.Application;

public sealed class DebtsService(
    IDebtsCalculator calculator,
    IPaymentSimulator simulator) : IDebtsService
{
    public async Task<DebtsResult> GetAsync(Plate plate, CancellationToken ct)
    {
        var calc = await calculator.CalculateAsync(plate, ct);
        var options = simulator.Simulate(calc.Debts);
        return new DebtsResult(plate, calc.Debts, calc.Summary, options);
    }
}
