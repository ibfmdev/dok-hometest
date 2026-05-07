namespace Dok.Domain.Tests;

public class IpvaInterestRuleTests
{
    private readonly IpvaInterestRule _rule = new();
    private readonly DateOnly _today = new(2024, 5, 10);

    [Fact]
    public void Apply_with_121_days_overdue_caps_at_20_percent()
    {
        // 1500 com 121 dias: 0,33% × 1500 × 121 = 598,95; cap = 20% × 1500 = 300; usa cap → 1800,00
        var debt = new Debt(DebtType.Ipva, Money.Of(1500m), new DateOnly(2024, 1, 10));
        var result = _rule.Apply(debt, _today);

        result.UpdatedAmount.Value.ShouldBe(1800.00m);
        result.DaysOverdue.ShouldBe(121);
    }

    [Fact]
    public void Apply_when_not_overdue_returns_original_with_zero_days()
    {
        var debt = new Debt(DebtType.Ipva, Money.Of(1500m), new DateOnly(2024, 6, 10));
        var result = _rule.Apply(debt, _today);

        result.UpdatedAmount.Value.ShouldBe(1500.00m);
        result.DaysOverdue.ShouldBe(0);
    }

    [Fact]
    public void Apply_when_due_today_returns_original()
    {
        var debt = new Debt(DebtType.Ipva, Money.Of(1500m), _today);
        var result = _rule.Apply(debt, _today);

        result.UpdatedAmount.Value.ShouldBe(1500.00m);
        result.DaysOverdue.ShouldBe(0);
    }

    [Fact]
    public void Apply_under_cap_uses_proportional_interest()
    {
        // 1000 com 30 dias: 0,33% × 1000 × 30 = 99 (cap = 200 — não atinge)
        var debt = new Debt(DebtType.Ipva, Money.Of(1000m), _today.AddDays(-30));
        var result = _rule.Apply(debt, _today);

        result.UpdatedAmount.Value.ShouldBe(1099m);
        result.DaysOverdue.ShouldBe(30);
    }

    [Fact]
    public void Type_is_Ipva()
    {
        _rule.Type.ShouldBe(DebtType.Ipva);
    }
}
