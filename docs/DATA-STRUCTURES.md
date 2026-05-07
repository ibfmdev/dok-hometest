# Data structures do Dok — o que cada uma é e por quê

> Doc de estudo. Inventário dos tipos de dados do projeto, mapeados por intenção. Inclui escolha entre `class`/`struct`/`record`, mutabilidade, e por que cada decisão paga aluguel.

## Índice

1. [Value Objects (Domain)](#value-objects-domain)
2. [Entidades de domínio (records)](#entidades-de-domínio-records)
3. [Enum + mapper de tipo](#enum--mapper-de-tipo)
4. [Hierarquia de exceções de domínio](#hierarquia-de-exceções-de-domínio)
5. [Models da Application (records de orquestração)](#models-da-application-records-de-orquestração)
6. [State holder cross-cutting](#state-holder-cross-cutting)
7. [Lookup table (dicionário de strategy)](#lookup-table-dicionário-de-strategy)
8. [DTOs da fronteira HTTP](#dtos-da-fronteira-http)
9. [Provider response shapes (privados)](#provider-response-shapes-privados)

---

## Value Objects (Domain)

### `Plate` — `readonly record struct`

Arquivo: `src/Dok.Domain/Plate.cs`.

```csharp
public readonly record struct Plate
{
    public string Value { get; }
    private Plate(string value) => Value = value;
    public static Plate Parse(string? raw) { … }
    public static bool TryParse(string? raw, out Plate plate) { … }
    public string Masked() => …;
}
```

**Quatro adjetivos, quatro decisões:**

- `readonly` — garante imutabilidade ao compilador (sem mutação acidental).
- `record` — equality estrutural automática (`p1 == p2` se `Value` bate). Necessário pra usar `Plate` como key em `Dictionary` ou comparar em testes.
- `struct` — alocada em stack. `Plate` é pequena (uma `string`) e short-lived (uma por request); evita pressão no GC.
- `public` Value, `private` ctor — **smart constructor**. Único caminho de criação válido é `Plate.Parse`, que normaliza (`Trim().ToUpperInvariant()`) e valida (regex Mercosul/Antiga). Quem tem `Plate` em mãos tem prova de validade no tipo.

**Por que VO em vez de `string`:** elimina primitive obsession. `FetchAsync(Plate plate)` na assinatura prova validade ao compilador. Logging policy (`PlateDestructuringPolicy`) pode mascarar exclusivamente esse tipo. Refactor de "renomear placa pra plate_id" não vira find-and-replace de string.

**Edge case conhecido:** `default(Plate)` cria instância com `Value == null` — toda struct é default-constructible. Mitigação: único caminho de produção é JsonConverter → `Plate.Parse`. Documentado como tradeoff aceito.

### `Money` — `readonly record struct`

Arquivo: `src/Dok.Domain/Money.cs`.

```csharp
public readonly record struct Money
{
    public static readonly Money Zero = new(0m);
    public decimal Value { get; }
    private Money(decimal value) => Value = value;
    public static Money Of(decimal v) => new(Math.Round(v, 2, MidpointRounding.AwayFromZero));
    public static Money operator +(Money a, Money b) => Of(a.Value + b.Value);
    public string ToJsonString() => Value.ToString("F2", CultureInfo.InvariantCulture);
}
```

- Mesma decisão estrutural de `Plate`: VO imutável em stack, smart constructor.
- **`Of(decimal)` aplica `MidpointRounding.AwayFromZero`** — esse é o "HALF_UP" tradicional, exigido por contabilidade brasileira (R$1,255 → R$1,26, não R$1,25 como o "banker's rounding" default do `Math.Round`).
- **Operadores `+`, `-`, `*`** retornam `Money` já arredondado. Aritmética financeira segura por construção.
- **`ToJsonString()`** garante representação `"1800.00"` (string, não número) — exigência da spec; `MoneyJsonConverter` chama essa formatação.
- **`Zero` static readonly** — sentinel pra casos onde `Money` opcional precisa de default sem `Nullable<Money>`.

**Por que `decimal` e não `double`:** `decimal` é base-10 IEEE 754-2008, exato pra valores monetários até ~28 dígitos. `double` é base-2 e não consegue representar `0.025` exatamente. Numa fórmula como Price/PMT com `(1+i)^12`, o erro acumulado em `double` estoura a tolerância de R$0,02 da spec.

---

## Entidades de domínio (records)

### `Debt` — `sealed record`

```csharp
public sealed record Debt(DebtType Type, Money OriginalAmount, DateOnly DueDate);
```

- `record` (não `record struct`) — heap allocation; reference type. Justificativa: passa por listas e grupos do LINQ; ergonomia > micro-otimização.
- `sealed` — herança proibida. Effective Java rule. Estende-se via composição.
- **3 campos**, todos imutáveis (`record` com positional ctor). Estado puro.
- `DateOnly` em vez de `DateTime` — semântica clara: vencimento é dia, não instante. Sem timezone.

### `UpdatedDebt` — `sealed record`

```csharp
public sealed record UpdatedDebt(
    DebtType Type, Money OriginalAmount, Money UpdatedAmount, DateOnly DueDate, int DaysOverdue);
```

- "`Debt` depois da rule aplicada". Carrega o que foi mostrado na response.
- **`int DaysOverdue`** — 0 quando não em atraso. Calculado uma vez na rule, não recomputado.
- **Não substitui `Debt`** — coexistem porque o pipeline tem 2 fases: input canônico (`Debt`) → cálculo (`UpdatedDebt`).

---

## Enum + mapper de tipo

### `DebtType` — `enum`

```csharp
public enum DebtType { Ipva, Multa }
```

- Enum simples, valores subjacentes default (`int`).
- **Convenção interna:** PascalCase (`Ipva`, não `IPVA`). A representação "wire" (string da spec) é problema do mapper.
- **Aberto a extensão por adição:** uma nova categoria (ex: `Licenciamento`, `Dpvat`) entra como mais um valor + entrada no mapper + nova `IInterestRule`. Skill `/add-debt-type` automatiza esse fluxo.

### `DebtTypeMapper` — `static class`

```csharp
public static class DebtTypeMapper
{
    public static DebtType Parse(string? raw) => (raw?.Trim().ToUpperInvariant()) switch
    {
        "IPVA"  => DebtType.Ipva,
        "MULTA" => DebtType.Multa,
        null or "" => throw new UnknownDebtTypeException(raw ?? "<null>"),
        var other  => throw new UnknownDebtTypeException(other),
    };

    public static string ToWire(DebtType type) => type switch
    {
        DebtType.Ipva  => "IPVA",
        DebtType.Multa => "MULTA",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unmapped DebtType"),
    };
}
```

- **Tradução boundary ↔ domínio:** spec usa `"IPVA"`, domínio usa `DebtType.Ipva`. O mapper é a única ponte.
- `Parse` — smart constructor estilo. Lança `UnknownDebtTypeException` no input inválido (vira 422). Provider devolvendo `"DPVAT"` ou qualquer outro tipo não mapeado vira 422 imediato — interpretação estrita da spec.
- `ToWire` — caminho inverso. Lança `ArgumentOutOfRangeException` se enum cresce e o switch esquece — sentinel defensivo, não-de-domínio.
- **Por que não `[EnumMember(Value = "IPVA")]` + `JsonStringEnumConverter`:** controle explícito sobre erros (queremos mensagem rica + exception type customizado pra mapear pra 422). Converter genérico não dá esse poder.

---

## Hierarquia de exceções de domínio

```
DomainException (abstract)            // base aberta
├── InvalidPlateException             // → 400
├── UnknownDebtTypeException          // → 422
└── AllProvidersUnavailableException  // → 503
```

Arquivos: `src/Dok.Domain/Exceptions/*.cs`.

```csharp
public abstract class DomainException(string message) : Exception(message);

public sealed class InvalidPlateException(string? raw)
    : DomainException($"Invalid plate format: {raw ?? "<null>"}")
{ public string? Raw { get; } = raw; }

public sealed class UnknownDebtTypeException(string type)
    : DomainException($"Unknown debt type: {type}")
{ public string Type { get; } = type; }

public sealed class AllProvidersUnavailableException(IReadOnlyList<Exception> failures)
    : DomainException($"All {failures.Count} providers failed")
{ public IReadOnlyList<Exception> Failures { get; } = failures; }
```

**Padrão "base aberta, folhas seladas":**
- Base `abstract` — projetada pra ser herdada (não-sealed).
- Folhas `sealed` — fecham a hierarquia ali. Ninguém deriva de `InvalidPlateException`.

**Por que cada folha carrega payload (`Raw`, `Type`, `Failures`):** o handler global precisa enriquecer o response (ex: `{"error":"unknown_debt_type","type":"LICENCIAMENTO"}`) sem reparsar mensagem. Dado estruturado > string parsing.

**LGPD:** `InvalidPlateException.Raw` guarda o input cru pra debug interno, **mas nunca é logado direto** — o `DomainExceptionHandler` loga só `Length`, e o body de erro só carrega `"invalid_plate"` (a placa não vaza no response).

---

## Models da Application (records de orquestração)

Arquivo: `src/Dok.Application/Models.cs`.

```csharp
public sealed record DebtsResult(Plate Plate, IReadOnlyList<UpdatedDebt> Debts, DebtsSummary Summary, IReadOnlyList<PaymentOption> Options);
public sealed record DebtsSummary(Money TotalOriginal, Money TotalUpdated);
public sealed record CalculatorResult(IReadOnlyList<UpdatedDebt> Debts, DebtsSummary Summary);
public sealed record PaymentOption(string Type, Money Base, PixOption Pix, CreditCardOption CreditCard);
public sealed record PixOption(Money TotalWithDiscount);
public sealed record CreditCardOption(IReadOnlyList<Installment> Installments);
public sealed record Installment(int Quantity, Money Amount);
```

**Tudo `sealed record`. Por quê:**
- `record` — value-like equality + imutabilidade automática. Records são o tipo natural pra "data carrier" entre camadas.
- `sealed` — não foram projetados pra herança. Bloch.
- `IReadOnlyList<T>` em vez de `List<T>` — exposição mínima. Quem recebe não pode mutar.

**Hierarquia de composição:**
- `DebtsResult` (top-level) é o que `DebtsService` retorna pra Controller.
- `CalculatorResult` é intermediário, retorno do `DebtsCalculator` pra `DebtsService`. **Não vai pra fora da Application.**
- `PaymentOption` agrega `PixOption` + `CreditCardOption` + `Installment`s — espelha estrutura da response da spec.

**Por que separar `CalculatorResult` de `DebtsResult`:** decomposição da Application. `DebtsCalculator` não sabe da `Plate` (recebe ela só pra delegar pra chain). Quem amarra é o `DebtsService` (facade) — ele junta `Plate + CalculatorResult + Options` no `DebtsResult` final.

---

## State holder cross-cutting

### `ProviderUsage` — `sealed class` mutável (scoped)

Arquivo: `src/Dok.Domain/ProviderUsage.cs`.

```csharp
public sealed class ProviderUsage
{
    public string? Name { get; private set; }
    public void Mark(string name) => Name = name;
}
```

**Aqui o padrão muda — única classe de domínio mutável.**

- Não é VO, não é record, não é entidade. É **state holder de request**.
- `Scoped` lifetime no DI — uma instância por request HTTP.
- Fluxo: `DebtProviderChain` chama `usage.Mark("ProviderA")` quando A responde com sucesso. O middleware `UseProviderHeader` lê `usage.Name` ao final da request e adiciona o header `X-Dok-Provider`.
- **Por que `class` mutável e não record:** record com mutação não faz sentido — record é pra equality estrutural de dados imutáveis. Aqui queremos identidade per-request com mutação controlada (uma escrita, várias leituras).
- **Por que em `Dok.Domain`:** ele é puro (zero deps em ASP.NET). Vive em domínio porque é shared state cross-camada — Infrastructure escreve, Api lê. Não tem nada de framework dentro.

---

## Lookup table (dicionário de strategy)

```csharp
services.AddSingleton<IReadOnlyDictionary<DebtType, IInterestRule>>(sp =>
    sp.GetServices<IInterestRule>().ToDictionary(r => r.Type));
```

**`IReadOnlyDictionary<DebtType, IInterestRule>`** registrado como singleton.

- **Não é tipo nominal** — é só uma interface BCL.
- **Construído na DI** a partir de `GetServices<IInterestRule>()` — ou seja, **registrar uma rule nova faz o dicionário ganhar entrada automaticamente**. Zero alteração no `DebtsCalculator`.
- `IReadOnly` — consumidores não podem mutar. Imutável de fato (criado uma vez no boot).
- **Indexado por `DebtType` (enum)**, lookup O(1).
- `DebtsCalculator` usa `rules.TryGetValue(debt.Type, out var rule)` em vez de `rules[debt.Type]` — lança `UnknownDebtTypeException` (de domínio), não `KeyNotFoundException` (BCL).

**Defesa de banca:** "isso é Strategy + Registry. Não preciso de `if/switch` por tipo no calculator porque o dicionário já resolve. Adicionar uma rule nova é uma linha de DI; zero alteração no calculator."

---

## DTOs da fronteira HTTP

### `ConsultRequest` — `sealed class` com `required init`

Arquivo: `src/Dok.Api/Dtos/ConsultRequest.cs`.

```csharp
public sealed class ConsultRequest
{
    [JsonPropertyName("placa")]
    public required Plate Placa { get; init; }
}
```

- **`class`, não `record`** — DTO de input com 1 campo, e queremos `required` + `init` explícitos pro System.Text.Json.
- `required` — compilador força inicialização (`{ Placa = ... }`). Se o JSON omitir `"placa"`, deserialização falha.
- `init` — set apenas no inicializador. Após construído, imutável.
- **`Placa` é `Plate`, não `string`** — VO direto no DTO. JsonConverter chama `Plate.Parse` durante deserialização. Validação acontece na borda, não em Validator separado.

### `DebtsResponseDto` — `record` (em `Dok.Api/Dtos/DebtsResponseDto.cs`)

DTO de resposta. Shape literal da spec (`placa`, `debitos[]`, `resumo`, `pagamentos[]`). `Mapping.cs` traduz `DebtsResult` (Application) para `DebtsResponseDto` (Api). Separação clara: domain types nunca vazam direto no JSON.

### `ErrorPayload` / `UnknownDebtTypeErrorPayload` — `record`

Em `Dok.Api/Dtos/ErrorPayload.cs`.

- `ErrorPayload(string Error)` — cobre 400/413/503.
- `UnknownDebtTypeErrorPayload(string Error, string Type)` — cobre 422 (carrega o tipo desconhecido pra response).
- **Records porque são puros data carriers.** Sem comportamento, equality irrelevante (ninguém compara `ErrorPayload`s), mas a sintaxe positional é a mais concisa.

---

## Provider response shapes (privados)

Dentro de `ProviderAJsonAdapter.cs`:

```csharp
private sealed record ProviderAResponse(
    [property: JsonPropertyName("vehicle")] string? Vehicle,
    [property: JsonPropertyName("debts")] List<ProviderAItem>? Debts);

private sealed record ProviderAItem(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("due_date")] string DueDate);
```

- **Records aninhados privados** — escopo do adapter. Nenhum outro lugar do código sabe que `ProviderAItem` existe.
- `[property: JsonPropertyName(...)]` no positional record — atributo aplicado à property gerada (não ao parâmetro do ctor).
- **Tipos primitivos no DTO de wire** (`string Type`, `string DueDate`) — aceita qualquer string e o adapter normaliza pra domínio (`DebtTypeMapper.Parse`, `DateOnly.Parse`). Erros viram `UnknownDebtTypeException` ou `FormatException`.
- **Por que adapters separados em vez de DTO compartilhado:** Provider B fala XML com schema próprio. Cada adapter cuida do seu formato; domínio canônico (`Debt`) é o ponto de convergência.

---

## Por que tantas variações? Resumo

| Tipo | Quando | Por quê |
|---|---|---|
| `readonly record struct` | VO pequeno, imutável, hot path | Stack, equality, sem GC |
| `sealed record` | Data carrier imutável, heap | Equality estrutural + composição em listas |
| `sealed class` (DTO `required init`) | DTO de input que precisa de `[JsonPropertyName]`, `required` | Controle fino sobre deserialização |
| `sealed class` mutável | State holder per-request | Identidade > equality, mutação controlada |
| `enum` | Conjunto fechado de valores | Type-safe, switch exaustivo |
| `static class` mapper | Tradução pura entre representações | Sem estado, sem injeção |
| `abstract class` (exceção base) | Hierarquia projetada pra extensão | Bloch: design for inheritance OR seal |

A regra simples: **VO ↔ struct**, **dado de fluxo ↔ record**, **identidade per-request ↔ class**, **conjunto fechado ↔ enum**.
