namespace Dok.Domain.Tests;

public class MultaInterestRuleTests
{
    private readonly MultaInterestRule _rule = new();
    private readonly DateOnly _today = new(2024, 5, 10);

    [Fact]
    public void Apply_with_85_days_overdue_matches_spec_example()
    {
        // 300,50 × 1% × 85 = 255,425 → HALF_UP 255,43 → total 555,93
        var debt = new Debt(DebtType.Multa, Money.Of(300.50m), new DateOnly(2024, 2, 15));
        var result = _rule.Apply(debt, _today);

        result.UpdatedAmount.Value.ShouldBe(555.93m);
        result.DaysOverdue.ShouldBe(85);
    }

    [Fact]
    public void Apply_when_not_overdue_returns_original()
    {
        var debt = new Debt(DebtType.Multa, Money.Of(100m), new DateOnly(2024, 6, 1));
        var result = _rule.Apply(debt, _today);

        result.UpdatedAmount.Value.ShouldBe(100m);
        result.DaysOverdue.ShouldBe(0);
    }

    [Fact]
    public void Apply_no_cap_so_high_days_yields_high_interest()
    {
        // 100 com 200 dias: 100 * 0.01 * 200 = 200 — sem teto, total 300
        var debt = new Debt(DebtType.Multa, Money.Of(100m), _today.AddDays(-200));
        var result = _rule.Apply(debt, _today);

        result.UpdatedAmount.Value.ShouldBe(300m);
    }

    [Fact]
    public void Type_is_Multa()
    {
        _rule.Type.ShouldBe(DebtType.Multa);
    }
}
