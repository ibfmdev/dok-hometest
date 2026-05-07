# ADR-005 — Decomposição da camada Application: fachada + colaboradores

**Status:** aceito
**Data:** 2026-04-27

## Contexto

Decidido o estilo arquitetural geral (ADR-004), faltava decidir a granularidade da camada Application: 1 service único com 1 método? 1 service com vários métodos? Decompor em colaboradores especializados?

A pergunta tem fundo em **SRP**: quem deve ter cada razão para mudar?

## Análise SRP

A versão moderna do SRP (Uncle Bob) diz: *"um módulo deve ter uma única razão para mudar — deve responder a um único ator"*.

Aplicando ao domínio:
- **Consulta de débitos** muda quando: novo provedor, nova regra de juros, novo tipo de débito → ator: domínio fiscal.
- **Simulação de pagamento** muda quando: nova forma de pagamento, novo desconto, novo parcelamento → ator: domínio de pagamento.

São atores diferentes — separar é correto.

## Opções consideradas

| Opção | Descrição | Tradeoff |
|---|---|---|
| A | `DebtsService.ConsultAndSimulate` único | Simples; classe com 2 razões para mudar (fere SRP) |
| B | `DebtsService` com 2 métodos (`Consult` + `Simulate`) | Métodos coesos, mas a classe ainda tem 2 razões para mudar |
| C | Fachada `DebtsService` orquestra `DebtsCalculator` + `PaymentSimulator` | Cada classe com 1 razão; fachada com responsabilidade própria de orquestração |
| D | Sem fachada, Controller chama os 2 colaboradores | Menos uma classe; orquestração migra para a borda HTTP |

## Decisão

**Opção C** com a seguinte composição:

```
DebtsService (fachada)
   ├── DebtsCalculator
   │     ├── IDebtProviderChain
   │     └── IInterestRule (Strategy por DebtType)
   └── PaymentSimulator
         (calcula PIX e cartão; puro, sem deps externas)
```

Nomes finais (em inglês):

- **`DebtsService`** — fachada do caso de uso. Único ponto de entrada para a Controller.
- **`DebtsCalculator`** — busca débitos e aplica juros, retorna débitos atualizados + totais.
- **`PaymentSimulator`** — recebe débitos atualizados, gera as opções de pagamento (TOTAL, SOMENTE_<TIPO>) com PIX e cartão.

## Justificativa

1. **SRP correto em três níveis**: cálculo fiscal, simulação de pagamento, e orquestração do caso de uso são responsabilidades distintas.
2. **Testabilidade**: `PaymentSimulator` testa-se sem mockar provedores; `DebtsCalculator` testa-se sem invocar `PaymentSimulator`.
3. **Cross-cutting natural**: logging do fluxo total, métricas, autorizações futuras vão na fachada — não poluem Controller nem colaboradores.
4. **Naming alinhado com o domínio**: o enunciado fala em "simular formas de pagamento" — `Simulator` adota o vocabulário (Ubiquitous Language). Não é `Processor` (não executa) nem `Calculator` (não retorna um número, retorna estrutura de opções).

---
