# ADR-004 — Estilo arquitetural / fluxo da aplicação

**Status:** aceito
**Data:** 2026-04-27

## Contexto

Decidido que **não** vamos usar Clean Architecture pura nem CQRS com MediatR. A pergunta é: qual é então o fluxo concreto da aplicação? O que a Controller chama? Como o domínio fica isolado da infraestrutura sem o aparato cerimonioso do Clean Arch?

A escolha aqui dita: número de classes/interfaces, onde os contratos vivem, e como cada peça se comunica com a próxima.

## Estilos arquiteturais considerados

| Estilo                                  | O que é                                                                                     | Adequação ao desafio                              |
|-----------------------------------------|---------------------------------------------------------------------------------------------|---------------------------------------------------|
| **N-tier clássico**                     | Controller → Service → Repository → DB. Domínio "anêmico", lógica nos services             | Funciona, mas não destaca o domínio rico de juros |
| **Clean Architecture pura**             | 4+ camadas concêntricas, MediatR, UseCases como classes, DTOs imutáveis, Repositories      | Cerimônia demais para 1 endpoint                  |
| **Hexagonal (Ports & Adapters)**        | Domínio no centro, "ports" são interfaces, adapters fazem IO                               | Casa com requisito de "isolar integração/domínio/pagamento" |
| **CQRS + MediatR**                      | Comandos e Queries separados, mediator faz dispatch                                         | Sem benefício aqui — só uma operação              |
| **Vertical Slices**                     | Organiza por feature, não por camada técnica                                                | Útil em apps grandes; aqui temos 1 feature        |

## Hexagonal vs Clean Architecture: comparação detalhada

Os dois estilos são parentes próximos — ambos colocam o domínio no centro e invertem dependências de IO. Por isso a confusão comum *"é a mesma coisa, né?"*. **Não é.** As diferenças importam para a escolha aqui.

### Origens e propósitos

| | Hexagonal (Ports & Adapters) | Clean Architecture |
|---|---|---|
| **Autor** | Alistair Cockburn (2005) | Robert C. Martin (2012) |
| **Foco original** | Isolar lógica de IO ("dentro vs fora"); permitir trocar HTTP por CLI por mensagem sem mudar o domínio | Sintetizar Hexagonal + Onion + Screaming Architecture; impor regra de dependência radial |
| **Metáfora** | Hexágono com portas (interfaces) e adaptadores (implementações) | Círculos concêntricos com regra de dependência apontando para dentro |

### Diferenças concretas (o que muda no código)

| Aspecto | Hexagonal | Clean Architecture |
|---|---|---|
| **Camadas** | Duas zonas: **dentro** (domínio) e **fora** (adapters). Não impõe número de camadas internas. | Quatro camadas concêntricas: Entities, Use Cases, Interface Adapters, Frameworks/Drivers. Mais formal. |
| **Use Cases** | Não exige formato. Pode ser método em service, classe, função. | Quase sempre **classes formais** com `Input` e `Output` (ports). Uma classe por operação. |
| **DTOs nas fronteiras** | Recomendado, não obrigatório. | Obrigatório nas boundaries inter-camadas. Output DTOs distintos dos Input. |
| **Presenters / Output Ports** | Opcionais. Domínio pode retornar DTO direto. | Padrão clássico inclui Presenter para formatar output sem o Use Case conhecer detalhes de UI/JSON. |
| **Repository** | Não exige. Adapter de "persistência" é só mais um adapter. | Quase sempre presente, mesmo sem BD relacional. |
| **MediatR** | Não exige. | Comum em templates .NET (Jason Taylor, etc.) — virou quase ritual. |
| **Tamanho típico** | Médio. Pouca cerimônia, pouco boilerplate. | Maior. Cada use case = 3-5 classes (Input, Output, Use Case, Validator, Handler). |
| **Curva de adoção** | Baixa. Conceito de "port" e "adapter" é intuitivo. | Maior. Quatro camadas + regra de dependência + DTOs em todo lugar = mais para internalizar. |

### Tradeoffs aplicados ao desafio

#### Onde Clean Architecture **brilharia** (e não é nosso caso)

- **Múltiplos use cases independentes** (10+, 50+): a estrutura formal de UseCase classes paga aluguel pela consistência.
- **Múltiplas interfaces de entrega** (HTTP + gRPC + CLI + worker queue): Presenters e Input/Output ports protegem o domínio de cada uma.
- **Equipe grande** (10+ devs) onde a formalidade evita atalhos.
- **Sistema com vida longa** (5+ anos) onde a cerimônia inicial se amortiza.

#### Por que Clean Architecture pura **não paga aluguel aqui**

1. **Um único caso de uso**: criar `ConsultAndSimulateUseCase` com `ConsultAndSimulateInput` e `ConsultAndSimulateOutput` para envolver o que já está em `DebtsService.GetAsync(plate)` é cerimônia visível sem ganho real.
2. **Uma única interface de entrega** (HTTP): Presenters/Output Ports formais não se justificam quando só há um formato de saída.
3. **Sem persistência**: Repository pattern, comum no Clean Arch, vira artefato sem propósito.
4. **MediatR**: o template Clean Arch popular em .NET geralmente puxa MediatR. Sem múltiplos handlers, é overhead — e adiciona indireção que **dificulta** a navegação por uma IA durante apresentação ao vivo.
5. **Banca pragmática**: defender 12 classes para responder uma request HTTP simples dá margem para a pergunta *"você não está overengineering?"*. Defender 6 classes com responsabilidades claras é mais sólido.

#### Por que Hexagonal **paga aluguel** (com pragmatismo)

1. **Foco no que importa**: o enunciado pede explicitamente isolar integração/domínio/pagamento. Hexagonal é literalmente isso: "dentro = domínio; fora = adapters".
2. **Cerimônia mínima**: domínio puro + ports (interfaces) + adapters (implementações). Sem rituais.
3. **Extensibilidade real**: adicionar provider C = novo adapter; adicionar tipo de débito = nova `IInterestRule`. Zero alteração no domínio.
4. **Testabilidade direta**: domínio testável sem mocks de framework; adapters testáveis com WireMock.
5. **Narrativa de banca clara**: o `Dok.Domain.csproj` com zero dependências externas é a prova viva do Hexagonal.

### Por que chamamos de "Hexagonal pragmático" e não Hexagonal puro

Mesmo Hexagonal "puro" tem rituais que podemos dispensar aqui:

| Ritual hexagonal puro | O que fazemos | Por quê |
|---|---|---|
| Toda interação externa atrás de um "port" formal | Sim para `IDebtProvider` e `IClock`; logger usa abstração padrão do .NET (`ILogger<T>`) | `ILogger<T>` já é um port idiomático e portável. |
| Use Cases como classes nomeadas (uma por operação) | Não. Usamos `DebtsService` como fachada com método único | Operação única não justifica classe extra. |
| Application services não retornam Domain Entities; retornam DTOs próprios | Sim, retornamos record DTOs | Mantém o contrato HTTP estável mesmo se entidades de domínio mudarem. |
| Inversão estrita: ports definidos no domínio, implementados na infra | Sim para `IClock` (em `Domain`); `IDebtProvider` ficará em `Infrastructure` por proximidade ao adapter (decisão prática a ser revisitada se incomodar) | "Hexagonal pragmático" admite esse pragmatismo. |

> **Resumo defensável na banca:**
> *"Adotei Hexagonal porque o enunciado pede isolamento e Hexagonal é a forma mais direta de demonstrá-lo. Considerei Clean Architecture, mas a cerimônia (Use Case classes, Input/Output ports, MediatR, Repository) não pagaria aluguel para um único caso de uso, uma única interface de entrega e zero persistência. Pragmaticamente: peguei o núcleo do Hexagonal — domínio puro + ports + adapters — e dispensei o ritual extra que não agrega valor neste escopo."*

## Estilo proposto: **"Camadas práticas com domínio puro e adapters"** (Hexagonal pragmático)

É um Hexagonal pragmático: pega o que importa do Hexagonal (domínio puro, dependências invertidas via interfaces para IO), sem o aparato extra do Clean Architecture (sem MediatR, sem UseCase classes, sem Repository quando não há BD).

### Camadas e responsabilidades

```
┌─────────────────────────────────────────────────────────────────────┐
│  API (HTTP)                                                         │
│  • DebtsController        ← borda HTTP, só recebe/devolve          │
│  • Middlewares            ← exception handler, logging             │
│  • Filters / Validators   ← FluentValidation no input              │
└──────────────────┬──────────────────────────────────────────────────┘
                   │ chama
                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│  Application (Orquestração)                                         │
│  • IDebtConsultationService                                         │
│      .ConsultAndSimulateAsync(plate, ct) → DebtConsultationResult   │
│  • DTOs de saída (response)                                         │
└──────────────────┬──────────────────────────────────────────────────┘
                   │ usa
        ┌──────────┴──────────┐
        ▼                     ▼
┌─────────────────┐   ┌─────────────────────────────────────────────┐
│  Domain (puro)  │   │  Ports (interfaces)                         │
│  • Plate        │   │  • IDebtProvider (1 método: FetchAsync)     │
│  • Debt         │   │  • IDebtProviderChain                       │
│  • Money        │   │  • IClock                                   │
│  • IInterest    │   └────────┬────────────────────────────────────┘
│    Rule         │            │ implementado por
│  • IpvaRule     │            ▼
│  • MultaRule    │   ┌─────────────────────────────────────────────┐
│  • Payment      │   │  Infrastructure (Adapters)                  │
│    Simulator    │   │  • ProviderAJsonAdapter (HttpClient + JSON) │
│                 │   │  • ProviderBXmlAdapter (HttpClient + XML)   │
│  Sem IO,        │   │  • DebtProviderChain (fallback A→B+Polly)   │
│  sem async,     │   │  • SystemClock / FixedClock (testes)        │
│  sem deps       │   │  • Logging (Serilog), mascaramento placa    │
└─────────────────┘   └─────────────────────────────────────────────┘
```

### Fluxo de uma request

1. **Cliente** → `POST /api/v1/debitos { "placa": "ABC1234" }`.
2. **DebtsController** recebe. Validação de placa via `[ApiController]` + FluentValidation. Se inválida → `400 invalid_plate`.
3. Controller chama `IDebtConsultationService.ConsultAndSimulateAsync(plate, ct)`.
4. **DebtConsultationService** orquestra:
   - a. Pede `IDebtProviderChain.FetchDebtsAsync(plate, ct)` → recebe lista canônica de `Debt`.
        - A chain tenta `ProviderA` primeiro; se falhar (timeout, 5xx, exceção), tenta `ProviderB`.
        - Se todos falharem → exceção `AllProvidersUnavailableException` → middleware mapeia para `503`.
   - b. Para cada `Debt`, resolve a `IInterestRule` correspondente ao tipo (via dicionário `Dictionary<DebtType, IInterestRule>` injetado).
        - Se algum tipo desconhecido → exceção `UnknownDebtTypeException` → middleware mapeia para `422`.
   - c. Aplica a regra: cada débito vira um `UpdatedDebt` (com `valor_atualizado` e `dias_atraso`).
   - d. Calcula resumo (`total_original`, `total_atualizado`).
   - e. Chama `PaymentSimulator.Simulate(updatedDebts)` → retorna a lista de opções (`TOTAL`, `SOMENTE_IPVA`, `SOMENTE_MULTA`) com `pix` e `cartao_credito`.
   - f. Monta o DTO de resposta.
5. Controller serializa em JSON (com strings decimais, conforme spec) e responde `200`.

### O que **não** entra (e por quê)

- **MediatR / CQRS**: 1 endpoint, 1 caso de uso. Bus não justifica.
- **Repository**: não há banco. Os providers já são a abstração de "fonte de dados".
- **AutoMapper**: mapping é trivial e explícito; AutoMapper esconde bugs de mapping. Mapping manual em ~10 linhas por DTO.
- **UseCase classes (uma classe por operação)**: 1 método em um Service basta. Adicionar uma classe `ConsultAndSimulateUseCase` com `Execute()` é cerimônia sem ganho aqui.
- **Domain Events**: nada acontece de forma assíncrona ou em reação a outra coisa.
- **Aggregates ricos no DDD**: o domínio aqui é uma calculadora. Entidades com identidade não fazem sentido.

### Padrões formais que aparecem (e onde)

| Padrão           | Onde                                          | Por quê                                                              |
|------------------|-----------------------------------------------|----------------------------------------------------------------------|
| **Adapter**      | `ProviderAJsonAdapter`, `ProviderBXmlAdapter` | Cada provedor tem formato próprio, normaliza para `Debt` canônico    |
| **Strategy**     | `IInterestRule` + `IpvaRule`, `MultaRule`     | Regra de juros varia por tipo de débito; novo tipo = nova classe     |
| **Chain (leve)** | `DebtProviderChain` itera providers ordenados | Fallback entre provedores                                            |
| **Decorator**    | `ResilientDebtProvider` envelopa Provider c/ Polly (retry, timeout, circuit breaker) | Resiliência sem poluir o adapter                                      |
| **Ports & Adapters (Hexagonal light)** | Domain só conhece interfaces (`IDebtProvider`, `IClock`) | Domínio testável sem mocks de HTTP                                  |

## Decisão

**Hexagonal pragmático** com:
- **`DebtsService`** como fachada da camada Application, com método único de orquestração;
- Tratamento de erros **centralizado em middleware** (exceções de domínio mapeadas para 400/422/503);
- Decomposição interna detalhada no ADR-005.

## Justificativa

1. O fluxo é simples e linear: input → busca → cálculo → simulação → resposta. MediatR/CQRS adicionariam ritual sem ganho.
2. Domínio puro com ports (interfaces) garante testabilidade total sem mocks de HTTP.
3. Middleware central mantém Application/Domain agnósticos a ASP.NET — defensável como "separation of concerns" na banca.

---
