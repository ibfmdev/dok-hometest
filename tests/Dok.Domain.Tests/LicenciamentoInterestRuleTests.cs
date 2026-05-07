namespace Dok.Domain.Tests;

public class LicenciamentoInterestRuleTests
{
    private readonly LicenciamentoInterestRule _rule = new();
    private readonly DateOnly _today = new(2024, 5, 10);

    [Fact]
    public void Type_is_Licenciamento()
    {
        _rule.Type.ShouldBe(DebtType.Licenciamento);
    }

    [Fact]
    public void Apply_when_not_overdue_returns_original_with_zero_days()
    {
        var debt = new Debt(DebtType.Licenciamento, Money.Of(1000m), new DateOnly(2024, 6, 10));
        var result = _rule.Apply(debt, _today);

        result.UpdatedAmount.Value.ShouldBe(1000.00m);
        result.DaysOverdue.ShouldBe(0);
    }

    [Fact]
    public void Apply_when_due_today_returns_original()
    {
        var debt = new Debt(DebtType.Licenciamento, Money.Of(1000m), _today);
        var result = _rule.Apply(debt, _today);

        result.UpdatedAmount.Value.ShouldBe(1000.00m);
        result.DaysOverdue.ShouldBe(0);
    }

    [Fact]
    public void Apply_under_cap_uses_proportional_interest()
    {
        // 1000 com 10 dias: 1% × 1000 × 10 = 100 (cap = 200 — não atinge)
        var debt = new Debt(DebtType.Licenciamento, Money.Of(1000m), _today.AddDays(-10));
        var result = _rule.Apply(debt, _today);

        result.UpdatedAmount.Value.ShouldBe(1100m);
        result.DaysOverdue.ShouldBe(10);
    }

    [Fact]
    public void Apply_when_exceeding_cap_uses_cap_value()
    {
        // 1000 com 30 dias: 1% × 1000 × 30 = 300; cap = 20% × 1000 = 200; usa cap → 1200,00
        var debt = new Debt(DebtType.Licenciamento, Money.Of(1000m), _today.AddDays(-30));
        var result = _rule.Apply(debt, _today);

        result.UpdatedAmount.Value.ShouldBe(1200.00m);
        result.DaysOverdue.ShouldBe(30);
    }
}
