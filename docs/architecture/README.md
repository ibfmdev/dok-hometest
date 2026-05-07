# Arquitetura — Serviço de Débitos Veiculares

Esta pasta documenta as decisões arquiteturais do projeto **Dok** (HomeTest — Backend Engineer Sênior). Cada decisão é registrada em um arquivo separado seguindo o padrão **ADR (Architecture Decision Record)**, permitindo rastreabilidade, auditoria histórica e consulta independente.

## O que é um ADR

Um **Architecture Decision Record** é um documento curto e estruturado que registra uma decisão arquitetural significativa, com:

- **Contexto** que motivou a decisão.
- **Opções consideradas** (não apenas a vencedora).
- **Tradeoffs** explícitos de cada opção.
- **Decisão** final, sem ambiguidade.
- **Justificativa** numerada e defensável.

A premissa: *uma decisão sem alternativas registradas não é uma decisão — é um palpite*. ADRs forçam a explicitação do raciocínio, tornam o histórico do projeto auditável e dão munição para defesa em revisões/apresentações.

## Formato dos ADRs neste projeto

Cada arquivo `ADR-NNN-<topico>.md` segue o template:

```markdown
# ADR-NNN — Título da decisão

**Status:** aceito | em discussão | revisado | descartado
**Data:** AAAA-MM-DD

## Contexto
Por que esta decisão precisou ser tomada. Que problema resolve.

## Opções consideradas
Lista das alternativas reais avaliadas, cada uma com prós e contras.

## Tradeoffs
Análise comparativa: o que ganha e o que perde escolhendo cada opção.
Frequentemente apresentada como tabela lado a lado.

## Decisão
A escolha final, sem ambiguidade. Pode incluir parâmetros concretos
(versões, valores, estruturas).

## Justificativa
Argumentos numerados que sustentam a decisão. Cada argumento deve ser
defensável em uma apresentação para banca técnica.
```

### Variações aceitas

ADRs que cobrem decisões compostas (ex: ADR-009 sobre resiliência, com camadas de "lib" + "políticas" + "parâmetros") podem usar **estrutura por camadas** ou **sub-decisões** dentro do mesmo arquivo. O essencial — opções, tradeoffs, decisão, justificativa — está sempre presente.

### Convenções

- **Status `aceito`**: decisão fechada e em vigor para a implementação.
- **Status `em discussão`**: decisão aberta, ainda sendo debatida.
- **Status `revisado`**: decisão antiga que foi reavaliada — o registro original é mantido com nota explicando a revisão e a data, **nunca apagado**.
- **Status `descartado`**: ADR proposto mas não adotado; mantido para preservar histórico e evitar revisitar a mesma discussão.

## Sumário das decisões

| # | Decisão | Status | Arquivo |
|---|---|---|---|
| 001 | Linguagem e plataforma: .NET / C# | aceito | [ADR-001](ADR-001-Linguagem-e-Plataforma.md) |
| 002 | Versão do .NET: 10 (LTS) | aceito | [ADR-002](ADR-002-Versao-do-Dotnet.md) |
| 003 | Estilo de API: Controllers (MVC) | aceito | [ADR-003](ADR-003-Estilo-de-API.md) |
| 004 | Estilo arquitetural: Hexagonal pragmático | aceito | [ADR-004](ADR-004-Estilo-Arquitetural.md) |
| 005 | Decomposição da Application: fachada + colaboradores | aceito | [ADR-005](ADR-005-Decomposicao-Application.md) |
| 006 | Modelagem do domínio: Value Objects | aceito | [ADR-006](ADR-006-Value-Objects.md) |
| 007 | Estrutura física: multi-projeto | aceito | [ADR-007](ADR-007-Estrutura-Multi-Projeto.md) |
| 008 | Simulação dos provedores: WireMock externo | aceito | [ADR-008](ADR-008-Simulacao-Provedores.md) |
| 009 | Resiliência: Microsoft.Extensions.Http.Resilience (Polly v8) | aceito | [ADR-009](ADR-009-Resiliencia.md) |
| 010 | Logging estruturado: Serilog + destructuring policy + TraceId | aceito | [ADR-010](ADR-010-Logging.md) |
| 011 | Validação de input: JsonConverter + VO como SSOT | aceito | [ADR-011](ADR-011-Validacao-Input.md) |
| 012 | Tempo: TimeProvider (BCL .NET 8+) | aceito | [ADR-012](ADR-012-Tempo-TimeProvider.md) |
| 013 | Estratégia de testes: xUnit + Shouldly + NSubstitute + FsCheck + WireMock | aceito | [ADR-013](ADR-013-Estrategia-Testes.md) |
| 014 | Tratamento de erros: IExceptionHandler + payload da spec | aceito | [ADR-014](ADR-014-Tratamento-de-Erros.md) |
| 015 | Limites de body (1 MiB) + rejeição de campos desconhecidos | aceito | [ADR-015](ADR-015-Limites-Request.md) |
| 016 | Documentação de API: OpenAPI nativo + Scalar | aceito | [ADR-016](ADR-016-Documentacao-API.md) |
| 017 | Configuração: IOptions tipado + ValidateOnStart | aceito | [ADR-017](ADR-017-Configuracao.md) |
| 018 | Empacotamento: Dockerfile multi-stage + docker-compose + Makefile | aceito | [ADR-018](ADR-018-Empacotamento.md) |
| 019 | Skills de modificação ao vivo (Claude Code) para a banca | aceito | [ADR-019](ADR-019-Skills-Modificacao-Ao-Vivo.md) |

## Como ler

- **Leitura linear (apresentação)**: percorrer ADR-001 a ADR-018 em ordem. Cada um leva 2-5 minutos. Total: ~1h.
- **Consulta pontual**: usar a tabela acima para ir direto ao tópico.
- **Auditoria de decisão específica**: cada ADR é autocontido — pode ser lido isoladamente sem dependência de leitura anterior.

## Visão consolidada do que foi decidido

### Stack e ambiente
- .NET 10 LTS (ADR-001, ADR-002).
- ASP.NET Core com Controllers (ADR-003).

### Arquitetura
- Hexagonal pragmático: domínio puro, ports/adapters, sem cerimônia de Clean Architecture (ADR-004).
- Application com fachada `DebtsService` orquestrando `DebtsCalculator` + `PaymentSimulator` (ADR-005).
- Modelagem com Value Objects para `Plate` e `Money`; enum para `DebtType`; `DateOnly` nativo para `DueDate` (ADR-006).
- Multi-projeto: `Dok.Api`, `Dok.Application`, `Dok.Domain`, `Dok.Infrastructure` + 3 projetos de teste (ADR-007).

### Integração e resiliência
- Provedores fake via WireMock.Net em projeto auxiliar (ADR-008).
- Resiliência com `Microsoft.Extensions.Http.Resilience` (Polly v8 internamente): timeout total 10s, retry 2x com backoff exponencial e jitter, circuit breaker 5/30s, per-attempt timeout 3s, isolado por provider (ADR-009).
- Tempo via `TimeProvider` do BCL com `FakeTimeProvider` em testes (ADR-012).

### Observabilidade
- Logs estruturados Serilog com mascaramento de placa (LGPD) via `IDestructuringPolicy`, e correlation via TraceId (ADR-010).

### Borda HTTP
- Validação de input por `JsonConverter` + `Plate.Parse` (VO como single-source-of-truth) (ADR-011).
- Tratamento de erros centralizado via `IExceptionHandler` com payload literal da spec (ADR-014).
- Limite de body 1 MiB e rejeição estrita de campos desconhecidos (ADR-015).
- Documentação via OpenAPI nativo (.NET 9+) + UI Scalar (ADR-016).

### Testes
- xUnit + Shouldly + NSubstitute + builders manuais + FsCheck para property-based + Coverlet para cobertura (ADR-013).

### Operação
- `IOptions<T>` tipado com validação no startup (ADR-017).
- Dockerfile multi-stage + docker-compose com 3 serviços + Makefile (ADR-018).

### Apresentação
- 3 skills de Claude Code (`/add-provider`, `/add-debt-type`, `/change-interest-rate`) para o item 9 da apresentação — prompt-as-code versionado, com guardrails de branch isolado e validação build+test (ADR-019).

## Convenção de nomes para novos ADRs

Ao criar um novo ADR:

1. Reservar o próximo número sequencial (`ADR-019`, `ADR-020`, ...).
2. Nome do arquivo em PascalCase com hífens: `ADR-NNN-Topico-Curto.md`.
3. Adicionar entrada na tabela do **Sumário** acima e descrição em **Visão consolidada**.
4. Iniciar com status `em discussão`; mover para `aceito` apenas após decisão.
5. Em decisões que **revisam** ADRs anteriores: adicionar nota no ADR original referenciando o novo, e iniciar o novo com seção "Histórico" descrevendo a revisão.

## Referências

- A spec original do desafio está em [`../HomeTest-2.pdf`](../HomeTest-2.pdf) (versão autoritativa; a v1 em `HomeTest.pdf` foi superada).
- O documento monolítico anterior (`docs/ARCHITECTURE.md`) foi mantido como redirecionamento para esta pasta.
