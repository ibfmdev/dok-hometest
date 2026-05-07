# Plano de Implementação — Serviço de Débitos Veiculares (Dok)

> Plano vivo. Atualizar à medida que estágios forem concluídos.

## Contexto

Projeto **greenfield** para o desafio HomeTest (backend engineer sênior nível 3).

- **Spec autoritativa**: [`HomeTest-2.pdf`](HomeTest-2.pdf) — substitui a v1.
- **Arquitetura**: 19 ADRs aceitos em [`architecture/`](architecture/) — cobrem stack, padrões, libs, estrutura e prompt-as-code (ADR-019: skills da apresentação).
- **Stack**: .NET 10 LTS, ASP.NET Core Controllers, Hexagonal pragmático, multi-projeto.
- **Prazo**: spec dá até 2026-05-04 (7 dias). Meta interna: **entregar em até 2 dias** — não há razão para esticar.
- **Apresentação**: ao vivo, com modificações via IA na banca — código precisa ser navegável e os boundaries precisam ser respeitados pelo compilador.

A fase de decisão arquitetural está **encerrada para o produto** (ADR-001 a ADR-018). ADR-019 trata de **artefatos de apresentação** (skills do Claude Code para o item 9), não do produto em si. Esta é a fase de execução: transformar os ADRs em código entregável, cobrindo também os 7 GAPs identificados (regras numéricas e DTOs específicos da spec que não foram cobertos pelos ADRs porque são detalhes de implementação, não decisão arquitetural) e as skills da apresentação.

## Estrutura final esperada (ADR-007)

```
Dok.sln
├── src/
│   ├── Dok.Api/                  → ref Application, Infrastructure
│   ├── Dok.Application/          → ref Domain
│   ├── Dok.Domain/               → ref nenhuma (núcleo puro)
│   ├── Dok.Infrastructure/       → ref Domain
│   └── Dok.FakeProviders/        → projeto auxiliar (ADR-008)
├── tests/
│   ├── Dok.Domain.Tests/         → xUnit + Shouldly + FsCheck
│   ├── Dok.Application.Tests/    → xUnit + Shouldly + NSubstitute
│   └── Dok.Integration.Tests/    → WebApplicationFactory + WireMock.Net
├── docker-compose.yml
├── Makefile
├── README.md
└── docs/
    ├── HomeTest.pdf, HomeTest-2.pdf
    ├── architecture/ (18 ADRs)
    └── PLANO-IMPLEMENTACAO.md (este arquivo)
```

## Estágios de implementação

### Estágio 1 — Skeleton da solution (ADR-007)

`dotnet new sln`, criação dos 4 projetos `src/` e 3 `tests/` + `Dok.FakeProviders`, ajuste de `<TargetFramework>net10.0</TargetFramework>`, `Nullable=enable`, `ImplicitUsings=enable`, e configuração de **referências entre projetos** seguindo a Dependency Rule. `Directory.Build.props` na raiz para defaults compartilhados.

**Arquivos críticos**: `Dok.sln`, `Directory.Build.props`, cada `*.csproj`.

**Validação do estágio**: `dotnet build` passa sem erros; `Dok.Domain.csproj` tem **zero** `PackageReference` e `ProjectReference` (evidência visual de domínio puro).

### Estágio 2 — Domain (ADR-004, ADR-006, ADR-012)

Núcleo puro, sem IO.

- **VOs**: `Plate` (regex Mercosul/antigo, `.Masked()` para LGPD), `Money` (HALF_UP, `ToJsonString()`).
- **Enum**: `DebtType` + `DebtTypeMapper.Parse(string)` (lança `UnknownDebtTypeException`).
- **Records**: `Debt`, `UpdatedDebt`.
- **Strategy**: `IInterestRule` + `IpvaInterestRule` (taxa 0,33%/dia, teto 20% sobre o juros) + `MultaInterestRule` (taxa 1%/dia, sem teto). **Ambas** com guarda `dias_atraso ≤ 0 → juros = 0` (cobre **GAP 3**).
- **Exceções**: `DomainException` (abstract) + `InvalidPlateException`, `UnknownDebtTypeException` (carrega `Type`), `AllProvidersUnavailableException` (carrega `Failures`).

**Cobre GAPs 1 e 3.**

**Arquivos críticos**: `src/Dok.Domain/{Plate,Money,Debt,DebtType,DebtTypeMapper,UpdatedDebt}.cs`, `src/Dok.Domain/Rules/*.cs`, `src/Dok.Domain/Exceptions/*.cs`.

### Estágio 3 — Application (ADR-005, ADR-012)

- **`IDebtProviderChain`** (port) em `Dok.Application` ou `Dok.Domain` (decidir; default sugerido: `Dok.Application`).
- **`DebtsCalculator`**: recebe `TimeProvider` + `IDebtProviderChain` + `IReadOnlyDictionary<DebtType, IInterestRule>`. Resolve `today: DateOnly` do `TimeProvider`, busca débitos, aplica rules, calcula totais. Retorna lista canônica + resumo.
- **`PaymentSimulator`** (puro): recebe `IReadOnlyList<UpdatedDebt>`, agrupa por `DebtType`, gera lista `PaymentOption` com:
  - `TOTAL` (soma de todos os débitos)
  - Uma `SOMENTE_<TIPO>` **singular** por tipo presente — mesmo se houver múltiplos débitos do mesmo tipo (cobre **GAP 5**).
  - Para cada opção: `pix.total_com_desconto = base × 0.95` (HALF_UP); cartão 1x/6x/12x via Price/PMT a `i = 0.025` (cobre **GAP 2**).
- **`DebtsService`** (fachada, ADR-005): orquestra `DebtsCalculator` + `PaymentSimulator`, monta DTO de resposta.
- **DTOs de saída**: `DebtsResponse`, `DebtResponse` (com `tipo`, `valor_original`, `valor_atualizado`, `vencimento`, `dias_atraso`), `SummaryResponse`, `PaymentsResponse`, `PaymentOptionResponse`, `PixResponse`, `CreditCardResponse`, `InstallmentResponse`. Money serializado via `Money.ToJsonString()` ou `JsonConverter` configurado globalmente. Cobre **GAP 4**.

**Cobre GAPs 2, 4, 5.**

**Arquivos críticos**: `src/Dok.Application/{DebtsService,DebtsCalculator,PaymentSimulator}.cs`, `src/Dok.Application/Abstractions/IDebtProviderChain.cs`, `src/Dok.Application/Dtos/*.cs`.

### Estágio 4 — Infrastructure (ADR-008, ADR-009)

- **`ProviderAJsonAdapter`** e **`ProviderBXmlAdapter`** implementam `IDebtProvider`. Usam `HttpClient` injetado por `IHttpClientFactory` com nomes `"providerA"` e `"providerB"`. Deserializam JSON/XML para `Debt` canônico via `DebtTypeMapper.Parse`. Adapter B trata `<debts/>` autofechado como lista vazia.
- **`DebtProviderChain`**: itera providers ordenados (A → B); coleta exceções; se todos falharem, lança `AllProvidersUnavailableException(failures)`.
- **Resilience (ADR-009)**: `AddHttpClient("providerA").AddResilienceHandler(...)` configurando pipeline canônica (Total Timeout 10s → Retry 2× backoff exponencial+jitter base 200ms → Circuit Breaker 5/30s → Per-Attempt Timeout 3s). **Pipeline isolada por client** (cada provider com seu CB).
- **`SystemClock` não é necessário** — usamos `TimeProvider.System` direto via DI.

**Arquivos críticos**: `src/Dok.Infrastructure/Providers/*.cs`, `src/Dok.Infrastructure/DependencyInjection.cs`.

### Estágio 5 — API (ADR-003, ADR-010, ADR-011, ADR-014, ADR-015, ADR-016, ADR-017)

- **`DebtsController`** (`[ApiController]`, `Route("api/v1/debitos")`) com 1 método POST.
- **`ConsultRequest { required Plate Placa }`** + `PlateJsonConverter` (chama `Plate.Parse`).
- **`MoneyJsonConverter`** (serializa como string decimal HALF_UP).
- **`IExceptionHandler`s**: `DomainExceptionHandler` (mapeia 400/422/503 com payload literal da spec, ADR-014) + `HttpRequestErrorsHandler` (mapeia 400/413 da borda HTTP, ADR-015) + `UnhandledExceptionHandler` (500 fallback).
- **Body limit**: `Kestrel.MaxRequestBodySize = 1 MiB`. **JSON unmapped**: `JsonUnmappedMemberHandling.Disallow` (ADR-015).
- **Logging Serilog**: `IDestructuringPolicy<Plate>` para mascaramento LGPD; enricher de TraceId; console pretty em Dev, JSON em Production (ADR-010).
- **OpenAPI nativo**: `AddOpenApi()` + `MapOpenApi("/openapi/v1.json")`. **Scalar UI**: `AddScalarApiReference()` em `/scalar/v1`. `AddSchemaTransformer` para `Plate` e `Money` com `pattern` e `example`. `[ProducesResponseType]` por status (200/400/413/422/503) (ADR-016).
- **`IOptions<T>`**: `ProvidersOptions`, `ResilienceOptions`, `LoggingOptions` em `appsettings.json` + `appsettings.{Dev,Prod}.json`. `ValidateDataAnnotations()` + `ValidateOnStart()` (ADR-017).

**Arquivos críticos**: `src/Dok.Api/Controllers/DebtsController.cs`, `src/Dok.Api/Json/{Plate,Money}JsonConverter.cs`, `src/Dok.Api/Errors/{Domain,HttpRequestErrors,Unhandled}ExceptionHandler.cs`, `src/Dok.Api/Logging/PlateDestructuringPolicy.cs`, `src/Dok.Api/Options/*.cs`, `src/Dok.Api/Program.cs`, `src/Dok.Api/appsettings*.json`.

### Estágio 6 — FakeProviders (ADR-008)

`Dok.FakeProviders` sobe um `WireMockServer` em porta configurável e carrega mappings de arquivos JSON/XML.

- Configuração via env vars (`Provider__Port`, `Provider__DataFile`).
- Arquivos de mapping: `data/providerA.json`, `data/providerB.xml`. Caso "sem débitos" para Provider B usa `<debts/>` autofechado (cobre **GAP 6**).
- Cenários default cobrindo a placa `ABC1234` da spec.

**Arquivos críticos**: `src/Dok.FakeProviders/Program.cs`, `src/Dok.FakeProviders/data/*`.

### Estágio 7 — Tests (ADR-013)

- **`Dok.Domain.Tests`**: cobertura completa de `Plate.Parse` (regex válido/inválido), `Money` (HALF_UP, operadores), `IpvaInterestRule` (incluindo teto 20% e dias_atraso ≤ 0), `MultaInterestRule`, `DebtTypeMapper`. **Property-based com FsCheck**: `Money m, m + (-m) == zero`; `IpvaRule.Apply nunca excede valor + 20%`.
- **`Dok.Application.Tests`**: `DebtsCalculator` com `IDebtProviderChain` mockado via NSubstitute; `PaymentSimulator` puro com builders manuais; `DebtsService` orquestração. Validar **exemplos da spec literais** (placa `ABC1234`, totais 1800,00 / 555,93 / 2355,93, parcelas 6× 427,72 e 12× 229,67 com tolerância ±0,02).
- **`Dok.Integration.Tests`**: `WebApplicationFactory<Program>` + `WireMockServer.Start(port: 0)` por teste. Cenários:
  - 200 happy path com `ABC1234`.
  - Fallback A→B (A retorna 500, B retorna 200).
  - 503 quando ambos falham.
  - 422 com tipo `LICENCIAMENTO`.
  - 400 placa inválida.
  - 400 campo desconhecido.
  - 413 body > 1 MiB.
  - Provider B retorna `<debts/>` vazio → resposta 200 com `debitos: []`.
- **`FakeTimeProvider`** fixado em `2024-05-10T00:00:00Z` em todos os testes.

**Arquivos críticos**: `tests/*/Helpers/`, `tests/*/Builders/`, `tests/Dok.Integration.Tests/Fixtures/WireMockFixture.cs`.

### Estágio 8 — Empacotamento (ADR-018)

- **Dockerfile multi-stage** para `Dok.Api` (`mcr.microsoft.com/dotnet/aspnet:10.0`) e `Dok.FakeProviders`. Build-cache friendly (csproj antes do código).
- **`docker-compose.yml`** com 3 services (`provider-a`, `provider-b`, `api`) + network interno.
- **`Makefile`** com `up`, `down`, `build`, `test`, `coverage`, `clean`.

**Arquivos críticos**: `src/Dok.Api/Dockerfile`, `src/Dok.FakeProviders/Dockerfile`, `docker-compose.yml`, `Makefile`.

### Estágio 8.5 — Skills de modificação ao vivo (ADR-019)

Artefato de **apresentação**, não de produto. Empacota os 3 cenários do item 9 do `APRESENTACAO.md` como skills versionadas em `.claude/skills/`.

- **`add-provider`**: pergunta nome (`C`, `D`, ...), URL base e formato (`JSON`/`XML`); cria `Provider<X>Adapter`, registra DI com Polly, atualiza `ProvidersOptions`, `appsettings.json`, `docker-compose.yml`, e `Dok.FakeProviders`.
- **`add-debt-type`**: pergunta nome do tipo, taxa diária e cap opcional; estende `DebtType` enum, atualiza `DebtTypeMapper`, cria `<X>InterestRule`, registra no DI da Application, gera testes em `Dok.Domain.Tests`.
- **`change-interest-rate`**: pergunta tipo (`ipva`/`multa`) e nova taxa/cap; edita a constante na rule e ajusta os testes que dependem dela.

**Workflow Git obrigatório (ADR-019 sub-decisão 5)**:
- **Pré-flight**: working tree limpo + `git fetch origin main && git checkout main && git pull --ff-only` + `git checkout -b feat/<skill>-<param>`.
- **Validação**: `dotnet build` + `dotnet test` (escopo afetado) verde antes de commit. Falha aborta sem commitar.
- **Post-flight**: `git add <arquivos explícitos>` + commit padronizado + `git push -u origin <branch>` + `gh pr create` com título e body. URL do PR é o output final.
- Skills **não tocam** em `Directory.Build.props`, `Dok.slnx`, `Dockerfile`, `Makefile`, `.github/`, `docs/architecture/`, ou `.claude/`.

**Critério de pronto**: cada skill executada com sucesso 3× consecutivas em branches descartáveis (PRs do ensaio fechados sem merge).

**Arquivos críticos**: `.claude/skills/add-provider/SKILL.md`, `.claude/skills/add-debt-type/SKILL.md`, `.claude/skills/change-interest-rate/SKILL.md`.

### Estágio 9 — README e entrega

`README.md` na raiz com:

- **Como rodar** (3 caminhos: dev local com `dotnet run`, demo com `docker compose up`, testes com `make test`).
- **Decisões técnicas** (link para `docs/architecture/README.md`; resumir os 5-7 pontos de maior impacto).
- **Trade-offs** (interpretação estrita do 422; Shouldly em vez de FA; OpenAPI nativo em vez de Swashbuckle; etc.).
- **Melhorias futuras** (RFC 7807, Seq, chiseled images, rate limiting, AwesomeAssertions verificação, etc.).
- **Divergências da spec**:
  - Interpretação **estrita** do 422 (qualquer débito desconhecido invalida tudo) com justificativa.
  - **Estratégia para divergência entre providers** (cobre **GAP 7**) — documentar a estratégia escolhida (proposto: usar dados do primeiro provider que respondeu com sucesso; alternativas listadas).
- **Demo de fallback ao vivo**: instrução explícita `docker compose stop provider-a` + refazer request.

## GAPs identificados (checklist)

Originados do cross-check spec vs ADRs. Cada GAP é resolvido em um estágio específico:

- [ ] **GAP 1** — Regras numéricas exatas em `IpvaRule` / `MultaRule` (Estágio 2).
- [ ] **GAP 2** — `PaymentSimulator` com PIX 5% por opção e Price/PMT 2,5% para 1x/6x/12x (Estágio 3).
- [ ] **GAP 3** — Lógica `dias_atraso ≤ 0 → juros = 0` nas rules (Estágio 2).
- [ ] **GAP 4** — DTOs de resposta com estrutura literal da spec, incluindo `dias_atraso` int e moeda como string (Estágio 3 e 5).
- [ ] **GAP 5** — Agrupamento `SOMENTE_<TIPO>` singular mesmo com múltiplos débitos do tipo no input (Estágio 3).
- [ ] **GAP 6** — Mapping XML do Provider B fake retornando `<debts/>` autofechado quando vazio (Estágio 6).
- [ ] **GAP 7** — Estratégia documentada para divergência entre providers (Estágio 9 — apenas README, sem código).

## Verificação end-to-end

Após cada estágio, rodar localmente:

1. `dotnet build` — sem erros, sem warnings.
2. `dotnet test` — todos os testes passam.
3. `make test && make coverage` — relatório HTML em `coverage/report/index.html`.
4. `docker compose up --build` — Api e 2 providers sobem.
5. **Caso happy path**:
   ```bash
   curl -X POST http://localhost:8080/api/v1/debitos \
        -H 'Content-Type: application/json' \
        -d '{"placa":"ABC1234"}'
   ```
   Esperado: 200 com JSON conforme spec (com `valor_atualizado: "1800.00"` etc., tolerância ±R$ 0,02).
6. **Caso 400** (placa inválida): `{"placa":"123"}` → `{"error":"invalid_plate"}`.
7. **Caso 422** (tipo desconhecido): configurar Provider A com tipo `LICENCIAMENTO` → `{"error":"unknown_debt_type","type":"LICENCIAMENTO"}`.
8. **Caso 503** (todos providers off): `docker compose stop provider-a provider-b` → `{"error":"all_providers_unavailable"}`.
9. **Caso 413** (body grande): `curl -d "$(yes a | head -c 2000000)..."` → 413.
10. **Caso campo desconhecido**: `{"placa":"ABC1234","extra":"x"}` → 400.
11. **Demo de fallback**: `docker compose stop provider-a` → refazer happy path → 200 normal + log mostrando "ProviderA failed, fell back to ProviderB".
12. **Swagger UI**: abrir `http://localhost:8080/scalar/v1` → todos os endpoints e schemas documentados; `Plate` e `Money` aparecem como string com pattern e example.

## Critérios de pronto

- [ ] Os 18 ADRs estão refletidos no código.
- [ ] Os 7 GAPs estão resolvidos.
- [ ] Saída JSON do happy path bate com a spec dentro da tolerância de ±R$ 0,02.
- [ ] Todos os erros estruturados (400/413/422/503) retornam payload literal da spec.
- [ ] Logs estruturados com placa mascarada e TraceId.
- [ ] Testes: unit + property-based + integração (cobrindo todos os edge cases da spec).
- [ ] `docker compose up` sobe tudo sem ajuste manual.
- [ ] README cobre: como rodar, decisões, trade-offs, melhorias, divergências.
- [ ] Repositório commitado e pushed para `https://github.com/ibfm/dok` na branch `main`.

## Riscos e mitigações

| Risco | Mitigação |
|---|---|
| `Scalar.AspNetCore` é lib relativamente nova | Verificar releases recentes ao adicionar; fallback para Swagger UI se issues bloqueantes |
| Bibliotecas com release pra .NET 10 ainda em catch-up | Verificar compatibilidade ao adicionar cada NuGet; fallback documentado para .NET 8 (improvável) |
| `JsonConverter<Plate>` mal configurado quebra desserialização | Testes unitários cobrem caminhos: válido, inválido, null, whitespace, lower-case |
| Cálculo de juros perdendo precisão | Política `Money.Of(...)` apenas nas fronteiras; cálculos intermediários mantêm `decimal` puro |
| Tempo até apresentação apertado | Meta interna agressiva (2 dias); priorizar happy path + fallback antes de polish |

## Referências

- ADRs: [`architecture/`](architecture/) (índice em [`architecture/README.md`](architecture/README.md))
- Spec autoritativa: [`HomeTest-2.pdf`](HomeTest-2.pdf)
