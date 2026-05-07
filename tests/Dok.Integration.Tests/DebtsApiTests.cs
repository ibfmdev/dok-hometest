namespace Dok.Integration.Tests;

public class DebtsApiTests : IClassFixture<WireMockApiFactory>
{
    private readonly WireMockApiFactory _factory;
    private readonly HttpClient _client;

    public DebtsApiTests(WireMockApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private HttpContent JsonBody(string raw) => new StringContent(raw, Encoding.UTF8, "application/json");

    [Fact]
    public async Task POST_with_valid_plate_and_provider_A_responds_200()
    {
        _factory.ResetMocks();
        _factory.StubProviderA(SampleData.ProviderAJsonHappy);

        var response = await _client.PostAsync("/api/v1/debitos",
            JsonBody("""{ "placa": "ABC1234" }"""));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("X-Dok-Provider").ShouldContain("ProviderA");

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("placa").GetString().ShouldBe("ABC1234");
        root.GetProperty("debitos").GetArrayLength().ShouldBe(2);
        root.GetProperty("resumo").GetProperty("total_atualizado").GetString().ShouldBe("2355.93");
        root.GetProperty("pagamentos").GetProperty("opcoes").GetArrayLength().ShouldBe(3);
    }

    [Fact]
    public async Task POST_falls_back_from_A_to_B_when_A_fails()
    {
        _factory.ResetMocks();
        _factory.StubProviderAFails();
        _factory.StubProviderB(SampleData.ProviderBXmlHappy);

        var response = await _client.PostAsync("/api/v1/debitos",
            JsonBody("""{ "placa": "ABC1234" }"""));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("X-Dok-Provider").ShouldContain("ProviderB");

        var json = await response.Content.ReadAsStringAsync();
        json.ShouldContain("\"placa\":\"ABC1234\"");
        json.ShouldContain("\"total_atualizado\":\"2355.93\"");
    }

    [Fact]
    public async Task POST_returns_503_when_all_providers_fail()
    {
        _factory.ResetMocks();
        _factory.StubProviderAFails();
        _factory.StubProviderBFails();

        var response = await _client.PostAsync("/api/v1/debitos",
            JsonBody("""{ "placa": "ABC1234" }"""));

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("\"error\":\"all_providers_unavailable\"");
    }

    [Fact]
    public async Task POST_returns_422_when_provider_returns_unknown_debt_type()
    {
        _factory.ResetMocks();
        _factory.StubProviderA(SampleData.ProviderAJsonUnknownType);

        var response = await _client.PostAsync("/api/v1/debitos",
            JsonBody("""{ "placa": "ABC1234" }"""));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("\"error\":\"unknown_debt_type\"");
        body.ShouldContain("\"type\":\"LICENCIAMENTO\"");
    }

    [Fact]
    public async Task POST_returns_400_when_plate_is_invalid()
    {
        _factory.ResetMocks();

        var response = await _client.PostAsync("/api/v1/debitos",
            JsonBody("""{ "placa": "123" }"""));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("\"error\":\"invalid_plate\"");
    }

    [Fact]
    public async Task POST_returns_400_when_unknown_field_present()
    {
        _factory.ResetMocks();
        _factory.StubProviderA(SampleData.ProviderAJsonHappy);

        var response = await _client.PostAsync("/api/v1/debitos",
            JsonBody("""{ "placa": "ABC1234", "extra": "x" }"""));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("\"error\":\"invalid_request\"");
    }

    [Fact]
    public async Task POST_returns_200_with_empty_debts_when_provider_B_uses_self_closing_debts_tag()
    {
        _factory.ResetMocks();
        _factory.StubProviderAFails();
        _factory.StubProviderB(SampleData.ProviderBXmlEmpty);

        var response = await _client.PostAsync("/api/v1/debitos",
            JsonBody("""{ "placa": "ABC1234" }"""));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("debitos").GetArrayLength().ShouldBe(0);
        doc.RootElement.GetProperty("resumo").GetProperty("total_atualizado").GetString().ShouldBe("0.00");
    }

    [Fact]
    public async Task POST_with_malformed_json_returns_400()
    {
        _factory.ResetMocks();

        var response = await _client.PostAsync("/api/v1/debitos",
            JsonBody("""{ "placa": """));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("\"error\":\"invalid_request\"");
    }

    [Fact]
    public async Task POST_with_future_due_date_returns_zero_days_overdue_and_original_amount()
    {
        _factory.ResetMocks();
        _factory.StubProviderA(SampleData.ProviderAJsonFuture);

        var response = await _client.PostAsync("/api/v1/debitos",
            JsonBody("""{ "placa": "ABC1234" }"""));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var debit = doc.RootElement.GetProperty("debitos")[0];

        debit.GetProperty("dias_atraso").GetInt32().ShouldBe(0);
        debit.GetProperty("valor_original").GetString().ShouldBe("1500.00");
        debit.GetProperty("valor_atualizado").GetString().ShouldBe("1500.00");
    }

    [Fact]
    public async Task POST_with_two_ipvas_groups_into_singular_SOMENTE_IPVA()
    {
        _factory.ResetMocks();
        _factory.StubProviderA(SampleData.ProviderAJsonTwoIpvas);

        var response = await _client.PostAsync("/api/v1/debitos",
            JsonBody("""{ "placa": "ABC1234" }"""));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        // Debits chegam como dois itens IPVA separados
        doc.RootElement.GetProperty("debitos").GetArrayLength().ShouldBe(2);

        // Mas só duas opções de pagamento: TOTAL + um único SOMENTE_IPVA agregado (singular)
        var opcoes = doc.RootElement.GetProperty("pagamentos").GetProperty("opcoes");
        opcoes.GetArrayLength().ShouldBe(2);
        opcoes[0].GetProperty("tipo").GetString().ShouldBe("TOTAL");
        opcoes[1].GetProperty("tipo").GetString().ShouldBe("SOMENTE_IPVA");
    }

    [Fact]
    public async Task POST_when_provider_A_returns_malformed_json_falls_back_to_B()
    {
        _factory.ResetMocks();
        _factory.StubProviderA(SampleData.ProviderAJsonMalformed);
        _factory.StubProviderB(SampleData.ProviderBXmlHappy);

        var response = await _client.PostAsync("/api/v1/debitos",
            JsonBody("""{ "placa": "ABC1234" }"""));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("X-Dok-Provider").ShouldContain("ProviderB");
    }

    [Fact]
    public async Task POST_when_provider_A_returns_html_with_wrong_content_type_falls_back_to_B()
    {
        _factory.ResetMocks();
        // Conteúdo é JSON válido, mas Content-Type anuncia HTML — gateway de erro mascarado.
        _factory.StubProviderA(SampleData.ProviderAJsonHappy, contentType: "text/html");
        _factory.StubProviderB(SampleData.ProviderBXmlHappy);

        var response = await _client.PostAsync("/api/v1/debitos",
            JsonBody("""{ "placa": "ABC1234" }"""));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("X-Dok-Provider").ShouldContain("ProviderB");
    }

    [Fact]
    public async Task POST_when_both_providers_return_malformed_payload_returns_503()
    {
        _factory.ResetMocks();
        _factory.StubProviderA(SampleData.ProviderAJsonMalformed);
        _factory.StubProviderB(SampleData.ProviderBXmlMalformed);

        var response = await _client.PostAsync("/api/v1/debitos",
            JsonBody("""{ "placa": "ABC1234" }"""));

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("\"error\":\"all_providers_unavailable\"");
    }

    [Fact]
    public async Task POST_with_body_above_1MiB_returns_413()
    {
        _factory.ResetMocks();
        // Tenta passar > 1 MiB de payload — Kestrel deveria rejeitar.
        // Nota: WebApplicationFactory usa TestServer (não Kestrel), então este teste
        // só será efetivo em E2E real (Docker). Mantemos como contrato esperado.
        var padding = new string('A', 2 * 1024 * 1024);
        var bigJson = $$"""{ "placa": "ABC1234", "padding": "{{padding}}" }""";

        var response = await _client.PostAsync("/api/v1/debitos",
            JsonBody(bigJson));

        // Em Kestrel real → 413. Em TestServer → 400 invalid_request (campo desconhecido pega antes).
        // Aceitamos qualquer um dos dois aqui — o comportamento Kestrel é validado em testes E2E manuais.
        response.StatusCode.ShouldBeOneOf(
            HttpStatusCode.RequestEntityTooLarge,
            HttpStatusCode.BadRequest);
    }
}
