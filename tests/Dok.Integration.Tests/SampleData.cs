namespace Dok.Integration.Tests;

public static class SampleData
{
    public const string ProviderAJsonHappy = """
        {
          "vehicle": "ABC1234",
          "debts": [
            { "type": "IPVA", "amount": 1500.00, "due_date": "2024-01-10" },
            { "type": "MULTA", "amount": 300.50, "due_date": "2024-02-15" }
          ]
        }
        """;

    public const string ProviderBXmlHappy = """
        <response>
          <plate>ABC1234</plate>
          <debts>
            <debt><category>IPVA</category><value>1500.00</value><expiration>2024-01-10</expiration></debt>
            <debt><category>MULTA</category><value>300.50</value><expiration>2024-02-15</expiration></debt>
          </debts>
        </response>
        """;

    public const string ProviderBXmlEmpty = """
        <response>
          <plate>ABC1234</plate>
          <debts/>
        </response>
        """;

    public const string ProviderAJsonUnknownType = """
        {
          "vehicle": "ABC1234",
          "debts": [
            { "type": "LICENCIAMENTO", "amount": 200.00, "due_date": "2024-01-10" }
          ]
        }
        """;

    /// <summary>Débito futuro: dias_atraso ≤ 0, juros = 0.</summary>
    public const string ProviderAJsonFuture = """
        {
          "vehicle": "ABC1234",
          "debts": [
            { "type": "IPVA", "amount": 1500.00, "due_date": "2024-06-10" }
          ]
        }
        """;

    /// <summary>Múltiplos débitos do mesmo tipo (testa SOMENTE_IPVA singular agregado).</summary>
    public const string ProviderAJsonTwoIpvas = """
        {
          "vehicle": "ABC1234",
          "debts": [
            { "type": "IPVA", "amount": 500.00, "due_date": "2024-01-01" },
            { "type": "IPVA", "amount": 700.00, "due_date": "2024-02-01" }
          ]
        }
        """;

    /// <summary>JSON malformado (estrutura inválida): debts deveria ser array, vem string.</summary>
    public const string ProviderAJsonMalformed = """
        { "vehicle": "ABC1234", "debts": "NOT_AN_ARRAY" }
        """;

    /// <summary>XML malformado: tag não fechada.</summary>
    public const string ProviderBXmlMalformed = """
        <response><plate>ABC1234</plate><debts><debt><category>IPVA
        """;
}
