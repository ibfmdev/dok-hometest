namespace Dok.Application.Tests;

public class DebtsServiceTests
{
    [Fact]
    public async Task GetAsync_orchestrates_calculator_then_simulator()
    {
        var calc = Substitute.For<IDebtsCalculator>();
        var ipva = new UpdatedDebt(DebtType.Ipva, Money.Of(1500m), Money.Of(1800m), new DateOnly(2024, 1, 10), 121);
        var summary = new DebtsSummary(Money.Of(1500m), Money.Of(1800m));
        calc.CalculateAsync(Arg.Any<Plate>(), Arg.Any<CancellationToken>())
            .Returns(new CalculatorResult(new[] { ipva }, summary));

        var sim = new PaymentSimulator();
        var service = new DebtsService(calc, sim);

        var result = await service.GetAsync(Plate.Parse("ABC1234"), default);

        result.Plate.Value.ShouldBe("ABC1234");
        result.Debts.Count.ShouldBe(1);
        result.Summary.TotalUpdated.Value.ShouldBe(1800m);
        result.Options.Count.ShouldBe(2); // TOTAL + SOMENTE_IPVA
    }
}
