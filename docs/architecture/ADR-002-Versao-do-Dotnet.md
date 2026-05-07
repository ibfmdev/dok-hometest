# ADR-002 — Versão do .NET

**Status:** aceito
**Data:** 2026-04-27

## Contexto

Em abril de 2026, três versões do .NET são relevantes:

- **.NET 8 (LTS)** — lançado nov/2023, suporte até **nov/2026**.
- **.NET 9 (STS)** — lançado nov/2024, suporte até **mai/2026** (ou seja, sai de suporte daqui a ~1 mês).
- **.NET 10 (LTS)** — lançado nov/2025, suporte até **nov/2028**.

A escolha tem impacto em: features de C# disponíveis (C# 12 vs 13 vs 14), maturidade do ecossistema (bibliotecas com suporte a 10 ainda em catch-up), e a mensagem que passa na apresentação ("escolhi a LTS atual" vs "escolhi a versão mais consolidada").

## Opções consideradas

| Versão     | Prós                                                                                       | Contras                                                                                   |
|------------|--------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------|
| **.NET 8** | Mais maduro; ecossistema 100% compatível; muitos exemplos e tutoriais; ainda LTS          | Não é mais a versão "atual"; pode parecer conservador em entrevista                        |
| **.NET 9** | Algumas features novas                                                                     | STS expirando em ~30 dias — escolha **ruim** para apresentar; descartável                  |
| **.NET 10**| LTS atual; C# 14; suporte longo; sinaliza que o candidato acompanha a stack                | Algumas libs auxiliares (FluentValidation, Polly extensions, etc.) podem ainda estar atualizando — risco baixo, mas existe |

## Tradeoffs principais

- **8 vs 10**: a aplicação **não usa nada exclusivo de 9 ou 10** — ambos servem tecnicamente. A diferença é de mensagem e de longevidade do projeto. Em uma apresentação sênior em 2026, defender .NET 10 com "é a LTS atual e não vi razão para usar uma versão saindo de suporte" é forte. Defender .NET 8 com "é o LTS mais maduro, com o ecossistema 100% testado" também é defensável.
- **Polly, Serilog, FluentValidation, xUnit, WireMock.Net**: todos têm suporte oficial a .NET 8. Para .NET 10, todos já lançaram releases compatíveis até esta data — verificar antes de fechar.

## Decisão

**.NET 10 (LTS)**, garantindo a versão **GA** (não RC).

## Justificativa

1. .NET 8 sai de suporte em ~7 meses (nov/2026); começar um projeto novo na LTS que está acabando é uma escolha difícil de defender em apresentação.
2. .NET 9 já está praticamente fora de suporte (mai/2026) — descartado.
3. .NET 10 é LTS, suporte até nov/2028, e é a versão atual em 2026 — escolha alinhada com a postura "mantenho a stack atualizada".
4. A aplicação não usa nenhuma feature exclusiva de C# 14 / .NET 10; a escolha é por longevidade e mensagem na banca, não por dependência técnica.

## Notas de execução

- A máquina tinha instalada apenas a `10.0.100-rc.2.25502.107` (RC2). Em 2026-04-27 foi instalada a GA `10.0.203` via `dotnet-install.sh` no canal 10.0 quality GA. Runtime correspondente: `Microsoft.AspNetCore.App 10.0.7` / `Microsoft.NETCore.App 10.0.7`. SDK GA passou a ser a versão padrão (`dotnet --version` → `10.0.203`).
- A SDK RC2 segue instalada lado a lado mas não é mais a default; pode ser removida (`rm -rf ~/.dotnet/sdk/10.0.100-rc.2.25502.107` e o runtime equivalente) se quisermos higiene total. Decisão deixada para o usuário.
- Verificar compatibilidade de cada lib auxiliar com .NET 10 no momento de adicioná-la (Polly, Serilog, FluentValidation, xUnit, WireMock.Net, FluentAssertions). Se alguma estiver atrasada, registrar como dívida técnica e considerar fallback para .NET 8.

---
