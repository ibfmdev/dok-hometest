# Dependency Injection no Dok — lifetimes e por que cada um

> Doc de estudo. Inventário das registrações de DI, mapeadas por lifetime, com a justificativa de cada escolha. Foco em "**por que Singleton aqui e Scoped ali**".

## Os três lifetimes — recap rápido

| Lifetime | Uma instância por… | Quando usar | Risco |
|---|---|---|---|
| **Singleton** | aplicação inteira (do boot ao shutdown) | serviços stateless, config, factories | thread-safety: múltiplas requests acessam ao mesmo tempo |
| **Scoped** | request HTTP (em ASP.NET) | serviços com estado per-request, ou que dependem de algo Scoped | captive dependency se Singleton depender de Scoped |
| **Transient** | a cada `GetService<T>()` | objetos baratos, sem estado, descartáveis | overhead se for caro de criar; nunca segurar referência |

**Regra de ouro:** lifetime do consumidor ≥ lifetime das dependências. Singleton **não pode** segurar `Scoped` (captive dependency — vaza estado entre requests).

---

## Mapa completo das registrações

### `Dok.Application/DependencyInjection.cs::AddDokApplication`

```csharp
services.AddSingleton<IInterestRule, IpvaInterestRule>();
services.AddSingleton<IInterestRule, MultaInterestRule>();
services.AddSingleton<IReadOnlyDictionary<DebtType, IInterestRule>>(sp =>
    sp.GetServices<IInterestRule>().ToDictionary(r => r.Type));

services.TryAddSingleton<IDebtsClock, SystemDebtsClock>();

services.AddScoped<IDebtsCalculator, DebtsCalculator>();
services.AddSingleton<IPaymentSimulator, PaymentSimulator>();
services.AddScoped<IDebtsService, DebtsService>();
```

### `Dok.Infrastructure/DependencyInjection.cs::AddDokInfrastructure`

```csharp
services.AddOptions<ProvidersOptions>()...    // IOptions<T> — lifetime gerenciado pela infra
services.AddOptions<ResilienceOptions>()...

services.AddHttpClient<ProviderAJsonAdapter>().AddStandardResilienceHandler(...);
services.AddHttpClient<ProviderBXmlAdapter>().AddStandardResilienceHandler(...);

services.AddTransient<IDebtProvider>(sp => sp.GetRequiredService<ProviderAJsonAdapter>());
services.AddTransient<IDebtProvider>(sp => sp.GetRequiredService<ProviderBXmlAdapter>());

services.AddScoped<ProviderUsage>();
services.AddSingleton<ProviderMetrics>();
services.AddScoped<IDebtProviderChain, DebtProviderChain>();
```

### `Dok.Api/Program.cs` (via extensions)

```csharp
services.AddSingleton(TimeProvider.System);   // TimeProviderExtensions
```

---

## Tabela: registração → lifetime → razão

| Tipo | Lifetime | Por que esse |
|---|---|---|
| `IInterestRule` (Ipva, Multa) | **Singleton** | Stateless, imutável (constantes). Cria-se uma vez no boot. |
| `IReadOnlyDictionary<DebtType, IInterestRule>` | **Singleton** | Só agrega rules singleton; o dicionário em si é imutável. |
| `IDebtsClock` (Default `SystemDebtsClock`) | **Singleton** | Sem estado; só lê do `TimeProvider`. |
| `TimeProvider` (= `TimeProvider.System`) | **Singleton** | Singleton da BCL. Thread-safe. |
| `IPaymentSimulator` | **Singleton** | Stateless puro — só faz cálculos sobre os argumentos. |
| `IDebtsCalculator` | **Scoped** | Dependências (logger Scoped, possivelmente cancelable). Defensivo. |
| `IDebtsService` | **Scoped** | Depende de `IDebtsCalculator` (Scoped). |
| `ProviderUsage` | **Scoped** | **Estado per-request** (qual provider respondeu). Não pode ser Singleton — vazaria entre requests. |
| `ProviderMetrics` | **Singleton** | Wrapping do `Meter` da BCL — único para a aplicação. Counters thread-safe. |
| `IDebtProviderChain` | **Scoped** | Depende de `ProviderUsage` (Scoped). |
| `IDebtProvider` (registrações de chain) | **Transient** | Delega via `GetRequiredService` aos typed clients (que têm lifetime gerenciado pelo `IHttpClientFactory`). |
| `ProviderAJsonAdapter` / `ProviderBXmlAdapter` (typed clients) | **Transient** (na prática gerenciado por `IHttpClientFactory`) | `AddHttpClient<T>` registra como Transient mas com pooling de `HttpMessageHandler`. |

---

## Análise por categoria

### 1. Singleton — sem estado, criado uma vez

#### `IInterestRule` (rules de juros)

```csharp
services.AddSingleton<IInterestRule, IpvaInterestRule>();
services.AddSingleton<IInterestRule, MultaInterestRule>();
```

- Olha o código: `IpvaInterestRule` tem só constantes (`DailyInterestRate`, `InterestCapRatio`). Zero estado mutável.
- **Por que múltiplas registrações da mesma interface:** `GetServices<IInterestRule>()` (plural) coleta todas. É como o dicionário é construído.
- Singleton é seguro porque a classe **não tem estado per-call** — `Apply(debt, today)` usa só argumentos.

#### `IReadOnlyDictionary<DebtType, IInterestRule>`

```csharp
services.AddSingleton<IReadOnlyDictionary<DebtType, IInterestRule>>(sp =>
    sp.GetServices<IInterestRule>().ToDictionary(r => r.Type));
```

- **Factory delegate** (`sp => ...`) — DI passa o `IServiceProvider` e você constrói o objeto.
- Avaliado uma vez (Singleton). O `.ToDictionary` roda no boot, não a cada request.
- Imutável (`IReadOnlyDictionary`), seguro pra concorrência.

#### `IDebtsClock` — `TryAddSingleton`

```csharp
services.TryAddSingleton<IDebtsClock, SystemDebtsClock>();
```

- **`TryAdd`** em vez de `Add`: só registra **se ainda não existe**. Padrão idiomático pra "default que pode ser sobrescrito".
- Sobrescrita: `SetDebtsReferenceDate(DateOnly)` faz `RemoveAll<IDebtsClock>()` + `AddSingleton(new FixedDebtsClock(date))`. Usado pra demo da spec.
- `SystemDebtsClock` recebe `TimeProvider` no ctor. **Captura no boot, segura referência** — seguro porque `TimeProvider.System` também é singleton.

#### `IPaymentSimulator`

- Singleton. Stateless — `Simulate(debts)` é função pura sobre os argumentos.

#### `ProviderMetrics`

```csharp
services.AddSingleton<ProviderMetrics>();
```

- Wrap de `Meter` da BCL. **Counters são thread-safe por design** (`Counter<long>.Add()` é atomic).
- Singleton porque deve agregar métricas de toda a aplicação (não resetar a cada request).
- `IDisposable` — disposed no shutdown da aplicação (mantém `_meter` vivo enquanto serve).

---

### 2. Scoped — uma instância por request HTTP

#### `IDebtsCalculator` e `IDebtsService`

- Scoped por **defensão**: dependem de `ILogger<T>` (que ASP.NET registra como Singleton, mas trata internamente bem) e de outros Scoped (`IDebtProviderChain`).
- **Princípio:** se uma dependência é Scoped, o consumidor **deve** ser Scoped (ou mais curto). Singleton → Scoped seria captive.

#### `IDebtProviderChain`

- Depende de `ProviderUsage` (Scoped) e `IEnumerable<IDebtProvider>` (Transient).
- Logger Scoped também.
- Scoped permite captura limpa de `usage` per-request — não vaza identidade do provider entre requests.

#### `ProviderUsage` — **o caso mais didático**

```csharp
public sealed class ProviderUsage
{
    public string? Name { get; private set; }
    public void Mark(string name) => Name = name;
}

services.AddScoped<ProviderUsage>();
```

- **Mutável.** Carrega "qual provider respondeu nesta request".
- **Tem que ser Scoped:**
  - Singleton: vazaria entre requests (request 1 marca A, request 2 lê A mesmo se foi B).
  - Transient: `DebtProviderChain` recebe uma instância, middleware `UseProviderHeader` recebe outra — não veem a mesma referência.
  - Scoped: ambos recebem **a mesma instância dentro da request**. Chain escreve, middleware lê. Funciona.

**Defesa de banca:** "Esse é o exemplo canônico de quando Scoped paga aluguel: estado mutável compartilhado dentro de uma request, isolado entre requests."

---

### 3. Transient — descartável, novo a cada resolução

#### `IDebtProvider` (registros de chain)

```csharp
services.AddTransient<IDebtProvider>(sp => sp.GetRequiredService<ProviderAJsonAdapter>());
services.AddTransient<IDebtProvider>(sp => sp.GetRequiredService<ProviderBXmlAdapter>());
```

- **Não criam objeto novo de fato** — fazem `GetRequiredService` do typed client.
- O lifetime efetivo é o do typed client (`AddHttpClient<T>` por baixo gerencia).
- **Por que duas registrações:** `IEnumerable<IDebtProvider>` na chain coleta as duas em ordem. **Ordem de registração = ordem da chain (A → B).**

---

### 4. `AddHttpClient<T>` — caso especial

```csharp
services.AddHttpClient<ProviderAJsonAdapter>()
    .ConfigureHttpClient(...)
    .AddStandardResilienceHandler(...);
```

**O que isso faz na prática:**
1. Registra `ProviderAJsonAdapter` como **Transient**.
2. Cria `HttpClient` com `BaseAddress` configurada via `IOptions<ProvidersOptions>`.
3. Anexa **pipeline Polly** (timeout/retry/circuit breaker) ao `HttpClient`.
4. **`HttpMessageHandler`** (a parte cara) é gerenciado por `IHttpClientFactory` — pooled e reciclado a cada 2 minutos por padrão.

**Por que isso importa:**
- `HttpClient` em si é leve — pode ser Transient.
- O handler subjacente (sockets, DNS, TLS) é **caro** e fica vivo no pool.
- O pipeline Polly fica colado no handler — **circuit breaker preserva estado entre requests** mesmo com adapter Transient.

**Consequência prática:** ProviderA fica com circuito aberto em request 1, Provider A em request 2 vê circuito ainda aberto e falha rápido sem chamar HTTP. Esse é o mecanismo de fallback rápido.

---

### 5. Options — `IOptions<T>`

```csharp
services.AddOptions<ProvidersOptions>()
    .Bind(config.GetSection(ProvidersOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

- Lifetime: `IOptions<T>` é Singleton (snapshot de boot), `IOptionsSnapshot<T>` é Scoped, `IOptionsMonitor<T>` é Singleton com mudanças observáveis.
- Aqui usamos `IOptions<T>` — config é boot-time. Mudanças no `appsettings.json` exigem restart. CLAUDE.md confirma: "Editing Providers, Resilience, or RequestLimits.MaxBodyBytes requires docker compose restart api".
- `ValidateDataAnnotations` — checa `[Required]`, `[Url]`, etc.
- `ValidateOnStart` — falha no boot, não na primeira request.

---

## Padrões de DI que aparecem

### `TryAddSingleton` — default sobrescritível

Já discutido em `IDebtsClock`. Idioma pra "registra esse default, mas se alguém já registrou, não mexa".

### `RemoveAll<T>` + `AddSingleton<T>(instance)` — sobrescrita explícita

```csharp
services.RemoveAll<IDebtsClock>();
services.AddSingleton<IDebtsClock>(new FixedDebtsClock(referenceDate));
```

Usado em `SetDebtsReferenceDate` pra trocar o clock default por um fixo (demo da spec). Padrão "tira o que tem, coloca o que eu quero".

### Multiple registration coletado via `IEnumerable<T>` ou `GetServices<T>()`

```csharp
services.AddSingleton<IInterestRule, IpvaInterestRule>();
services.AddSingleton<IInterestRule, MultaInterestRule>();
// ...
sp.GetServices<IInterestRule>()  // coleta todas
```

Equivalente: `IEnumerable<IInterestRule>` injetado no construtor. Aparece em `DebtProviderChain` (`IEnumerable<IDebtProvider>`).

### Factory delegate

```csharp
services.AddSingleton<IReadOnlyDictionary<DebtType, IInterestRule>>(sp =>
    sp.GetServices<IInterestRule>().ToDictionary(r => r.Type));

services.AddTransient<IDebtProvider>(sp => sp.GetRequiredService<ProviderAJsonAdapter>());
```

Usado quando a construção precisa do `IServiceProvider` ou de lógica não-trivial.

---

## Erros comuns (e como o projeto evita)

| Anti-pattern | Como o Dok evita |
|---|---|
| **Captive dependency** (Singleton segurando Scoped) | `DebtsCalculator` é Scoped porque depende de Scoped. `ProviderUsage` deliberadamente Scoped, não Singleton. |
| **Service locator** (`sp.GetService` espalhado) | Dependências sempre via construtor. `GetRequiredService` só em factory delegates do DI. |
| **`new HttpClient()` direto** | Sempre via `AddHttpClient<T>` — pool, resilience, config. |
| **`DateTime.UtcNow` direto** | Sempre via `TimeProvider` ou `IDebtsClock`. Testável. |
| **State estático em singleton** | Constantes `private const` ok. Estado mutável estático: zero ocorrências. |
| **Múltiplas declarações sem ordem clara** | Comentário explícito: "Ordem importa: A antes de B para o fallback". |

---

## O caso especial dos dois clocks

CLAUDE.md ADR-012:

> Time has two clocks, on purpose:
> - **`TimeProvider`** (BCL) drives Polly's circuit breaker and is global; always `TimeProvider.System` in the running app.
> - **`IDebtsClock`** drives interest math. Default `SystemDebtsClock` reads `TimeProvider`, but `SetDebtsReferenceDate(DateOnly)` swaps in `FixedDebtsClock`.

**Por que separados:**
- Demo do fallback ao vivo precisa **frear o tempo do cálculo** (pra reproduzir os 1800.00/555.93/2355.93 da spec) **sem** frear o tempo do Polly (que mediria circuit breaker errado).
- Soluções monolíticas (frear `TimeProvider` global) quebravam o circuit breaker.

**Como o DI espelha isso:**
- `TimeProvider` é Singleton, sempre `TimeProvider.System`.
- `IDebtsClock` default é `SystemDebtsClock` (que lê o `TimeProvider`).
- Quando `Domain:ReferenceDate` está setado, `SetDebtsReferenceDate` substitui só `IDebtsClock` por `FixedDebtsClock` — `TimeProvider` continua System.

**Defesa de banca:** "É a única decoupling não óbvia do projeto. ADR-012 documenta o porquê. Sem isso, não conseguia rodar a demo do fallback com data fixa pros números baterem com a spec."

---

## Resumo: como decidir lifetime no Dok

```
Tem estado mutável que precisa ser per-request? → Scoped
Stateless e thread-safe? → Singleton
Ponte pra typed HttpClient? → Transient
Depende de algo Scoped? → Scoped (nunca Singleton)
Default que pode ser sobrescrito? → TryAddSingleton
```

E sempre: **lifetime do consumidor ≥ lifetime das dependências.**
