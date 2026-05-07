namespace Dok.Application.Tests;

public class PaymentSimulatorTests
{
    private readonly PaymentSimulator _sim = new();

    [Fact]
    public void Simulate_empty_list_returns_empty()
    {
        _sim.Simulate(Array.Empty<UpdatedDebt>()).ShouldBeEmpty();
    }

    [Fact]
    public void Simulate_with_one_ipva_returns_TOTAL_and_SOMENTE_IPVA()
    {
        var ipva = new UpdatedDebt(DebtType.Ipva, Money.Of(1500m), Money.Of(1800m),
            new DateOnly(2024, 1, 10), 121);

        var options = _sim.Simulate(new[] { ipva });

        options.Count.ShouldBe(2);
        options[0].Type.ShouldBe("TOTAL");
        options[1].Type.ShouldBe("SOMENTE_IPVA");
    }

    [Fact]
    public void Simulate_matches_spec_example_for_ABC1234()
    {
        // Exemplos da spec v2:
        // TOTAL = 2355.93, PIX 2238.13, 6x = 427.72, 12x = 229.67
        var ipva = new UpdatedDebt(DebtType.Ipva, Money.Of(1500m), Money.Of(1800m),
            new DateOnly(2024, 1, 10), 121);
        var multa = new UpdatedDebt(DebtType.Multa, Money.Of(300.50m), Money.Of(555.93m),
            new DateOnly(2024, 2, 15), 85);

        var options = _sim.Simulate(new[] { ipva, multa });

        options.Count.ShouldBe(3);

        // ----- TOTAL -----
        var total = options[0];
        total.Type.ShouldBe("TOTAL");
        total.Base.Value.ShouldBe(2355.93m);
        total.Pix.TotalWithDiscount.Value.ShouldBeInRange(2238.11m, 2238.15m);

        var totalParcelas = total.CreditCard.Installments;
        totalParcelas.Count.ShouldBe(3);
        totalParcelas[0].Quantity.ShouldBe(1);
        totalParcelas[0].Amount.Value.ShouldBe(2355.93m);
        totalParcelas[1].Quantity.ShouldBe(6);
        totalParcelas[1].Amount.Value.ShouldBeInRange(427.70m, 427.74m);
        totalParcelas[2].Quantity.ShouldBe(12);
        totalParcelas[2].Amount.Value.ShouldBeInRange(229.65m, 229.69m);

        // ----- SOMENTE_IPVA -----
        var soIpva = options[1];
        soIpva.Type.ShouldBe("SOMENTE_IPVA");
        soIpva.Base.Value.ShouldBe(1800m);
        soIpva.Pix.TotalWithDiscount.Value.ShouldBe(1710m);

        // ----- SOMENTE_MULTA -----
        var soMulta = options[2];
        soMulta.Type.ShouldBe("SOMENTE_MULTA");
        soMulta.Base.Value.ShouldBe(555.93m);
        soMulta.Pix.TotalWithDiscount.Value.ShouldBeInRange(528.11m, 528.15m);
    }

    [Fact]
    public void Simulate_with_multiple_same_type_returns_singular_SOMENTE_TIPO()
    {
        // 2 IPVAs no input → 1 opção SOMENTE_IPVA (singular, soma os valores)
        var ipva1 = new UpdatedDebt(DebtType.Ipva, Money.Of(500m), Money.Of(550m), new DateOnly(2024, 1, 1), 130);
        var ipva2 = new UpdatedDebt(DebtType.Ipva, Money.Of(700m), Money.Of(770m), new DateOnly(2024, 2, 1), 99);

        var options = _sim.Simulate(new[] { ipva1, ipva2 });

        options.Count.ShouldBe(2); // TOTAL + SOMENTE_IPVA
        options[0].Type.ShouldBe("TOTAL");
        options[0].Base.Value.ShouldBe(1320m);
        options[1].Type.ShouldBe("SOMENTE_IPVA");
        options[1].Base.Value.ShouldBe(1320m);
    }
}
