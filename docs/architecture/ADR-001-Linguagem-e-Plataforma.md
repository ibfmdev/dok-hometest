# ADR-001 — Linguagem e plataforma: .NET / C#

**Status:** aceito
**Data:** 2026-04-27

## Contexto

O enunciado do desafio é agnóstico de linguagem ("use a linguagem que se sentir mais confortável"), mas o repositório foi inicializado com um `.gitignore` padrão de projetos .NET, sugerindo que a vaga é para uma stack .NET.

## Opções consideradas

| Stack            | Quando faria sentido                                                                  |
|------------------|----------------------------------------------------------------------------------------|
| .NET (C#)        | Vaga aparenta ser .NET; ecossistema maduro para APIs, DI, resiliência (Polly), testes |
| Node.js / TS     | Iteração mais rápida, ótima DX com Fastify+Zod                                        |
| Java (Spring)    | Robusto, mas mais cerimônia; pouca vantagem aqui                                      |
| Go               | Excelente para serviços HTTP enxutos; menos idiomático para o domínio rico do desafio |
| Python (FastAPI) | Prototipagem rápida; menos comum em vagas backend sênior brasileiras                  |

## Tradeoffs

- **A favor de .NET:** ecossistema padronizado para o tipo de problema (DI nativa, `HttpClientFactory`, Polly, ASP.NET Core), tipagem forte (importante para `decimal` e modelos canônicos), familiaridade do candidato, sinal de "fit" com a vaga.
- **Contra .NET:** projeto inicial demanda um pouco mais de boilerplate (solution, projetos, `Program.cs`) que outras stacks resolvem em um único arquivo.

## Decisão

.NET com C#.

## Justificativa

1. O `.gitignore` do repositório já está configurado para .NET, indicando expectativa.
2. O candidato declarou preferência pela stack.
3. .NET oferece de forma idiomática tudo o que o desafio pede: DI para troca de provedores, `HttpClient` + Polly para resiliência, `decimal` nativo para valores monetários, xUnit + FluentAssertions para testes, e Serilog para logs estruturados.

---
