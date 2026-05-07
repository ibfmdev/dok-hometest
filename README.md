# Dok — Serviço de Débitos Veiculares

API HTTP em **.NET 10** que consulta múltiplos provedores externos de débitos veiculares (IPVA, multa), normaliza, calcula juros simples e simula formas de pagamento (PIX e cartão de crédito).

Implementação do desafio HomeTest (Backend Engineer Sênior). Spec autoritativa em [`docs/HomeTest-2.pdf`](docs/HomeTest-2.pdf). Decisões arquiteturais documentadas em [`docs/architecture/`](docs/architecture/) — 20 ADRs aceitos.

## Como rodar

### Caminho 1 — Docker Compose (recomendado para demo)

```bash
docker compose up --build
```

Sobe 3 containers:
- `provider-a` → `http://localhost:9001` (WireMock fake JSON)
- `provider-b` → `http://localhost:9002` (WireMock fake XML)
- `api`        → `http://localhost:8080`

Smoke test:

```bash
curl -X POST http://localhost:8080/api/v1/debitos \
     -H 'Content-Type: application/json' \
     -d '{"placa":"ABC1234"}'
```

OpenAPI/Scalar UI: `http://localhost:8080/scalar` · spec JSON em `http://localhost:8080/openapi/v1.json`.

### Caminho 2 — Dev local sem Docker

Em três terminais:

```bash
# Terminal 1 — Provider A (JSON)
Provider__Port=9001 Provider__DataFile=data/providerA.json \
Provider__ContentType=application/json \
dotnet run --project src/Dok.FakeProviders

# Terminal 2 — Provider B (XML)
Provider__Port=9002 Provider__DataFile=data/providerB.xml \
Provider__ContentType=application/xml \
dotnet run --project src/Dok.FakeProviders

# Terminal 3 — API
dotnet run --project src/Dok.Api
```

### Caminho 3 — Testes

```bash
dotnet test         # 57 testes (Domain 39 + Application 7 + Integration 11)
make test           # equivalente
```

## Demo de fallback (ao vivo)

Com tudo rodando via `docker compose up`:

```bash
# 1. Request normal — Provider A responde
curl -X POST http://localhost:8080/api/v1/debitos \
     -H 'Content-Type: application/json' -d '{"placa":"ABC1234"}'

# 2. Derruba o Provider A
docker compose stop provider-a

# 3. Refaz a request — agora cai pro Provider B (você vê nos logs do api)
curl -X POST http://localhost:8080/api/v1/debitos \
     -H 'Content-Type: application/json' -d '{"placa":"ABC1234"}'
```

Os logs do container `api` mostram a sequência: `Querying ProviderA`, `ProviderA failed`, `Querying ProviderB`, `ProviderB returned 2 debts`.

## Configuração

Tudo configurável vive em `src/Dok.Api/appsettings.json` (com overrides em `appsettings.Development.json` e `appsettings.Production.json`). **Mudar config não exige rebuild** — basta reiniciar o processo.

### Seções principais

```json
{
  "Providers": {
    "ProviderAUrl": "http://localhost:9001",
    "ProviderBUrl": "http://localhost:9002"
  },
  "Resilience": {
    "TotalTimeoutSeconds": 10,
    "PerAttemptTimeoutSeconds": 1,
    "RetryCount": 2,
    "RetryBaseDelayMs": 200,
    "CircuitBreakerFailures": 2,
    "CircuitBreakerWindowSeconds": 30,
    "CircuitBreakerBreakDurationSeconds": 30
  },
  "RequestLimits": {
    "MaxBodyBytes": 1048576
  }
}
```

### Três formas de sobrescrever (em ordem de precedência crescente)

1. **Editar `appsettings.json`** — vale para o ambiente padrão.
2. **Editar `appsettings.{Env}.json`** — vale só para o ambiente correspondente (`Development`, `Production`, ...). Sobrescreve o `appsettings.json`.
3. **Variável de ambiente** — sobrescreve qualquer JSON. Use `__` (dois underscores) para representar nesting:
   ```bash
   RequestLimits__MaxBodyBytes=524288 dotnet run --project src/Dok.Api
   ```
   Ou no `docker-compose.yml`:
   ```yaml
   api:
     environment:
       Resilience__RetryCount: "3"
       RequestLimits__MaxBodyBytes: "2097152"
   ```

### Reiniciando após mudar config

| Ambiente | Comando |
|---|---|
| Dev local (`dotnet run` em foreground) | `Ctrl+C` no terminal e rodar `dotnet run` de novo |
| Docker Compose (recomendado) | `docker compose restart api` (ou `restart provider-a`/`provider-b` se mudou config dos providers) |
| Reiniciar tudo (limpando volumes) | `docker compose down -v && docker compose up --build` |

### Quais settings precisam restart

A maioria dos settings é lida **uma única vez no startup** — alterações exigem reiniciar:

| Seção | Hot reload? | Comportamento |
|---|---|---|
| `Logging` (níveis Serilog) | ✅ sim | Recarrega automaticamente ao salvar |
| `Providers` (URLs) | ❌ não | `HttpClient` é construído uma vez |
| `Resilience` (Polly) | ❌ não | Pipeline é construída uma vez |
| `RequestLimits.MaxBodyBytes` | ❌ não | Kestrel decide o limite no startup |

> **Resumindo**: editar config sempre funciona sem rebuild. **Reload em runtime** só vale pra logging — para tudo mais, restart é necessário. Isso é limitação do framework (Kestrel/HttpClient/Polly), não do design da aplicação.

## Endpoints

| Método | Rota | Descrição |
|---|---|---|
| `POST` | `/api/v1/debitos` | Consulta + simulação. Body: `{"placa":"ABC1234"}` |
| `GET`  | `/openapi/v1.json` | Spec OpenAPI 3 |
| `GET`  | `/scalar`          | UI interativa |

### Códigos de erro

| Status | Causa | Payload |
|---|---|---|
| `200` | sucesso | corpo completo (débitos + resumo + opções de pagamento) |
| `400` | placa fora do padrão Mercosul/antigo | `{"error":"invalid_plate"}` |
| `400` | JSON inválido / campo desconhecido | `{"error":"invalid_request"}` |
| `413` | body > 1 MiB | `{"error":"payload_too_large"}` |
| `422` | tipo de débito desconhecido | `{"error":"unknown_debt_type","type":"<TIPO>"}` |
| `503` | todos os provedores falharam | `{"error":"all_providers_unavailable"}` |

## Estrutura

```
Dok.slnx
├── src/
│   ├── Dok.Api/                  ASP.NET Controllers, IExceptionHandler, Serilog, OpenAPI
│   ├── Dok.Application/          DebtsService (fachada), DebtsCalculator, PaymentSimulator
│   ├── Dok.Domain/               VOs (Plate, Money), Rules (IpvaInterestRule, MultaInterestRule), Exceptions — ZERO dependências externas
│   ├── Dok.Infrastructure/       Adapters HTTP (Provider A JSON, Provider B XML), DebtProviderChain, resilience com Polly v8
│   └── Dok.FakeProviders/        Worker que sobe WireMockServer (não faz parte da entrega de produção)
├── tests/
│   ├── Dok.Domain.Tests/         xUnit + Shouldly
│   ├── Dok.Application.Tests/    xUnit + Shouldly + NSubstitute + FakeTimeProvider
│   └── Dok.Integration.Tests/    WebApplicationFactory + WireMock.Net
├── docs/
│   ├── HomeTest.pdf, HomeTest-2.pdf   spec do desafio
│   ├── architecture/                  20 ADRs (decisões e tradeoffs)
│   ├── APRESENTACAO.md                roteiro da banca
│   └── PLANO-IMPLEMENTACAO.md         plano de execução
├── .claude/
│   └── skills/                        skills de modificação ao vivo para o item 9 (ADR-019)
├── docker-compose.yml
├── Makefile
└── README.md
```

## Decisões técnicas em destaque

> Detalhe completo em [`docs/architecture/README.md`](docs/architecture/README.md).

- **.NET 10 LTS** + ASP.NET Core Controllers (ADR-001/002/003).
- **Hexagonal pragmático** com 4 projetos (`Domain` puro, sem `PackageReference` nem `ProjectReference`) — boundaries enforçadas pelo compilador (ADR-004/007).
- **Value Objects** para `Plate` (regex Mercosul/antigo + `.Masked()` LGPD) e `Money` (HALF_UP, JSON-string) — SSOT de validação e formatação (ADR-006).
- **Resiliência via `Microsoft.Extensions.Http.Resilience`** (Polly v8 internamente): timeout total 10s, retry 2× com jitter, circuit breaker **2/30s** isolado por provider, per-attempt timeout **1s** (ADR-009 — valores revisados após ensaio empírico do fallback ao vivo).
- **Fallback inter-provider**: `DebtProviderChain` itera Provider A → B; circuit breaker isolado garante fallback sem latência quando A está degradado.
- **Logging Serilog** com `IDestructuringPolicy<Plate>` que **mascara automaticamente** placas em todos os logs (LGPD), mais TraceId via W3C TraceContext (ADR-010).
- **`TimeProvider`** do BCL .NET 8+ (não `IClock` custom) com `FakeTimeProvider` em testes para fixar `2024-05-10T00:00:00Z` (ADR-012).
- **Testes**: xUnit + Shouldly + NSubstitute + builders manuais. WireMock real nos integration tests para exercitar HTTP/Polly de verdade (ADR-013).
- **Tratamento de erros via `IExceptionHandler`** (.NET 8+) em chain: HTTP errors handler → Domain handler → Unhandled. Payload literal da spec (ADR-014).
- **Limites de borda HTTP**: body 1 MiB, `JsonUnmappedMemberHandling.Disallow` (ADR-015).
- **OpenAPI** nativo (.NET 9+ `Microsoft.AspNetCore.OpenApi`) + UI **Scalar** moderna; VOs declarados como string com regex/example (ADR-016).
- **`IOptions<T>` tipado com `ValidateOnStart`** — config inválida não deixa o app subir (ADR-017).
- **Skills de modificação ao vivo (Claude Code)** versionadas em `.claude/skills/` — `/add-provider`, `/add-debt-type`, `/change-interest-rate` automatizam os 3 cenários do item 9 da apresentação com guardrails de branch isolado e validação build+test (ADR-019).

## Trade-offs

- **Shouldly em vez de FluentAssertions**: FA virou comercial em jul/2024 (v8). Optei por Shouldly (Apache 2.0, 15+ anos) em vez de freezar em FA 7.x ou usar fork incipiente.
- **NSubstitute em vez de Moq**: Moq teve a polêmica do SponsorLink (ago/2023). NSubstitute evita o drama com sintaxe igualmente boa.
- **Spec literal vs RFC 7807 ProblemDetails**: spec exige payload `{"error":"..."}`. ProblemDetails (RFC 7807) seria padrão da indústria, mas conformidade prevalece. Registrado como melhoria futura.
- **Interpretação estrita do 422**: spec tem ambiguidade entre "qualquer tipo desconhecido lança 422" vs "só se TODOS forem desconhecidos". Escolhi estrita: silenciar tipos desconhecidos gera dano real ao usuário (ele paga menos do que deve). Detalhamento na seção [Decisões interpretativas](#decisões-interpretativas-onde-a-spec-é-ambígua) abaixo.
- **Multi-projeto (4 src + 3 tests)**: 5 minutos extras de setup vs convenção em pastas. O ganho — `Dok.Domain.csproj` com **zero refs**, prova visual de domínio puro — paga aluguel para apresentação sênior.

## Melhorias futuras

- **RFC 7807 ProblemDetails** quando o cliente puder consumir (alternativo ao payload literal da spec).
- **Imagens chiseled** (`mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`) para reduzir superfície de ataque (~30 MB final).
- **Seq local** como sink do Serilog para experiência ainda melhor de inspeção em demo.
- **Rate limiting** na API (`Microsoft.AspNetCore.RateLimiting`).
- **Health checks** (`/health/live`, `/health/ready`).
- **Property-based tests** ampliados com `FsCheck.Xunit` (HALF_UP, monotonicidade das rules).
- **OpenTelemetry** para traces e métricas (já temos a base de TraceId).

## Decisões interpretativas (onde a spec é ambígua)

1. **Interpretação do 422 (`unknown_debt_type`) — estrita**. A spec tem dois trechos sobre o 422 em seções diferentes:
   - **Casos de borda das regras de juros**: *"Tipos de débito não previstos pelas regras acima devem causar erro HTTP 422... Não silenciar, não converter para 'OUTROS'."*
   - **Tratamento de erros**: *"HTTP 422... quando todos os débitos retornados são de tipo desconhecido."*

   Adotei a leitura **estrita**: o primeiro trecho é a **regra geral** (qualquer tipo desconhecido lança 422), o segundo é um **caso particular** dela. Qualquer débito com tipo não mapeado dispara `422` com `type` do primeiro encontrado. Sem inventar campos extras (`avisos[]`, `unknown_types[]`) — a spec dita o payload literal `{"error":"unknown_debt_type","type":"<TIPO>"}`.

## Itens descritivos atendidos

A spec pede algumas descrições sem exigir implementação. Para esses, registro aqui a estratégia:

- **Divergência de dados entre provedores** (*"descreva sua estratégia, mesmo que não a implemente"*): a spec define fallback **sequencial** (*"tente o próximo na ordem configurada"*). Nesse modelo, **divergência entre provedores não é observável** em uma mesma request — apenas um provider fornece dados a cada chamada (o primeiro que respondeu com sucesso). Logo, a "estratégia de divergência" se reduz à própria arquitetura da chain: **A é tentado primeiro; se falhar, B fornece os dados** — e essa é a única visão de mundo do consumidor naquela request. Caso uma evolução futura troque o fallback sequencial por consulta paralela, novas estratégias precisariam ser definidas (e essa mudança, em si, seria uma divergência da spec).

## Itens opcionais implementados ("Seria bacana se")

A spec lista vários itens em "Seria bacana se" — opcionais, mas implementados aqui:

- **Simulação de falha de provedor** (timeout/indisponibilidade) via WireMock.Net.
- **Retry com backoff e circuit breaker** via `Microsoft.Extensions.Http.Resilience` (Polly v8 internamente). Circuit breaker isolado por provider.
- **Testes automatizados**: 57 ao todo (Domain unitário + Application com mocks + Integration com WireMock + WebApplicationFactory).
- **Logs estruturados** Serilog com **mascaramento de placa para LGPD** (`IDestructuringPolicy<Plate>` que aplica `.Masked()` automaticamente). Em casos de erro de validação (`InvalidPlateException`), o raw também não é logado — apenas o tamanho.
- **Padrões nomeados** (Strategy, Adapter, Ports & Adapters / Hexagonal pragmático) — documentados nos ADRs e no README.
- **Limite de tamanho do body** (1 MiB, configurável via `RequestLimits:MaxBodyBytes`) e **rejeição de campos desconhecidos** (`JsonUnmappedMemberHandling.Disallow`).
- **Health checks**: `GET /health/live` e `GET /health/ready` (200 OK quando o app está saudável).

## Decisões fora do escopo direto da spec

A spec não dita certos detalhes de design — registro aqui as decisões arbitrárias para transparência:

- **Endpoint dos providers (`GET /debts/{plate}`)**: a spec define o **payload** retornado pelos provedores (formato JSON em A, XML em B), mas não o endpoint HTTP. Adotei `GET /debts/{placa}` como convenção. Em integração real, o path completo é configurável via `Providers.ProviderAUrl`/`ProviderBUrl`.
- **Data de referência configurável**: a spec define a data fixa `2024-05-10T00:00:00Z` para os exemplos numéricos. Em produção (relógio real), os valores variam dia a dia. Para garantir que a demo bata exatamente com os exemplos da spec (`1800.00`, `555.93`, `2355.93`), o `docker-compose.yml` fixa a data via `Domain__ReferenceDate=2024-05-10T00:00:00Z`. Removendo essa env var, a app usa `TimeProvider.System` (relógio real).
- **Header `X-Dok-Provider`**: cada response carrega esse header indicando qual provedor serviu os dados. **Body permanece literal** conforme a spec — header é metadado HTTP, não payload.

## Referências

- [Spec do desafio (v2)](docs/HomeTest-2.pdf)
- [ADRs — índice e formato](docs/architecture/README.md)
- [Plano de implementação](docs/PLANO-IMPLEMENTACAO.md)
