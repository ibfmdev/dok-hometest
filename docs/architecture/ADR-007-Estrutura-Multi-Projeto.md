# ADR-007 — Estrutura física da solution: monoprojeto vs múltiplos `.csproj`

**Status:** aceito
**Data:** 2026-04-27

## Contexto

Decidida a arquitetura lógica (camadas Api/Application/Domain/Infrastructure no ADR-004), falta decidir como essas camadas ficam **fisicamente** organizadas: tudo em um único `.csproj` (camadas como pastas) ou cada camada em um `.csproj` separado, conectados via referências de projeto?

A escolha tem impacto em: enforcement das boundaries arquiteturais, velocidade de build, ergonomia de teste, narrativa para a banca, e robustez frente a modificações ao vivo com IA.

## A pergunta central

> Em monoprojeto, as camadas são **convenções** (pastas e namespaces). Em multi-projeto, as camadas são **contratos** verificados pelo compilador.

A questão é: vale o custo de cerimônia para ter as boundaries enforçadas no compilador?

## Opção A — Monoprojeto (1 csproj de produção + 1 de testes)

```
Dok.sln
├── src/Dok/Dok.csproj
│   ├── Api/
│   ├── Application/
│   ├── Domain/
│   └── Infrastructure/
└── tests/Dok.Tests/Dok.Tests.csproj
```

### A favor
- **Simplicidade**: 1 csproj para gerenciar pacotes NuGet, 1 lugar para configurar build.
- **Velocidade**: build incremental mais rápido.
- **Refactor fluido**: mover arquivo entre pastas é trivial.
- **Setup mais rápido**: ~2 minutos para criar o esqueleto.

### Contra
- **Boundaries são convenção, não contrato**: nada impede `using Dok.Infrastructure` dentro do código que está em `Domain/`. Se alguém (ou a IA durante a apresentação) violar, ninguém percebe sem revisão manual.
- **Testes do Domain carregam ASP.NET**: porque o csproj é um só, todos os testes de Domain referenciam tudo. Marginal aqui, mas é sinal de acoplamento desnecessário.
- **Narrativa fraca na banca**: *"organizei em pastas"* não é prova de isolamento. O enunciado pede isolamento explicitamente.

## Opção B — Multi-projeto (4 src + 3 tests) — recomendada

```
Dok.sln
├── src/
│   ├── Dok.Api/                  → ref: Dok.Application, Dok.Infrastructure
│   ├── Dok.Application/          → ref: Dok.Domain
│   ├── Dok.Domain/               → ref: nenhuma (núcleo puro)
│   └── Dok.Infrastructure/       → ref: Dok.Domain
└── tests/
    ├── Dok.Domain.Tests/         → ref: Dok.Domain
    ├── Dok.Application.Tests/    → ref: Dok.Application + mocks
    └── Dok.Integration.Tests/    → ref: Dok.Api + WireMock
```

### Dependency Rule materializada

| Projeto | Pode referenciar | Não pode |
|---|---|---|
| `Dok.Domain` | nada | qualquer coisa externa |
| `Dok.Application` | `Dok.Domain` | `Dok.Infrastructure`, `Dok.Api` |
| `Dok.Infrastructure` | `Dok.Domain` (modelo canônico) | `Dok.Application`, `Dok.Api` |
| `Dok.Api` | `Dok.Application`, `Dok.Infrastructure` | — (composition root) |

Tentativa de violar = **erro de compilação**, não revisão de código.

### A favor
- **Boundaries enforçadas pelo compilador**: o ADR-004 (Hexagonal pragmático) ganha vida real, não fica só no diagrama.
- **Domain testável em puro**: os testes do `Dok.Domain` não carregam ASP.NET, Polly, Serilog. Mais rápidos, mais determinísticos.
- **Pacotes NuGet em escopo correto**: Polly fica em `Infrastructure`, FluentValidation na `Api`, nada poluindo `Domain`. Sinal técnico forte.
- **Modificações ao vivo com IA**: a IA respeita boundaries porque o compilador a obriga. Mitigação contra acidentes em apresentação.
- **Narrativa de banca**: abrir `Dok.Domain.csproj` e mostrar `<ItemGroup>` vazio (zero `<PackageReference>`, zero `<ProjectReference>`) é uma demonstração visual potente de "domínio puro".
- **Prepara para crescimento**: se um dia esse Domain virar lib reusável, já está empacotado.

### Contra
- **Setup inicial mais longo**: ~5 minutos extras (criar projetos, adicionar referências).
- **Build mais lento**: marginal nesse escopo (~1-2s), mas existe.
- **Ergonomia de pacotes**: cada NuGet vai no projeto certo — exige um momento de pensar. Em times pouco experientes pode levar a duplicações.
- **Refactor entre projetos**: mover um arquivo de `Application` para `Domain` exige `dotnet add reference` ajustado.

## Tradeoffs principais (lado a lado)

| Critério | A — Monoprojeto | B — Multi-projeto |
|---|---|---|
| Boundaries | convenção (pasta/namespace) | contrato (compilador) |
| Risco de violação por descuido (humano ou IA) | alto | nulo (não compila) |
| Setup inicial | ~2 min | ~5-7 min |
| Build incremental | ~1s mais rápido | ~1-2s |
| Testes do Domain | carregam ASP.NET indireto | rodam isolados |
| Pacotes NuGet em escopo correto | manual | enforçado por csproj |
| Refactor entre camadas | move arquivo | move arquivo + ajusta reference |
| Narrativa para banca sênior | fraca | forte (`Domain.csproj` vazio é prova) |
| Modificações ao vivo com IA | risco de atalho errado | IA respeita compilador |

## Por que multi-projeto paga aluguel especificamente neste desafio

1. **O enunciado pede isolamento explícito** entre integração/domínio/pagamento. Multi-projeto é a forma mais forte de demonstrar.
2. **Apresentação ao vivo com IA** — boundaries físicas impedem a IA de fazer atalho errado por descuido durante a banca.
3. **Vaga sênior nível 3** — avaliador espera ver dependency rule explícita; "organizei em pastas" é resposta de pleno.
4. **Custo é trivial** — 5 minutos de setup contra um argumento de defesa que permeia toda a apresentação.

## Estrutura proposta detalhada

```
Dok.sln
├── src/
│   ├── Dok.Api/
│   │   ├── Controllers/DebtsController.cs
│   │   ├── Middlewares/ExceptionHandlingMiddleware.cs
│   │   ├── Validators/PlateValidator.cs
│   │   ├── Json/PlateJsonConverter.cs
│   │   ├── Json/MoneyJsonConverter.cs
│   │   ├── DependencyInjection.cs        (extension method)
│   │   ├── Program.cs
│   │   └── Dok.Api.csproj
│   ├── Dok.Application/
│   │   ├── DebtsService.cs
│   │   ├── DebtsCalculator.cs
│   │   ├── PaymentSimulator.cs
│   │   ├── DTOs/...
│   │   ├── Abstractions/IDebtsService.cs
│   │   └── Dok.Application.csproj
│   ├── Dok.Domain/
│   │   ├── Plate.cs
│   │   ├── Money.cs
│   │   ├── Debt.cs
│   │   ├── DebtType.cs
│   │   ├── Rules/IInterestRule.cs
│   │   ├── Rules/IpvaInterestRule.cs
│   │   ├── Rules/MultaInterestRule.cs
│   │   ├── Exceptions/...
│   │   ├── Abstractions/IClock.cs
│   │   └── Dok.Domain.csproj           (ZERO PackageReference, ZERO ProjectReference)
│   └── Dok.Infrastructure/
│       ├── Providers/IDebtProvider.cs   (port — alternativamente em Domain)
│       ├── Providers/IDebtProviderChain.cs
│       ├── Providers/ProviderAJsonAdapter.cs
│       ├── Providers/ProviderBXmlAdapter.cs
│       ├── Providers/DebtProviderChain.cs
│       ├── Providers/Resilience/...
│       ├── Time/SystemClock.cs
│       └── Dok.Infrastructure.csproj
└── tests/
    ├── Dok.Domain.Tests/
    ├── Dok.Application.Tests/
    └── Dok.Integration.Tests/
```

## Decisão

**Multi-projeto** com a estrutura proposta (4 src + 3 tests).

## Justificativa

1. O enunciado pede explicitamente o isolamento de integração/domínio/pagamento — multi-projeto materializa isso no compilador, não em comentários.
2. Apresentação ao vivo com IA: boundaries físicas mitigam o risco de a IA fazer atalho errado por descuido durante a banca.
3. `Dok.Domain.csproj` com zero `PackageReference` e zero `ProjectReference` é uma demonstração visual potente para o avaliador.
4. Custo de setup (~5 minutos) é desprezível frente ao ganho narrativo e técnico.

---
