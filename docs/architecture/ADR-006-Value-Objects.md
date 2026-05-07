# ADR-006 — Modelagem do domínio: Value Objects

**Status:** aceito
**Data:** 2026-04-27

## Contexto

Nas assinaturas dos services e regras, o que tipa cada conceito do domínio? Usar tipos primitivos (`string` para placa, `decimal` para valores monetários, `string` para tipo de débito) é o caminho mais curto. Mas tem custos.

A spec tem invariantes não-triviais que precisam ser garantidas:

- **Placa**: precisa casar regex Mercosul (`AAA0A00`) ou antigo (`AAA0000`).
- **Valor monetário**: `decimal`, não `double`/`float`; arredondamento HALF_UP a 2 casas; serializa como string no JSON.
- **Tipo de débito**: apenas `IPVA` e `MULTA` no momento; tipo desconhecido → 422.
- **Placa em logs**: mascarar para LGPD (`ABC****`).

## Conceitos candidatos a Value Object

| Conceito       | Por que VO ajuda                                                                                                                                       |
|----------------|---------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Plate`        | Validação centralizada; impossível ter `Plate` inválida em circulação; método `.Masked()` para LGPD; igualdade estrutural sem cuidado manual         |
| `Money`        | Forçar `decimal`; encapsular HALF_UP; serialização como string controlada em um lugar; impede "misturar" `decimal` arbitrário com valor monetário     |
| `DebtType`     | Pode ser `enum` simples + parser, ou VO. Enum é mais idiomático em C# para conjuntos pequenos e fechados                                              |
| `DueDate`      | Provavelmente exagero — `DateOnly` nativo do .NET resolve                                                                                              |

## Tradeoffs gerais (Value Object vs primitivo)

### A favor de Value Objects

| # | Benefício | O que isso significa na prática |
|---|---|---|
| 1 | **Validação centralizada** | A regra (regex Mercosul, HALF_UP) vive **em um lugar só**. Mudou? Mexe num arquivo. Com primitivo, a validação tende a se replicar (Controller, Service, Calculator) — ou pior, alguém esquece e bug passa. |
| 2 | **Validade por construção** | Se a assinatura aceita `Plate`, é matematicamente impossível receber valor inválido. Reduz "defensive programming" — ninguém precisa revalidar. |
| 3 | **Tipagem forte** | `Calculate(Plate p, Money amount)` é diferente de `Calculate(string s, decimal d)`. Compilador pega trocas de argumento. Em código com vários `decimal`s circulando, isso vale ouro. |
| 4 | **Vocabulário do domínio no código** | Ler a assinatura conta a história. `Plate.Masked()`, `Money.ToJsonString()`, `Money + Money` são auto-explicativos. |
| 5 | **Igualdade e imutabilidade automáticas** | `readonly record struct` dá `Equals`/`GetHashCode` por valor + imutabilidade sem boilerplate. |
| 6 | **Performance** | `readonly record struct` é stack-allocated — zero pressão de GC. Para um struct com 1 campo (`Plate` = string, `Money` = decimal), o overhead vs primitivo é praticamente nulo. |
| 7 | **Mascaramento LGPD trivial** | `plate.Masked()` é um método nativo do tipo. Sem VO, mascaramento vira política manual em cada `LogXxx` — alguém vai esquecer. |
| 8 | **Refatoração futura segura** | Adicionar invariante novo (ex: rejeitar placas com letras proibidas) afeta um arquivo. Sem VO, é grep + reza. |

### Contra Value Objects (custos honestos)

| # | Custo | Quanto pesa aqui |
|---|---|---|
| 1 | **Boilerplate inicial** | Cada VO = ~30 linhas (struct + factory + ToString + serialização). Para 2 VOs (`Plate` + `Money`) ≈ 60 linhas extras + ~20 de testes. |
| 2 | **Conversores para framework** | `Plate` precisa de `JsonConverter` (request `{"placa":"..."}` → `Plate`). `Money` precisa de converter que serializa como **string** decimal no JSON de saída (spec exige). Cada conversor = ~15 linhas. |
| 3 | **Curva de aprendizado** | Devs vindos de stacks que não usam VO podem estranhar. Em time sênior é nulo; em time misto, pode pedir um ADR/onboarding curto. |
| 4 | **Risco de over-engineering** | Se aplicar a tudo (`Quantity`, `DueDate`, `ProviderName`...), o código vira sopa de tipos triviais. Mitigação: critério explícito (ver "Quando NÃO usar VO" abaixo). |
| 5 | **Operadores e fronteiras** | `Money` precisa expor `+`, `*` (com `decimal` ou outro `Money`?). Cada operação adicional = uma decisão de design. Risco: criar uma "API rica" que vira ela mesma uma fonte de bugs. Mitigação: começar com o mínimo (`+`, `*` com `decimal`) e crescer só sob demanda. |
| 6 | **Integração com bibliotecas externas** | Logger (Serilog), validador (FluentValidation), serializador (System.Text.Json) podem precisar de configuração extra. Custo único na inicialização (`Program.cs`). |

### Quando NÃO usar VO (critério)

Aplicamos VO **apenas** quando o conceito tem **pelo menos uma** das características:

- Invariantes de validação não-triviais (placa).
- Regras de formatação/arredondamento que precisam ser uniformes (dinheiro).
- Necessidade de mascaramento ou apresentação especial (LGPD).
- Risco real de troca de argumentos (vários `decimal` na mesma assinatura).

**Não** aplicamos a:

- Conceitos sem invariantes (ex.: descrição, nome livre).
- Tipos efêmeros que não atravessam camadas.
- Conceitos que `DateOnly`/`TimeSpan`/`Guid` já cobrem bem.

## Análise por conceito

### Plate — VO (sim)

- **Por que paga aluguel**: regex Mercosul/antigo (não-trivial), normalização (uppercase/trim), mascaramento LGPD obrigatório, validação ligada a HTTP 400.
- **Custo**: 1 `JsonConverter` para entrada (`"placa": "ABC1234"` → `Plate`); ~30 linhas no struct.
- **Veredito**: o conceito mais defensável de toda a aplicação para virar VO. Sem ele, mascaramento LGPD vira política frágil.

```csharp
public readonly record struct Plate
{
    private static readonly Regex Mercosul = new(@"^[A-Z]{3}\d[A-Z]\d{2}$", RegexOptions.Compiled);
    private static readonly Regex Antiga   = new(@"^[A-Z]{3}\d{4}$",         RegexOptions.Compiled);

    public string Value { get; }
    private Plate(string value) => Value = value;

    public static Plate Parse(string raw)
    {
        var normalized = raw?.Trim().ToUpperInvariant() ?? "";
        if (!Mercosul.IsMatch(normalized) && !Antiga.IsMatch(normalized))
            throw new InvalidPlateException(raw);
        return new Plate(normalized);
    }

    public string Masked() => Value.Length >= 3 ? $"{Value[..3]}****" : "****";
    public override string ToString() => Value;
}
```

### Money — VO (sim)

- **Por que paga aluguel**: spec exige HALF_UP em todo arredondamento e **string** decimal no JSON de saída ("evita perda de precisão de float"). Centralizar isso evita que algum `decimal` cru escape com vírgula em vez de ponto, ou com 3 casas, ou com `MidpointRounding.ToEven` (default do .NET).
- **Custo**: operadores básicos + serializador. ~40 linhas + testes.
- **Veredito**: o segundo mais defensável. Mais valioso ainda em código com muitos cálculos (juros + Price/PMT + descontos).

```csharp
public readonly record struct Money
{
    public decimal Value { get; }
    private Money(decimal value) => Value = value;

    public static Money Of(decimal v) =>
        new(Math.Round(v, 2, MidpointRounding.AwayFromZero));

    public static Money operator +(Money a, Money b) => Of(a.Value + b.Value);
    public static Money operator -(Money a, Money b) => Of(a.Value - b.Value);
    public static Money operator *(Money a, decimal factor) => Of(a.Value * factor);

    public string ToJsonString() =>
        Value.ToString("F2", CultureInfo.InvariantCulture);
}
```

### DebtType — enum (não VO)

- **Por que enum e não VO**: conjunto **fechado e pequeno** (`IPVA`, `MULTA` hoje, talvez `LICENCIAMENTO` amanhã). Enum é idiomático em C#, dá `switch` exaustivo, integra com bibliotecas (System.Text.Json suporta nativo), e custa ~3 linhas.
- **Onde mora a "validação"**: parser `DebtTypeMapper.Parse(string raw)` — usado pelos adapters de provider — converte string crua para enum; valor não mapeado lança `UnknownDebtTypeException` (vai virar HTTP 422 no middleware).
- **Veredito**: VO aqui seria over-engineering. Enum + parser resolvem com elegância.

```csharp
public enum DebtType { Ipva, Multa }

internal static class DebtTypeMapper
{
    public static DebtType Parse(string raw) => raw?.Trim().ToUpperInvariant() switch
    {
        "IPVA"  => DebtType.Ipva,
        "MULTA" => DebtType.Multa,
        _       => throw new UnknownDebtTypeException(raw)
    };
}
```

### DueDate — `DateOnly` nativo (não VO)

- Já é tipo do BCL (`System.DateOnly`, .NET 6+). Tem semântica de "data sem hora". Sem invariantes adicionais aqui — se aparecer alguma (ex: rejeitar datas no futuro distante), promovemos a VO.

## Riscos e mitigações

| Risco | Mitigação |
|---|---|
| `JsonConverter` mal escrito quebra desserialização da request | Cobertura por testes unitários do converter (caminhos válido/inválido/null/whitespace). |
| `Money` perdendo precisão por uso indevido (ex: cálculo intermediário com `decimal` cru e arredondamento só no final) | Política: cálculos de cadeia mantêm `decimal` puro internamente; `Money.Of(...)` aplicado apenas nas fronteiras (entrada do domínio e saída para DTO). Documentar no XML doc do `Money`. |
| Excesso de VOs polui o código | Aplicar critério "tem invariante real?" antes de promover qualquer conceito a VO. |
| Time não familiar com VO | Documentar no README e neste ADR. Para o desafio, é um candidato sênior com IA — risco baixo. |

## Decisão

- **`Plate`** → `readonly record struct` + `JsonConverter` para entrada.
- **`Money`** → `readonly record struct` com HALF_UP central e `ToJsonString()` para serialização como string decimal.
- **`DebtType`** → `enum` + `DebtTypeMapper` para parsing controlado.
- **`DueDate`** → `DateOnly` nativo, sem VO.

## Justificativa

1. A spec do desafio impõe regras estruturadas (regex Mercosul, HALF_UP, string-no-JSON, mascaramento LGPD) que **se beneficiam diretamente** de validação por construção e centralização.
2. O custo de boilerplate é localizado (~80 linhas de produção + ~50 de testes para os 2 VOs) e amortizado por evitar bugs de "validação esquecida em algum lugar".
3. `enum` para `DebtType` mantém C# idiomático; promover para VO sem motivo seria over-engineering — sinal contrário do que queremos passar na banca.
4. Defensável na narrativa: *"reduzi primitive obsession nos conceitos com invariantes reais (Plate, Money), mantive enum onde é idiomático (DebtType), e usei tipos do BCL onde basta (DueDate). VOs onde pagam aluguel, e nunca onde não pagam."*

---
