# ADR-014 — Tratamento de erros: middleware central, mapeamento HTTP 400/422/503

**Status:** aceito
**Data:** 2026-04-27

## Contexto

O ADR-004 decidiu que erros viram HTTP via **middleware central** — exceções de domínio são lançadas, e a tradução para status code mora num único ponto. Falta consolidar **como** isso é feito concretamente em .NET 10.

A spec exige formato exato de payload:

| Status | Payload | Quando |
|---|---|---|
| `400` | `{"error":"invalid_plate"}` | placa fora do padrão Mercosul/antigo |
| `422` | `{"error":"unknown_debt_type","type":"<TIPO>"}` | débito com tipo não previsto pelas regras |
| `503` | `{"error":"all_providers_unavailable"}` | todos os provedores falham |

E erros não previstos (bug, NRE, etc.): `500` genérico, sem vazar stack trace.

## Sub-decisões deste ADR

1. **Mecanismo**: Middleware customizado vs `IExceptionHandler` (.NET 8+) vs `UseExceptionHandler` com lambda vs `IExceptionFilter` (MVC).
2. **Formato do payload**: seguir spec literal (`{"error":"..."}`) vs RFC 7807 ProblemDetails vs híbrido.
3. **Hierarquia de exceções de domínio**.
4. **Interpretação ambígua da spec** sobre quando 422 é lançado.
5. **Logging dos erros**: nível, contexto, dados sensíveis.

## Sub-decisão 1 — Mecanismo

### Opção A — `IExceptionHandler` (.NET 8+) (recomendada)

API moderna do ASP.NET Core a partir do .NET 8. Múltiplos handlers em chain; cada handler decide se trata a exception ou passa adiante.

```csharp
public sealed class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, payload) = ex switch
        {
            InvalidPlateException             => (400, (object)new { error = "invalid_plate" }),
            UnknownDebtTypeException u        => (422, new { error = "unknown_debt_type", type = u.Type }),
            AllProvidersUnavailableException  => (503, new { error = "all_providers_unavailable" }),
            _                                 => (0, null!)
        };
        if (status == 0) return false; // não tratamos; passa pro próximo handler

        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(payload, ct);
        return true;
    }
}

// Program.cs
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddExceptionHandler<UnhandledExceptionHandler>(); // fallback 500
app.UseExceptionHandler();
```

- ✅ **Idiomático em .NET 10**.
- ✅ Compõe múltiplos handlers — separa "erros de domínio" de "fallback genérico 500".
- ✅ Fácil de testar isoladamente.
- ✅ Boa narrativa de banca: *"usei IExceptionHandler, a API moderna do .NET 8+, com múltiplos handlers em chain"*.
- ❌ Levemente mais cerimonioso que middleware único.

### Opção B — Middleware customizado

```csharp
public sealed class ExceptionHandlingMiddleware(RequestDelegate next) {
    public async Task InvokeAsync(HttpContext ctx) {
        try { await next(ctx); }
        catch (DomainException ex) { /* mapeia */ }
        catch (Exception ex) { /* 500 */ }
    }
}
app.UseMiddleware<ExceptionHandlingMiddleware>();
```

- ✅ Funciona em qualquer versão.
- ✅ Try/catch explícito — fácil de raciocinar.
- ❌ Em .NET 10, é "estilo antigo" — pergunta na banca: *"por que não IExceptionHandler?"*.

### Opção C — `UseExceptionHandler` com lambda

```csharp
app.UseExceptionHandler(builder => builder.Run(async ctx => { ... }));
```

- ❌ Tudo em uma lambda — vira "God lambda" rapidamente.
- ❌ Difícil de testar.

### Opção D — `IExceptionFilter` (MVC)

- ❌ Específico a MVC; não pega exceções de middleware fora do pipeline MVC. Inadequado.

## Sub-decisão 2 — Formato do payload

A spec usa formato **simples** com chave `error`:

```json
{ "error": "invalid_plate" }
{ "error": "unknown_debt_type", "type": "LICENCIAMENTO" }
{ "error": "all_providers_unavailable" }
```

ASP.NET Core tem suporte first-class a **RFC 7807 ProblemDetails** (`{ "type": "...", "title": "...", "status": 400, "detail": "..." }`), que é padrão da indústria.

### Opção A — Seguir spec literal (recomendada)

Resposta exatamente como a spec exige.

- ✅ **Conforme com o enunciado** — prioridade absoluta.
- ✅ Simples, sem ambiguidade.
- ❌ Não usa o padrão RFC 7807.

### Opção B — RFC 7807 ProblemDetails

- ✅ Padrão da indústria.
- ❌ **Diverge do enunciado**. Em apresentação, a primeira pergunta seria: *"por que você mudou o formato?"*.

### Opção C — Híbrido

Formato simples para os 3 casos da spec, ProblemDetails para 500 não previstos.

- ✅ Conforme com a spec onde ela é explícita.
- ✅ Bom default para erros não documentados.
- ⚠️ Mistura de formatos pode confundir clientes.

## Sub-decisão 3 — Hierarquia de exceções de domínio

```csharp
namespace Dok.Domain.Exceptions;

public abstract class DomainException(string message) : Exception(message);

public sealed class InvalidPlateException(string? raw)
    : DomainException($"Invalid plate format: {raw ?? "<null>"}");

public sealed class UnknownDebtTypeException(string type)
    : DomainException($"Unknown debt type: {type}")
{
    public string Type { get; } = type;
}

public sealed class AllProvidersUnavailableException(IReadOnlyList<Exception> failures)
    : DomainException($"All {failures.Count} providers failed")
{
    public IReadOnlyList<Exception> Failures { get; } = failures;
}
```

Vantagens:

- Cada exceção carrega **apenas** o que precisa: `Type` para `UnknownDebtTypeException` (vai pro payload), `Failures` para diagnóstico/log da `AllProvidersUnavailableException`.
- `DomainException` como base permite catch genérico se necessário (mas o handler usa `pattern matching` por tipo).
- Mantém-se em `Dok.Domain` — alinhado com o ADR-007 (Application e Infrastructure podem importá-las).

## Sub-decisão 4 — Interpretação ambígua da spec sobre 422

A spec da v2 do PDF tem dois trechos potencialmente conflitantes:

> "Tipos de débito não previstos pelas regras acima devem causar erro HTTP 422... Não silenciar, não converter para 'OUTROS'."

> "HTTP 422 com `{"error":"unknown_debt_type"}` quando todos os débitos retornados são de tipo desconhecido."

Interpretação possível 1 (estrita): qualquer débito com tipo desconhecido → 422 imediato (independente dos outros débitos).
Interpretação possível 2 (permissiva): só 422 se *todos* forem desconhecidos; senão, processar conhecidos e ignorar/comentar desconhecidos.

### Fluxo concreto da chamada que dispara 422

Importante separar de qual chamada estamos falando. O 422 é a resposta da **nossa API ao cliente** — não da nossa API para o provider. Sequência:

```
Cliente                Nossa API                 Provider A (fake)
  │──POST /debitos──────▶│                              │
  │ {placa:"ABC1234"}    │──GET ABC1234───────────────▶│
  │                      │◀──200 [IPVA, MULTA,         │
  │                      │       LICENCIAMENTO]        │
  │                      │                             │
  │                      │ DebtsCalculator processa:   │
  │                      │   IPVA  ✓ tem rule          │
  │                      │   MULTA ✓ tem rule          │
  │                      │   LICENCIAMENTO ❌ sem rule │
  │                      │                             │
  │                      │ throw UnknownDebtTypeException
  │◀──422 ───────────────│                             │
  │ {error:              │                             │
  │  "unknown_debt_type",│                             │
  │  type:               │                             │
  │  "LICENCIAMENTO"}    │                             │
```

### Semântica de cada status retornado pela nossa API

| Status | Significado HTTP | Quando | Origem do problema |
|---|---|---|---|
| `400` | Bad Request | placa fora do padrão Mercosul/antigo | **cliente** mandou input inválido |
| `422` | Unprocessable Content | provider trouxe tipo de débito sem regra mapeada | **lacuna do nosso domínio** — não é erro do cliente nem do provider |
| `503` | Service Unavailable | todos os provedores falham | nossa **dependência** está fora |

`422 Unprocessable Content` (RFC 9110) é exatamente *"recebi uma representação que entendo sintaticamente, mas há problema semântico que me impede de processar"*. Encaixa para tipo de débito desconhecido.

### Por que estrita vence — análise por consequência

| Cenário | Estrita (422) | Permissiva (silencia desconhecidos) |
|---|---|---|
| IPVA + MULTA + LICENCIAMENTO retornados; sem rule para LICENCIAMENTO | 422 com `type:"LICENCIAMENTO"` — cliente entende que falta regra | retorna IPVA + MULTA, **omite LICENCIAMENTO**. Cliente paga R$ 2.355, dirige, leva multa por licenciamento atrasado. **Dano real ao usuário.** |
| Provider retorna tipo novo `DPVAT` (válido em produção, mas ainda não mapeado) | 422 sinaliza que **o domínio precisa de update**, com `type:"DPVAT"` no payload — guia a evolução | desconhecido vira invisível; equipe descobre tarde, em produção |
| Provider retorna `{"type":"banana"}` (lixo) | 422 com `type:"banana"` permite alarme estruturado | silencia o lixo — bug do provider passa despercebido |

A regra **"Não silenciar, não converter para 'OUTROS'"** é explícita na spec e operacionalmente correta — silenciar gera dano real.

### Decisão sobre a ambiguidade

**Interpretação estrita**: qualquer débito com tipo desconhecido na lista normalizada → 422 imediato com `type` do primeiro tipo desconhecido encontrado (ou lista deles, decisão de payload abaixo).

A frase *"quando todos forem desconhecidos"* da spec é tratada como **um dos casos** que dispara 422 (caso particular), não como condição exclusiva.

**Documentar como divergência interpretativa no README**: mostrar que a ambiguidade foi notada, qual interpretação foi escolhida, e por quê — sinal de rigor sênior.

### Sub-decisão de payload: 1 tipo ou lista?

Spec exemplifica: `{"error":"unknown_debt_type","type":"<TIPO>"}` — singular `type`.

- Se houver múltiplos tipos desconhecidos na mesma response do provider, retornar o **primeiro** encontrado para conformidade com o singular do exemplo.
- Variante alternativa (registrada como melhoria futura): `"types": ["LICENCIAMENTO", "DPVAT"]` em array — mais informativo, mas diverge do exemplo. Não adotar agora.

## Sub-decisão 5 — Logging dos erros

| Tipo de exceção | Nível de log | Contexto incluído |
|---|---|---|
| `InvalidPlateException` | `Warning` (input do cliente) | `Plate.Masked()` (LGPD), TraceId |
| `UnknownDebtTypeException` | `Warning` (dados do provider) | `Type`, `Plate.Masked()`, TraceId |
| `AllProvidersUnavailableException` | `Error` | lista de causas internas, `Plate.Masked()`, TraceId |
| Exceção não tratada (→ 500) | `Error` | exception completa (sem ir pro response), `Plate.Masked()` se disponível, TraceId |

## Recomendação consolidada

1. **Mecanismo**: `IExceptionHandler` com 2 handlers: `DomainExceptionHandler` (mapeia exceções de domínio para 400/422/503 com payload da spec) e `UnhandledExceptionHandler` (500 genérico, sem stack trace no response, com log estruturado completo).
2. **Formato**: Opção A (spec literal). RFC 7807 fica como melhoria futura no README.
3. **Exceções**: hierarquia em `Dok.Domain.Exceptions/` com base `DomainException` e três concretas.
4. **Interpretação 422**: estrita (qualquer débito desconhecido lança).
5. **Logging**: níveis e contexto conforme tabela acima; placa sempre mascarada.

## Decisão

1. **Mecanismo**: `IExceptionHandler` (.NET 8+) com dois handlers em chain — `DomainExceptionHandler` (mapeia exceções de domínio para 400/422/503) e `UnhandledExceptionHandler` (fallback 500 sem stack trace no response).
2. **Formato do payload**: literal da spec (`{"error":"..."}`); RFC 7807 ProblemDetails fica como melhoria futura no README.
3. **Hierarquia de exceções**: base `DomainException` em `Dok.Domain.Exceptions/`, com três concretas — `InvalidPlateException`, `UnknownDebtTypeException` (carrega `Type`), `AllProvidersUnavailableException` (carrega `Failures` para diagnóstico).
4. **Interpretação do 422**: estrita — qualquer débito com tipo desconhecido na lista normalizada dispara 422 com `type` do primeiro tipo desconhecido. Documentar como divergência interpretativa no README.
5. **Logging**: tabela de níveis e contexto:
   - `InvalidPlateException` → Warning, `Plate.Masked()`, TraceId.
   - `UnknownDebtTypeException` → Warning, `Type`, `Plate.Masked()`, TraceId.
   - `AllProvidersUnavailableException` → Error, lista de causas, `Plate.Masked()`, TraceId.
   - Não tratada (→ 500) → Error, exception completa apenas no log (nunca no response), `Plate.Masked()` quando disponível, TraceId.

## Justificativa

1. **`IExceptionHandler` é a API moderna** do .NET 8+ para tratamento de exceções. Permite cadeia de handlers e isola "erros de domínio" de "fallback genérico", separando responsabilidades — defensável como padrão da indústria em 2026.
2. **Conformidade com a spec é prioridade** sobre RFC 7807. O enunciado define o payload literal; mudar formato seria divergência sem ganho.
3. **Hierarquia explícita de exceções** em `Dok.Domain` mantém a regra de domínio (o que pode dar errado e por quê) próxima da regra que define cada caso. Application e Infrastructure podem lançá-las sem importar nada da Api.
4. **Interpretação estrita do 422** é coerente com a regra explícita "Não silenciar, não converter para 'OUTROS'". Silenciar tipos desconhecidos gera dano real ao usuário (paga menos do que deve). Documentar a interpretação no README mostra rigor sênior.
5. **Logging com placa mascarada e TraceId** atende LGPD e permite correlação de retries/fallback de uma mesma request — fechando o ciclo do ADR-010.

---
