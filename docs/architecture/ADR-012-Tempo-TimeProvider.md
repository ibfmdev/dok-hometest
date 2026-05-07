# ADR-012 — Tempo: `IClock` custom vs `TimeProvider` (BCL) vs `DateTime.UtcNow`

**Status:** aceito
**Data:** 2026-04-27

## Contexto

O domínio precisa da "data de hoje" para calcular `dias_atraso` em cada débito (juros). O enunciado fixa a data de referência em **2024-05-10T00:00:00Z (UTC)** para validar os exemplos.

Se o código usar `DateTime.UtcNow` direto:

- Os testes não conseguem fixar 2024-05-10 sem usar bibliotecas exóticas (LibTime, etc.).
- Em produção, qualquer mudança de fuso/relógio do host quebra cálculos sem teste captar.
- Refactor futuro (ex: agendamento, expiração) fica acoplado ao relógio real.

A resposta canônica é abstrair "leitura do tempo". Em .NET há **três caminhos viáveis**:

1. Interface custom `IClock` (estilo "old-school" pré-.NET 8).
2. `TimeProvider` (BCL nativo a partir do .NET 8) — caminho **idiomático moderno**.
3. `DateTime.UtcNow` direto (sem abstração).

## Opções consideradas

### Opção A — `IClock` custom

```csharp
public interface IClock
{
    DateTimeOffset UtcNow { get; }
    DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class FixedClock(DateTimeOffset fixedUtc) : IClock
{
    public DateTimeOffset UtcNow => fixedUtc;
}
```

- ✅ Simples, controle total.
- ✅ Domain define o contrato (em `Dok.Domain`); Infrastructure provê a implementação (`SystemClock`).
- ❌ **Reinventa o que `TimeProvider` já oferece** desde .NET 8.
- ❌ Em entrevista sênior em 2026, *"escrevi minha própria abstração de relógio"* convida a pergunta "por que não TimeProvider?".
- ❌ Não integra automaticamente com Polly v8 / `Microsoft.Extensions.Http.Resilience` / ASP.NET timers — esses já aceitam `TimeProvider` nativamente.

### Opção B — `TimeProvider` (BCL .NET 8+) — recomendada

`TimeProvider` é uma classe abstrata no namespace `System` introduzida no .NET 8 exatamente para resolver este problema. Em .NET 10, está totalmente integrada ao ecossistema.

```csharp
// produção: TimeProvider.System (default em DI quando registrado via AddSingleton<TimeProvider>(_ => TimeProvider.System))
// componente:
public sealed class DebtsCalculator(IDebtProviderChain providers, IReadOnlyDictionary<DebtType, IInterestRule> rules, TimeProvider clock)
{
    public async Task<...> CalculateAsync(Plate plate, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        // ...
    }
}
```

```csharp
// testes: Microsoft.Extensions.TimeProvider.Testing
var clock = new FakeTimeProvider();
clock.SetUtcNow(new DateTimeOffset(2024, 5, 10, 0, 0, 0, TimeSpan.Zero));
// agora clock.GetUtcNow() retorna 2024-05-10T00:00:00Z
```

- ✅ **Idiomático em .NET 10**: tipo abstrato canônico, recomendado pela Microsoft.
- ✅ **`FakeTimeProvider` pronto** (pacote `Microsoft.Extensions.TimeProvider.Testing`) — `SetUtcNow`, `Advance(TimeSpan)`, fica trivial fixar 2024-05-10.
- ✅ **Integração nativa**: Polly v8, `Microsoft.Extensions.Http.Resilience`, ASP.NET timers e a maioria das libs modernas aceitam `TimeProvider` direto. Se um dia precisar testar timeout simulando avanço de tempo, o `FakeTimeProvider` já cobre.
- ✅ Mensagem de banca direta: *"usei o tipo abstrato do BCL para tempo, com `FakeTimeProvider` em testes — não reinventei a roda."*
- ❌ Levemente mais verboso que `IClock` simples (`clock.GetUtcNow()` vs `clock.UtcNow`); resolvido com extension method se incomodar.
- ❌ `TimeProvider` é mais geral do que precisamos (suporta timestamps, timers) — mas o "extra" não atrapalha.

### Opção C — `DateTime.UtcNow` direto

```csharp
var today = DateOnly.FromDateTime(DateTime.UtcNow);
```

- ✅ Zero abstração.
- ❌ **Testes não conseguem fixar a data** sem hacks. Os exemplos do enunciado (`121 dias atraso`, `85 dias atraso`) viram dependentes da data real.
- ❌ Acoplamento do domínio a `DateTime.UtcNow` é justamente o anti-pattern que `TimeProvider` veio corrigir.
- ❌ Inviável para este desafio.

## Tradeoffs principais (lado a lado)

| Critério | A — IClock custom | B — TimeProvider | C — UtcNow direto |
|---|---|---|---|
| Idiomático em .NET 10 | ⚠️ | ✅ | ❌ |
| Suporte de testing pronto | manual (`FixedClock`) | `FakeTimeProvider` (oficial) | impossível |
| Integração com libs (Polly, ASP.NET) | manual | nativa | n/a |
| Domain pode ser puro e testável | ✅ | ✅ | ❌ |
| Pacote NuGet adicional | nenhum | `Microsoft.Extensions.TimeProvider.Testing` (só nos testes) | nenhum |
| Defensável em entrevista sênior | "por que não TimeProvider?" | sólida | ❌ |
| Esforço de implementação | ~15 linhas | ~5 linhas + registro DI | ~0 |

## Tradeoffs aprofundados — `IClock` custom vs `TimeProvider`

A diferença entre A e B parece superficial ("ambas abstraem o relógio"), mas há sete dimensões em que `TimeProvider` ganha de forma material. Em uma banca sênior, é provável que pelo menos uma dessas perguntas apareça.

### 1. Origem e endorsement

| | IClock custom | TimeProvider |
|---|---|---|
| Quem criou | comunidade (cada projeto reinventa o seu) | **Microsoft, no BCL do .NET 8 (nov/2023)** |
| Status | padrão informal pré-.NET 8 | **tipo abstrato canônico do BCL** |
| Recomendação Microsoft | n/a | "use isto, não escreva o seu" — explícito na documentação oficial |

> *Mensagem na banca:* "antes de .NET 8, escrever um `IClock` próprio era padrão. Em .NET 8+ a Microsoft introduziu `TimeProvider` como tipo abstrato canônico exatamente para resolver essa lacuna. Em 2026, em .NET 10, escrever um `IClock` próprio é reinventar a roda."

### 2. API: o que cada um cobre

`IClock` típico expõe 1-2 métodos: `UtcNow`, talvez `Today`. `TimeProvider` é mais rico:

| Membro | `IClock` típico | `TimeProvider` |
|---|---|---|
| Hora atual UTC | `UtcNow` | `GetUtcNow() : DateTimeOffset` |
| Hora local com timezone | manual | `GetLocalNow() : DateTimeOffset` (com `LocalTimeZone`) |
| Timestamp de alta precisão | manual | `GetTimestamp() : long` |
| Tempo decorrido entre timestamps | manual | `GetElapsedTime(long) : TimeSpan` |
| Timer controlável em testes | manual | `CreateTimer(...)` |

**Implicação para nós**: para o desafio só precisamos de `GetUtcNow()`. Os métodos extras não atrapalham, mas mostram que o `TimeProvider` foi pensado para casos sofisticados (delays controlados em testes de Polly, por exemplo).

### 3. Integração com bibliotecas — **o argumento mais forte**

Em .NET 10, **bibliotecas-chave aceitam `TimeProvider` direto**:

| Biblioteca / Tipo | Aceita `TimeProvider`? | O que isso permite |
|---|---|---|
| `Polly` v8 | ✅ via `ResiliencePipelineBuilder.UseTimeProvider(...)` | testar circuit breaker e retry **avançando o `FakeTimeProvider`**, sem `Thread.Sleep` |
| `Microsoft.Extensions.Http.Resilience` | ✅ usa `TimeProvider` internamente | timeouts da pipeline respeitam o relógio fake |
| `Task.Delay(TimeSpan, TimeProvider, CT)` | ✅ overload em .NET 8+ | delays virtuais em testes |
| `PeriodicTimer(TimeSpan, TimeProvider)` | ✅ | timers virtuais |
| ASP.NET Core internals | ✅ vários componentes | health checks, hosted services |

Com `IClock` custom, nada disso "funciona automático" — você precisa escrever bridges manualmente para cada lib. Para um desafio com Polly + Resilience, isso é **trabalho extra que não paga aluguel**.

> *Caso prático na banca:* "se o avaliador pedir 'me mostre o circuit breaker abrindo após 5 falhas em 30s', com `TimeProvider` eu apenas faço `clock.Advance(TimeSpan.FromSeconds(31))` no teste e o Polly respeita. Com `IClock` próprio, eu teria que reescrever a integração."

### 4. Testing: `FakeTimeProvider`

Pacote `Microsoft.Extensions.TimeProvider.Testing` (Microsoft oficial). Permite:

```csharp
var clock = new FakeTimeProvider(startDateTime: new DateTimeOffset(2024, 5, 10, 0, 0, 0, TimeSpan.Zero));

clock.SetUtcNow(...)             // fixa o instante
clock.Advance(TimeSpan.FromMinutes(5))   // avança e dispara timers/Tasks pendentes
clock.AutoAdvanceAmount = ...    // avança automaticamente a cada chamada (útil para testes mais soltos)
```

Comparado a um `FixedClock` manual:

| Aspecto | `FixedClock` (custom) | `FakeTimeProvider` |
|---|---|---|
| Fixar instante | mutar campo manualmente | `SetUtcNow` |
| Avançar e disparar timers pendentes | impossível sem reescrever | `Advance` faz isso |
| Compatível com `Task.Delay` virtual | não | sim |
| Compatível com Polly/Resilience | manual | nativa |
| Manutenção | nossa | Microsoft |

### 5. Tipo: classe abstrata vs interface (escolha deliberada da Microsoft)

`TimeProvider` é **classe abstrata**, não interface. Decisão deliberada:

- Permite **métodos virtuais com implementação default** (`GetLocalNow()` é implementado em termos de `GetUtcNow() + LocalTimeZone`).
- Evolui sem quebrar implementações existentes (adicionar um método novo na classe abstrata com default não quebra subclasses).
- Singleton estático `TimeProvider.System` representa o relógio real.

Pequeno custo: bibliotecas de mocking baseadas em interface (NSubstitute, Moq) precisam de cuidado — mas você usa `FakeTimeProvider` ao invés de mockar, então isso não importa na prática.

### 6. Performance

Ambos abstraem operações triviais. `TimeProvider.System.GetUtcNow()` resolve para `DateTimeOffset.UtcNow` com chamada virtual — overhead irrelevante. Em hot paths (que não é nosso caso), a diferença é estatisticamente zero.

### 7. Custos honestos do `TimeProvider`

Para ser justo, `TimeProvider` tem desvantagens reais:

| Custo | Severidade | Mitigação |
|---|---|---|
| API levemente mais verbosa: `clock.GetUtcNow()` vs `clock.UtcNow` | cosmético | extension method `Today(this TimeProvider)` se incomodar |
| Tipo abstrato, não interface | nicho | irrelevante porque usamos `FakeTimeProvider`, não mocks |
| Cobre mais do que precisamos (timers, timezone) | mínimo | ignorar o que não usamos |
| Pacote extra para testes (`Microsoft.Extensions.TimeProvider.Testing`) | nenhum | escopo dos testes; não vai pra produção |

Nenhum desses chega perto de neutralizar os ganhos das dimensões 1-4.

## Resumo defensável de banca

> *"Em .NET 8 a Microsoft introduziu `TimeProvider` no BCL como tipo abstrato canônico para abstrair o relógio. Em .NET 10 (a versão deste projeto), `TimeProvider` é o caminho oficial e integra nativamente com Polly v8, `Microsoft.Extensions.Http.Resilience`, `Task.Delay`, `PeriodicTimer` e ASP.NET. Escrever um `IClock` próprio em 2026 seria reinventar a roda, perder a integração com o ecossistema, e exigir bridges manuais para cada lib. `FakeTimeProvider` é o pacote oficial de testing — eu fixo `2024-05-10T00:00:00Z` para os testes, e se precisar testar circuit breaker do Polly avançando o tempo, basta `clock.Advance(...)`. Domain segue puro: as `IInterestRule` recebem `DateOnly today` por parâmetro; só o `DebtsCalculator` toca o relógio."*

## Onde injetar e como propagar `today`

Dois estilos possíveis dentro da Opção B:

### B.1 — `TimeProvider` injetado no `DebtsCalculator`, que extrai `today` e passa às rules

```csharp
public interface IInterestRule
{
    UpdatedDebt Apply(Debt debt, DateOnly today);
}
```

- ✅ Rules permanecem **puras** (sem dep de tempo). Mais fáceis de testar — passa qualquer `DateOnly`.
- ✅ Calculator é o único ponto que toca o relógio — reduz acoplamento.

### B.2 — `TimeProvider` injetado em cada rule

- ❌ Cada rule passa a depender do relógio.
- ❌ Testes de rule precisam configurar `FakeTimeProvider` em vez de só passar uma data.

**Recomendado: B.1.**

## Recomendação

**Opção B (TimeProvider)** com:

- `TimeProvider.System` registrado como singleton no DI da `Dok.Api` (`builder.Services.AddSingleton(TimeProvider.System)`).
- `DebtsCalculator` recebe `TimeProvider` no construtor; resolve `today` como `DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)`.
- Rules (`IInterestRule.Apply(Debt debt, DateOnly today)`) **não** dependem de tempo — recebem `today` como parâmetro. Mantém o domínio puro e maximiza testabilidade.
- Pacote `Microsoft.Extensions.TimeProvider.Testing` adicionado **apenas em `Dok.Application.Tests` e `Dok.Domain.Tests`** (não na produção).

## Decisão

**Opção B — `TimeProvider` (BCL .NET 8+)** com a estrutura B.1 (relógio só no `DebtsCalculator`, rules puras recebendo `DateOnly today` por parâmetro).

Concretamente:

- `TimeProvider.System` registrado como singleton no DI da `Dok.Api`:
  ```csharp
  builder.Services.AddSingleton(TimeProvider.System);
  ```
- `DebtsCalculator` recebe `TimeProvider` no construtor; resolve `today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)`.
- Cada `IInterestRule.Apply(Debt debt, DateOnly today)` recebe a data por parâmetro. Rules **não** dependem do relógio — permanecem puras e testáveis com qualquer `DateOnly`.
- Pacote `Microsoft.Extensions.TimeProvider.Testing` adicionado **apenas** em `Dok.Application.Tests` e `Dok.Domain.Tests` (zero impacto na produção).
- Em testes, `FakeTimeProvider` é configurado para `2024-05-10T00:00:00Z` para reproduzir os exemplos do enunciado.

## Justificativa

1. **Endorsement Microsoft**: `TimeProvider` é o tipo abstrato canônico do BCL desde .NET 8, criado pela própria Microsoft para resolver este problema. Em .NET 10, escrever um `IClock` próprio é reinventar a roda — argumento difícil de defender em entrevista sênior.
2. **Integração com o ecossistema**: Polly v8, `Microsoft.Extensions.Http.Resilience`, `Task.Delay`, `PeriodicTimer` e vários componentes do ASP.NET aceitam `TimeProvider` direto. Significa que avançar o relógio em testes (`FakeTimeProvider.Advance(...)`) afeta também as políticas de resiliência — testes de circuit breaker e timeout deixam de precisar de `Thread.Sleep`.
3. **Testing oficial**: `FakeTimeProvider` (pacote `Microsoft.Extensions.TimeProvider.Testing`, mantido pela Microsoft) cobre `SetUtcNow`, `Advance`, `AutoAdvanceAmount` — tudo o que precisamos para fixar `2024-05-10` e simular passagem de tempo.
4. **Domínio puro preservado**: rules continuam recebendo `DateOnly` por parâmetro. Apenas o `DebtsCalculator` toca o relógio — o que mantém o domínio testável sem nenhum mock de tempo.
5. **Custo desprezível**: ~5 linhas de configuração + 1 pacote NuGet em testes. Nenhum custo de produção.
6. **Mensagem na banca, em uma frase**: *"`TimeProvider` é o relógio canônico do .NET; `FakeTimeProvider` me dá controle total em testes; rules puras recebem a data por parâmetro — domínio livre de IO de tempo."*

---
