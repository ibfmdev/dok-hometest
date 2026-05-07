# ADR-009 — Resiliência: Polly v8 / Microsoft.Extensions.Http.Resilience

**Status:** aceito
**Data:** 2026-04-27

## Contexto

O enunciado pede:

- Fallback caso um provedor falhe.
- Idealmente: simular timeout/indisponibilidade, retry com backoff, circuit breaker.

A decisão tem duas camadas:

1. **Lib de resiliência**: Polly v8 puro vs `Microsoft.Extensions.Http.Resilience` (que usa Polly v8 por baixo, com integração first-class ao `IHttpClientFactory`).
2. **Quais políticas, em que ordem, com que parâmetros**.

E um ponto importante: **resiliência intra-provider (Polly) é diferente de fallback inter-provider** — o fallback A → B é responsabilidade do `DebtProviderChain` (ADR-004), não do Polly. Polly cuida das tentativas e proteções **dentro** de um único provider.

## Camada 1 — Lib de resiliência

### Opção A — `Polly` v8 puro

Pacote `Polly` (≥ 8.x) com `ResiliencePipeline<T>` configurado manualmente, integrado via `AddHttpClient(...).AddPolicyHandler(...)` ou `AddResilienceHandler(...)`.

- ✅ Controle total: cada estratégia configurada explicitamente, fácil mostrar na banca a pipeline construída no código.
- ✅ Documentação rica e a própria lib é referência da indústria.
- ❌ Mais código boilerplate de configuração.

### Opção B — `Microsoft.Extensions.Http.Resilience` (recomendada)

Pacote da Microsoft que envolve Polly v8 com defaults sensatos. Configuração via `AddHttpClient(...).AddStandardResilienceHandler(opts => ...)`.

- ✅ **Microsoft-endorsed** — em apresentação sênior, *"usei o pacote oficial de resiliência da Microsoft, que internamente usa Polly v8"* é argumento forte.
- ✅ Defaults sensatos: timeout, retry com jitter, circuit breaker, rate limiter — tudo já configurado, só ajustar parâmetros.
- ✅ Integração nativa com `IHttpClientFactory`, telemetria, e `Microsoft.Extensions.Logging`.
- ✅ Em .NET 10, é o caminho idiomático.
- ❌ Um nível de abstração a mais — se quiser políticas exóticas, pode ter que descer pra Polly puro.
- ❌ Para a banca, é menos visível "o que está acontecendo" — defaults invisíveis.

## Camada 2 — Quais políticas e em que ordem

A pipeline canônica em sistemas HTTP segue (do mais externo para o mais interno):

```
[Total Timeout] → [Retry com backoff] → [Circuit Breaker] → [Per-Attempt Timeout] → [HTTP call]
```

- **Total Timeout** (outermost): garante que toda a operação termina em X segundos, mesmo com retries. Evita "morte lenta".
- **Retry**: tenta de novo em falhas transientes (5xx, timeout, connection refused). Backoff exponencial + jitter para não sobrecarregar o provedor que está se recuperando.
- **Circuit Breaker**: depois de N falhas em janela Y, "abre" e rejeita rapidamente novas chamadas por Z segundos — protege o provedor degradado e libera recursos do nosso lado.
- **Per-Attempt Timeout** (innermost): cada chamada HTTP tem seu próprio limite, separado do total.

### Parâmetros (ajustáveis em `appsettings.json`)

| Política | Valor | Justificativa |
|---|---|---|
| Total timeout | 10s | Cliente HTTP do desafio espera resposta síncrona — não pode esperar para sempre |
| Retry attempts | 2 (3 chamadas no total) | Falhas transientes geralmente passam em 1-2 retentativas; mais que isso é desperdício |
| Retry backoff | exponencial com jitter (200ms base, fator 2) | Padrão da indústria; jitter evita "thundering herd" |
| Circuit breaker | **2 falhas em 30s** → aberto por 30s | Ver "Revisão empírica" abaixo |
| Per-attempt timeout | **1s** | Ver "Revisão empírica" abaixo |

### Revisão empírica (2026-04-27, durante ensaio da apresentação)

A versão original deste ADR propunha `5 falhas em 30s` + `per-attempt 3s`, descritos como *"conservador; em produção real ajustaria por SLA do provedor"*. Ao testar o cenário ao vivo (`docker compose stop provider-a` + curl manual), descobri que **o circuit breaker nunca abria** — cada request com A morto demorava ~10s eternos.

**Causa raiz** (verificada com logs do `Polly`):

- Cada request com A indisponível → 3 attempts (1 + 2 retries) × `PerAttemptTimeout=3s` ≈ **9-10s por request**.
- Cliente sequencial faz uma request a cada ~10s.
- Janela do CB = 30s; threshold = 5 falhas (1 por request, no nível do retry consolidado).
- 5 requests sequenciais ocupam ~50s → quando a 5ª falha entra na janela, a 1ª **já saiu**.
- O CB nunca acumula 5 falhas simultâneas → nunca abre → `BrokenCircuitException` nunca é lançada → `gh pr create` digo, **fallback** sempre paga o preço dos 10s.

Confirmado em paralelo: 5 requests **paralelas** (≈3s cada, comprimidas) **disparam** o CB normalmente, e as próximas viram instantâneas. O bug é específico do uso sequencial real.

**Decisão revisada**:

- `PerAttemptTimeoutSeconds: 3 → 1`. Justificativa: 3s era folgado pra rede LAN em demo; 1s já cobre P99 de provedores HTTP saudáveis.
- `CircuitBreakerFailures: 5 → 2`. Justificativa: a spec não obriga "5 falhas" (o número original era escolha minha); 2 é o mínimo prático que abre o CB depois de 1 request "perdida" + 1 attempt da seguinte. Cobre cenário sequencial real.
- Janela e break duration mantidos em 30s — tempo suficiente pro provedor degradado se recuperar antes do half-open.

**Resultado observado** com a config nova (mesmo cenário sequencial):
- 1ª request com A morto: ~3s (3 × 1s).
- 2ª request: dispara o CB no 2º attempt → ~1-2s, vai pro B.
- 3ª+: CB aberto → ~50ms.

### Crucial: circuit breaker **por provider**

Cada `HttpClient` (Provider A, Provider B) registra seu **próprio** circuit breaker. Provider A degradado **não** deve afetar Provider B. Isso significa que o `DebtProviderChain` consegue, mesmo com A em circuito aberto, ir direto para B sem esperar timeout — exatamente o comportamento que defendemos como "fallback resiliente".

## Decisão

- **Lib**: `Microsoft.Extensions.Http.Resilience` (que usa Polly v8 internamente).
- **Pipeline canônica** (do mais externo para o mais interno): Total Timeout → Retry com backoff exponencial e jitter → Circuit Breaker → Per-Attempt Timeout → HTTP call.
- **Parâmetros** (configuráveis em `appsettings.json`):
  - Total timeout: 10s.
  - Retry: 2 tentativas, backoff base 200ms, fator 2, com jitter.
  - Circuit breaker: **2 falhas em 30s → aberto por 30s** (revisado após ensaio — ver seção "Revisão empírica").
  - Per-attempt timeout: **1s** (revisado após ensaio — ver seção "Revisão empírica").
- **Circuit breaker isolado por provider**: cada `HttpClient` nomeado (`"providerA"`, `"providerB"`) tem sua própria instância — degradação de A não afeta B.

## Justificativa

1. `Microsoft.Extensions.Http.Resilience` é o pacote oficial Microsoft para resiliência em .NET 8+, com Polly v8 por baixo. Usar o pacote endorsed em vez do Polly direto soma um argumento (oficialidade) sem perder expressividade.
2. A ordem da pipeline é canônica e protege contra todos os modos de falha: total timeout impede "morte lenta", retry trata transientes, circuit breaker protege provedor degradado e libera recursos do nosso lado, per-attempt timeout dá deadline a cada chamada individual.
3. Backoff exponencial com **jitter** evita thundering herd quando o provedor estiver se recuperando.
4. Circuit breaker por provider é o que torna o fallback do `DebtProviderChain` realmente resiliente: com A em circuito aberto, a chamada para A é rejeitada **imediatamente** (sem esperar timeout) e a chain segue para B sem latência adicional.
5. Parâmetros em `appsettings.json` permitem ajuste sem rebuild — argumento extra na banca de "operacionalmente saudável".

---
