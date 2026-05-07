namespace Dok.Application.Tests;

public class DebtsCalculatorTests
{
    private readonly IReadOnlyDictionary<DebtType, IInterestRule> _rules = new Dictionary<DebtType, IInterestRule>
    {
        [DebtType.Ipva] = new IpvaInterestRule(),
        [DebtType.Multa] = new MultaInterestRule(),
    };

    private readonly IDebtsClock _clock = new FixedDebtsClock(new DateOnly(2024, 5, 10));

    [Fact]
    public async Task CalculateAsync_with_two_debts_returns_canonical_response_for_ABC1234()
    {
        var providers = Substitute.For<IDebtProviderChain>();
        providers
            .FetchDebtsAsync(Arg.Any<Plate>(), Arg.Any<CancellationToken>())
            .Returns(new Debt[] {
                new(DebtType.Ipva, Money.Of(1500m), new DateOnly(2024, 1, 10)),
                new(DebtType.Multa, Money.Of(300.50m), new DateOnly(2024, 2, 15)),
            });

        var calculator = new DebtsCalculator(providers, _rules, _clock, NullLogger<DebtsCalculator>.Instance);

        var result = await calculator.CalculateAsync(Plate.Parse("ABC1234"), default);

        result.Debts.Count.ShouldBe(2);
        result.Summary.TotalOriginal.Value.ShouldBe(1800.50m);
        result.Summary.TotalUpdated.Value.ShouldBe(2355.93m);

        var ipva = result.Debts[0];
        ipva.Type.ShouldBe(DebtType.Ipva);
        ipva.UpdatedAmount.Value.ShouldBe(1800.00m);
        ipva.DaysOverdue.ShouldBe(121);

        var multa = result.Debts[1];
        multa.Type.ShouldBe(DebtType.Multa);
        multa.UpdatedAmount.Value.ShouldBe(555.93m);
        multa.DaysOverdue.ShouldBe(85);
    }

    [Fact]
    public async Task CalculateAsync_with_zero_debts_returns_empty()
    {
        var providers = Substitute.For<IDebtProviderChain>();
        providers
            .FetchDebtsAsync(Arg.Any<Plate>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Debt>());

        var calculator = new DebtsCalculator(providers, _rules, _clock, NullLogger<DebtsCalculator>.Instance);

        var result = await calculator.CalculateAsync(Plate.Parse("ABC1234"), default);
        result.Debts.ShouldBeEmpty();
        result.Summary.TotalOriginal.Value.ShouldBe(0m);
        result.Summary.TotalUpdated.Value.ShouldBe(0m);
    }
}
