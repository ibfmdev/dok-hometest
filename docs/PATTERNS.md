# Patterns do Dok — quais usamos e por quê

> Doc de estudo. Inventário dos design patterns e idiomas C# que aparecem no código, com o problema que cada um resolve e por que foi escolhido em vez da alternativa.

## Índice

1. [Arquiteturais](#arquiteturais)
   - [Ports & Adapters (Hexagonal pragmático)](#ports--adapters-hexagonal-pragmático)
   - [Facade](#facade)
   - [Chain of Responsibility (light)](#chain-of-responsibility-light)
2. [Criacionais](#criacionais)
   - [Smart Constructor](#smart-constructor)
   - [Dependency Injection (constructor injection)](#dependency-injection-constructor-injection)
   - [Options pattern](#options-pattern)
3. [Estruturais](#estruturais)
   - [Adapter](#adapter)
   - [Decorator (via Polly resilience pipeline)](#decorator-via-polly-resilience-pipeline)
4. [Comportamentais](#comportamentais)
   - [Strategy (+ Registry)](#strategy--registry)
   - [Exception Handler chain](#exception-handler-chain)
5. [Idiomas C# / .NET que viraram pattern](#idiomas-c--net-que-viraram-pattern)
   - [Primary constructor](#primary-constructor)
   - [Records pra value semantics](#records-pra-value-semantics)
   - [`sealed` por default](#sealed-por-default)
   - [`required` + `init`](#required--init)
   - [Custom `JsonConverter<T>`](#custom-jsonconvertert)
   - [`IDestructuringPolicy<T>` (Serilog)](#idestructuringpolicyt-serilog)
   - [Extension blocks (C# 14)](#extension-blocks-c-14)
   - [Exception filter (`when` clause)](#exception-filter-when-clause)

---

## Arquiteturais

### Ports & Adapters (Hexagonal pragmático)

**Problema:** isolar lógica de negócio (cálculo de juros, agrupamento de pagamentos) de IO (HTTP, JSON, XML, logging).

**Solução no Dok:**
- **Inside** = `Dok.Domain` (zero `PackageReference`, zero `ProjectReference`). Plate, Money, Debt, Rules, Exceções.
- **Ports (interfaces)** = `IDebtProviderChain`, `IDebtsCalculator`, `IDebtsService`, `IPaymentSimulator`, `IDebtsClock`, `IDebtProvider`, `IInterestRule`. Vivem no domínio ou em Application.
- **Adapters** = `ProviderAJsonAdapter` (HTTP+JSON), `ProviderBXmlAdapter` (HTTP+XML), `SystemDebtsClock` (relógio do SO), `PlateJsonConverter` (System.Text.Json), `PlateDestructuringPolicy` (Serilog), `DebtsController` (ASP.NET).

**Dependency direction:** `Api → Application → Domain` e `Api → Infrastructure → Domain`. Infrastructure **não** depende de Application. Compilador enforça.

**Por que pragmático:** ADR-004 explicita que peguei Hexagonal puro e dispensei rituais que não pagariam aluguel — não tem UseCase classes formais (`DebtsService` é fachada de método único), não tem MediatR, não tem Repository (sem persistência), não tem Presenter formal (DTOs viram JSON direto).

**Defesa de banca:** "O `Dok.Domain.csproj` com zero referências externas é a prova viva do hexágono. Não é ilustração — é o compilador enforcando o invariante."

---

### Facade

**Problema:** Controller não pode chamar `DebtsCalculator` e depois `PaymentSimulator` direto — viola SoC e espalha orquestração na Api.

**Solução:** `DebtsService` (`src/Dok.Application/DebtsService.cs`) — uma classe, um método (`GetAsync`). Recebe `Plate`, retorna `DebtsResult`.

```csharp
public sealed class DebtsService(
    IDebtsCalculator calculator,
    IPaymentSimulator simulator) : IDebtsService
{
    public async Task<DebtsResult> GetAsync(Plate plate, CancellationToken ct)
    {
        var calc = await calculator.CalculateAsync(plate, ct);
        var options = simulator.Simulate(calc.Debts);
        return new DebtsResult(plate, calc.Debts, calc.Summary, options);
    }
}
```

**Por que Facade e não UseCase class:** UseCase formal (Clean Arch) viria com `Input`, `Output`, `Handler`, `Validator`, `Execute()` — 5 classes pra 1 operação. Facade é o mesmo concept (orquestração delegando a colaboradores) com a sintaxe mínima do C#: 1 classe, 1 método.

---

### Chain of Responsibility (light)

**Problema:** se Provider A falha, tenta Provider B. Se ambos falham, 503.

**Solução:** `DebtProviderChain` (`src/Dok.Infrastructure/Providers/DebtProviderChain.cs`) recebe `IEnumerable<IDebtProvider>` (DI dá em ordem de registro: A → B) e itera.

```csharp
foreach (var provider in providers)
{
    try { return await provider.FetchAsync(plate, ct); }
    catch (UnknownDebtTypeException) { throw; }                 // domínio: não silencia
    catch (Exception ex) when (IsProviderFailure(ex)) { … }    // falha: tenta próximo
}
throw new AllProvidersUnavailableException(failures);
```

**Por que "light":** o pattern clássico tem cada handler com referência ao próximo (`next`). Aqui é uma lista e um loop — equivalente lógico, sem estado por handler. **Ordem é determinada pela ordem de registro no DI**:

```csharp
services.AddTransient<IDebtProvider>(sp => sp.GetRequiredService<ProviderAJsonAdapter>());
services.AddTransient<IDebtProvider>(sp => sp.GetRequiredService<ProviderBXmlAdapter>());
```

**Decisões importantes:**
- `UnknownDebtTypeException` **rethrow imediato** — não cai pra fallback. CLAUDE.md: "silenciar cobraria a menos que o usuário deve".
- `OperationCanceledException` quando `ct.IsCancellationRequested` — não é falha de provider, é cliente desconectou. Propaga.
- Filtro `IsProviderFailure(ex)` — define o que é "falha esperada de provider" (HttpRequestException, JsonException, XmlException, TimeoutRejectedException, BrokenCircuitException, TaskCanceledException). Bugs (`NullReferenceException`, etc.) **não** entram no fallback — propagam pra visibilidade no monitoring.

---

## Criacionais

### Smart Constructor

**Problema:** estado inválido representável. `string placa = "abc"` compila mas não é uma placa.

**Solução:** ctor privado + factory estático que valida.

Exemplos:
- `Plate.Parse(string?)` — regex Mercosul/antiga. Lança `InvalidPlateException`.
- `Money.Of(decimal)` — aplica HALF_UP. Único caminho de criação.
- `DebtTypeMapper.Parse(string?)` — switch fechado, lança `UnknownDebtTypeException`.

**Defesa:** Se você tem `Plate` em mãos, **o tipo já é prova de validade**. Validators externos (FluentValidation) seriam reimplementar a regra em outro lugar — duplicação com nome de "boa prática".

---

### Dependency Injection (constructor injection)

**Problema:** acoplar implementações concretas dificulta teste e troca.

**Solução:** todas as classes recebem dependências via construtor. Nenhum `new HttpClient()`, nenhum `DateTime.UtcNow`, nenhum logger estático.

**Padrão consistente:** primary constructor (C# 12).

```csharp
public sealed class DebtsCalculator(
    IDebtProviderChain providers,
    IReadOnlyDictionary<DebtType, IInterestRule> rules,
    IDebtsClock clock,
    ILogger<DebtsCalculator> logger) : IDebtsCalculator
```

Detalhes em `docs/DEPENDENCY-INJECTION.md`.

---

### Options pattern

**Problema:** ler config tipada do `appsettings.json`, com validação no boot.

**Solução:** classes `*Options` com `[Required]`/`[Url]` + `AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`.

Exemplos:
- `ProvidersOptions { ProviderAUrl, ProviderBUrl }` — URLs dos providers, validadas como URL.
- `ResilienceOptions` — timeouts, retries, circuit breaker.

```csharp
services.AddOptions<ProvidersOptions>()
    .Bind(config.GetSection(ProvidersOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

**`ValidateOnStart()`** é a parte importante: misconfig falha **no boot**, não na primeira request. CLAUDE.md: "IOptions<T> everywhere uses ValidateOnStart so misconfig fails fast at boot, not at first request."

---

## Estruturais

### Adapter

**Problema:** Provider A fala JSON com schema X; Provider B fala XML com schema Y. Domínio quer `Debt` canônico.

**Solução:** dois adapters dedicados (`ProviderAJsonAdapter`, `ProviderBXmlAdapter`), cada um responsável por:
1. Chamar HTTP.
2. Validar Content-Type (defensivo — gateway que devolve HTML de erro com 200 não escapa).
3. Deserializar (records `private` específicos do adapter).
4. Mapear pra `Debt` canônico via `DebtTypeMapper.Parse`, `Money.Of`, `DateOnly.Parse`.

```csharp
public sealed class ProviderAJsonAdapter(HttpClient http, ILogger<...> logger) : IDebtProvider
{
    public string Name => "ProviderA";
    public async Task<IReadOnlyList<Debt>> FetchAsync(Plate plate, CancellationToken ct) { … }
}
```

**O que torna isso pattern Adapter clássico:** o tipo de saída (`IReadOnlyList<Debt>`) é fixado pelo port `IDebtProvider`. Cada adapter "traduz" seu formato pra esse tipo único. Domínio nunca sabe que existem JSON e XML.

---

### Decorator (via Polly resilience pipeline)

**Problema:** cada call HTTP precisa de timeout, retry com jitter, circuit breaker.

**Solução:** `Microsoft.Extensions.Http.Resilience` (Polly) registra um pipeline em volta do `HttpClient`:

```csharp
services.AddHttpClient<ProviderAJsonAdapter>()
    .ConfigureHttpClient(...)
    .AddStandardResilienceHandler(o => ApplyResilience(o, resilience));
```

**Decorator porque:** o `HttpClient` que o adapter recebe **não é o cru** — é wrapping de `DelegatingHandler` que aplica timeout → retry → circuit breaker → per-attempt timeout, em ordem. Adapter chama `http.GetAsync(...)`; o pipeline intercepta, sem o adapter saber.

**Pipeline isolado por client:** cada provider tem seu próprio circuit breaker. Se Provider A está degradado e abre o circuito, Provider B continua com sua bateria limpa — **isolamento é o que torna o fallback inter-provider rápido**.

---

## Comportamentais

### Strategy (+ Registry)

**Problema:** regra de juros varia por tipo de débito. Adicionar tipo novo não pode mexer no Calculator.

**Solução:**
- Interface `IInterestRule` (`Type`, `Apply(Debt, DateOnly) → UpdatedDebt`).
- Implementações concretas: `IpvaInterestRule`, `MultaInterestRule` (e qualquer outra que se adicione).
- **Registry**: `IReadOnlyDictionary<DebtType, IInterestRule>` construído no DI a partir de `GetServices<IInterestRule>()`.

**Por que Strategy + Registry e não `switch` no Calculator:**
- Adicionar tipo novo é **registro de DI** + nova classe. Calculator não muda.
- Cada rule é testável isolada (`IpvaInterestRuleTests`).
- Acoplamento por tipo é em runtime via dicionário, não em compile-time via switch.

**Defesa de banca:** "Open/Closed em ação — Calculator está fechado pra modificação, aberto pra extensão. Adicionar uma rule é zero linha alterada no consumidor."

---

### Exception Handler chain

**Problema:** mapear `InvalidPlateException` → 400, `UnknownDebtTypeException` → 422, `AllProvidersUnavailableException` → 503, sem espalhar `try/catch` em controllers/services.

**Solução:** ASP.NET Core 8+ tem `IExceptionHandler` (substitui middleware de exception clássico). Registra três em ordem:

```csharp
services.AddExceptionHandler<HttpRequestErrorsHandler>();   // borda HTTP: 400, 413
services.AddExceptionHandler<DomainExceptionHandler>();     // domínio: 400, 422, 503
services.AddExceptionHandler<UnhandledExceptionHandler>();  // fallback 500
```

Cada handler retorna `bool` indicando se "tratou". Se não, próximo handler tenta. Análogo a Chain of Responsibility, mas oficial do ASP.NET.

**Detalhe:** `Configure<ApiBehaviorOptions>` substitui o 400 default do `[ApiController]` (ProblemDetails) pelo `{"error":"invalid_request"}` literal da spec. Fica em `ErrorHandlingExtensions.cs`.

---

## Idiomas C# / .NET que viraram pattern

### Primary constructor

C# 12. Reduz boilerplate de "campo + atribuição no ctor".

```csharp
// Antes:
public sealed class DebtsService : IDebtsService
{
    private readonly IDebtsCalculator _calc;
    private readonly IPaymentSimulator _sim;
    public DebtsService(IDebtsCalculator calc, IPaymentSimulator sim) { _calc = calc; _sim = sim; }
}

// Agora:
public sealed class DebtsService(IDebtsCalculator calc, IPaymentSimulator sim) : IDebtsService { … }
```

Os parâmetros viram campos privados implícitos. **Convenção:** nomes em camelCase (não `_calc`).

---

### Records pra value semantics

Records dão equality estrutural automática + `with` expressions + ToString legível, sem escrever boilerplate.

**Quando usar `record class`:** data carrier imutável que vai pra heap (passa por listas, lambdas LINQ).

**Quando usar `record struct`:** VO pequeno, imutável, hot-path. Usa `readonly record struct` pra garantir.

**Quando usar `class` clássica:** estado mutável OU quando precisa de comportamento custom no construtor pra DTOs (ex: `ConsultRequest` com `required` + `[JsonPropertyName]`).

---

### `sealed` por default

**Regra do Bloch:** "design for inheritance OR prohibit it". Em C# default é "herdável", então `sealed` é opt-in.

Onde aparece: praticamente toda classe concreta do projeto. Rules (`IpvaInterestRule`), services (`DebtsCalculator`), DTOs (`ConsultRequest`), exceções folha (`InvalidPlateException`), adapters, handlers.

Onde **não** aparece (intencional): `DomainException` é abstract — projetada pra ser herdada.

**Bônus:** JIT pode devirtualizar chamadas. Pequeno ganho de perf grátis.

---

### `required` + `init`

```csharp
public required Plate Placa { get; init; }
```

- `required` — compilador força o caller a atribuir (`new ConsultRequest { Placa = ... }`).
- `init` — só pode ser setado no inicializador. Após construído, imutável.
- Combinado: imutabilidade + validação de presença no compile-time.

Usado em DTOs de input onde queremos garantir que campo veio preenchido sem usar `Nullable` (que silenciaria ausência).

---

### Custom `JsonConverter<T>`

**Problema:** `Plate` e `Money` precisam de regras de serialização específicas (validação na leitura, formatação na escrita).

**Solução:** `PlateJsonConverter`, `MoneyJsonConverter`. Registrados globalmente em `JsonExtensions.AddDokJson()`.

```csharp
public sealed class PlateJsonConverter : JsonConverter<Plate>
{
    public override Plate Read(...) => Plate.Parse(reader.GetString());
    public override void Write(...) => writer.WriteStringValue(value.Value);
}
```

**Pattern reuse:** o converter delega ao smart constructor (`Plate.Parse`). Não duplica regex.

---

### `IDestructuringPolicy<T>` (Serilog)

**Problema:** template de log `"plate {Plate}"` não pode vazar a placa raw — LGPD.

**Solução:** `PlateDestructuringPolicy` (`src/Dok.Api/Logging/PlateDestructuringPolicy.cs`) interceptava qualquer `Plate` em log e devolve `plate.Masked()` ("ABC****") em vez do valor.

```csharp
if (value is Plate plate)
{
    result = new ScalarValue(plate.Masked());
    return true;
}
```

**Uso no template:** sempre `{@Plate}` (com `@` destructure operator), nunca `{Plate}` direto. O Serilog passa pelo policy, mascara, registra.

---

### Extension blocks (C# 14)

Sintaxe nova pra agrupar extension methods sem repetir `this IServiceCollection services`.

```csharp
internal static class ErrorHandlingExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDokErrorHandling() { … }
    }
}
```

Equivalente a:

```csharp
public static IServiceCollection AddDokErrorHandling(this IServiceCollection services) { … }
```

Mas escala melhor quando o arquivo tem 5 extensions sobre o mesmo tipo.

**Onde aparece:** `JsonExtensions`, `ErrorHandlingExtensions`, `KestrelExtensions`, `OpenApiExtensions`, `SerilogExtensions`, `TimeProviderExtensions`, `ObservabilityExtensions`, `ProviderHeaderExtensions`.

---

### Exception filter (`when` clause)

```csharp
catch (Exception ex) when (ex is not DomainException)
catch (OperationCanceledException) when (ct.IsCancellationRequested)
catch (Exception ex) when (IsProviderFailure(ex))
```

**Detalhe técnico crucial:** o filter `when` é avaliado **sem fazer unwind do stack**. Se a expressão é falsa, a exception continua subindo como se o `catch` não existisse — **stack trace original preservado**. Comparado com `catch (Exception ex) { if (...) throw; ... }`, que faz unwind e reseta.

**Casos do projeto:**
- "Capture tudo exceto domínio" — separa erros conhecidos (mapeados em handler) de bugs (logados + rethrown).
- "Cancelamento do cliente" — distingue cancel real de timeout-de-Polly.
- "Falha esperada de provider" — captura exceções HTTP/JSON/XML/Polly especificamente, deixa o resto subir.

---

## Resumo: o que escolhi NÃO usar (e por quê)

| Pattern | Por que rejeitei |
|---|---|
| **MediatR / CQRS** | 1 endpoint, 1 caso de uso. Bus não justifica. |
| **Repository** | Sem banco. Providers já são a abstração de fonte de dados. |
| **AutoMapper** | Mapping é trivial e explícito. AutoMapper esconde bugs de mapping. |
| **UseCase classes (Clean)** | Cerimônia sem ganho — `DebtsService.GetAsync` cumpre o papel. |
| **Domain Events** | Nada acontece de forma assíncrona ou reativa. |
| **Aggregates ricos no DDD** | Domínio é uma calculadora — entidades com identidade não fazem sentido. |
| **FluentValidation** | VOs com smart constructor já validam na borda. Validator separado é duplicação. |
| **Specification Pattern** | Não há queries com critérios compostos. |
| **Unit of Work** | Sem transações de persistência. |

A regra geral: **YAGNI por default. Cada pattern presente paga aluguel demonstrável.**
