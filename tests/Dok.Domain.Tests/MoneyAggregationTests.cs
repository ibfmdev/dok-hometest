namespace Dok.Domain.Tests;

/// <summary>
/// Documenta o trade-off "sum-of-rounded vs round-of-sum" no <see cref="Money"/>.
/// O projeto adota <strong>sum-of-rounded</strong> (cada <see cref="UpdatedDebt"/> é
/// uma unidade contábil arredondada conforme HALF_UP da spec; somas posteriores partem
/// dos valores já arredondados). Em auditoria contábil rigorosa o canônico seria
/// round-of-sum; aqui priorizamos consistência entre subtotais e total mostrados ao
/// usuário (sem "penny drop" entre linhas e o total).
/// </summary>
public class MoneyAggregationTests
{
    [Fact]
    public void Sum_of_three_rounded_133s_is_399()
    {
        // 3 débitos de R$ 1,33 — caso simples, sem ambiguidade.
        var sum = Money.Of(1.33m) + Money.Of(1.33m) + Money.Of(1.33m);
        sum.Value.ShouldBe(3.99m);
        sum.ToJsonString().ShouldBe("3.99");
    }

    [Fact]
    public void Sum_of_rounded_diverges_from_round_of_sum_when_each_is_at_HALF_UP_boundary()
    {
        // Caso de borda: 0,005 arredonda HALF_UP (AwayFromZero) para 0,01 cada.
        // sum-of-rounded: Money.Of(0.005) + Money.Of(0.005) = 0.01 + 0.01 = 0.02
        // round-of-sum:   Money.Of(0.005 + 0.005)           = Money.Of(0.01) = 0.01
        var sumOfRounded = Money.Of(0.005m) + Money.Of(0.005m);
        var roundOfSum = Money.Of(0.005m + 0.005m);

        sumOfRounded.Value.ShouldBe(0.02m);
        roundOfSum.Value.ShouldBe(0.01m);
        sumOfRounded.Value.ShouldNotBe(roundOfSum.Value);
    }

    [Fact]
    public void Aggregating_two_partial_interests_uses_sum_of_rounded_strategy()
    {
        // Cenário realista: dois débitos onde os juros parciais arredondam para cima individualmente.
        // Operator + de Money sempre usa Money.Of nas operandas (que já vêm arredondadas),
        // garantindo que somas sucessivas sejam determinísticas e iguais à soma exibida ao usuário.
        var debt1Updated = Money.Of(100.005m); // 100.01
        var debt2Updated = Money.Of(200.005m); // 200.01

        var aggregated = debt1Updated + debt2Updated;
        aggregated.Value.ShouldBe(300.02m);

        // round-of-sum daria 300.01 — divergência de 1 centavo entre subtotais e total.
        Money.Of(100.005m + 200.005m).Value.ShouldBe(300.01m);
    }

    [Fact]
    public void Spec_example_aggregation_remains_exact_at_2_decimal_places()
    {
        // Exemplos da spec: 1500.00 + 300.50 = 1800.50; 1800.00 + 555.93 = 2355.93.
        // Quando os valores envolvidos têm 2 casas exatas, sum-of-rounded e round-of-sum coincidem.
        (Money.Of(1500.00m) + Money.Of(300.50m)).Value.ShouldBe(1800.50m);
        (Money.Of(1800.00m) + Money.Of(555.93m)).Value.ShouldBe(2355.93m);
    }
}
