# ADR-003 — Estilo de API: Minimal API vs Controllers

**Status:** aceito
**Data:** 2026-04-27
**Revisada:** 2026-05-02

## Contexto

ASP.NET Core oferece dois estilos para expor endpoints HTTP:

- **Minimal API** (introduzido em .NET 6, refinado em 7/8/9/10): endpoints declarados diretamente em `Program.cs` ou em módulos de extensão; bind de parâmetros via convenção; ideal para APIs pequenas/médias e microsserviços. Em .NET 8+, a comunidade convergiu no padrão **REPR (Request-Endpoint-Response)** via interface `IEndpoint` custom + auto-discovery, popularizado por Ardalis/Jovanović/Chapsas.
- **Controllers (MVC)**: classes que herdam de `ControllerBase`, atributos `[HttpPost]`/`[FromBody]`, model binding clássico, filtros, ações; padrão histórico do ASP.NET, ainda dominante em sistemas corporativos.

A escolha afeta: estrutura do código de borda, ergonomia de versionamento, integração com OpenAPI, posicionamento dos validadores, e a percepção da banca ("moderno e enxuto" vs "tradicional e familiar").

## Opções consideradas

| Estilo         | Prós (no contexto deste teste)                                                                  | Contras (no contexto deste teste)                                                                |
|----------------|--------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------|
| **Minimal API**| Casa com a estética "à mão" do projeto (zero `PackageReference` em Domain, sem MediatR/AutoMapper); para 1 endpoint o estilo é mais enxuto; aproveita `TypedResults` e OpenAPI nativo do .NET 10; mensagem de senioridade ("escolhi o estilo recomendado pela Microsoft pra APIs pequenas") | Exige `RouteHandlerOptions.ThrowOnBadRequest = true` para que `JsonException` em deserialização propague até o `IExceptionHandler` chain — sem isso, o framework intercepta e retorna `ProblemDetails`, quebrando o contrato literal `{"error":"invalid_request"}` da spec; pegadinha não-óbvia que precisa ser documentada |
| **Controllers**| `[ApiController]` expõe o hook `InvalidModelStateResponseFactory` que substitui o ProblemDetails padrão pelo payload literal da spec em uma linha; estrutura por convenção (`Controllers/`) reduz custo de onboarding; pipeline de filtros maduro caso o sistema cresça com auth/audit/rate-limit; familiaridade ampla na comunidade .NET corporativa brasileira | Para 1 endpoint, uma classe inteira por método é cerimônia desproporcional; do `[ApiController]` em si, este projeto aproveita **apenas o `InvalidModelStateResponseFactory`** — as outras features (`DataAnnotations`, inferência de `[FromBody]`, ProblemDetails automático) não são usadas |

## Tradeoffs principais

- **Tamanho do projeto**: o desafio expõe um único endpoint (`POST /api/v1/debitos`). Minimal API é o estilo desenhado pra esse perfil.
- **Estética do projeto**: o resto do código é deliberadamente sem frameworks pesados. `Domain.csproj` tem zero `PackageReference`/`ProjectReference`. Plate/Money/IInterestRule são VOs/regras manuais. Trazer o pipeline MVC para 1 endpoint destoa um pouco.
- **Contrato literal da spec**: a spec exige `{"error":"..."}` (não `ProblemDetails`). Em Controllers isso resolve via `InvalidModelStateResponseFactory`. Em Minimal API resolve via `RouteHandlerOptions.ThrowOnBadRequest = true` + `IExceptionHandler` (já existente). Empate funcional.
- **Ergonomia de erro**: a chain `IExceptionHandler` (ADR-014) é agnóstica ao estilo da Api — funciona idêntica nos dois.
- **Reaproveitamento**: a camada `Application` (`IDebtsService`) é agnóstica e independente do estilo escolhido.
- **Custo de mudança**: trocar Controllers por Minimal API a 48h da apresentação é refator de baixo valor com risco de regressão. Manter o que já está feito e testado tem ganho não-linear.

## Decisão

**Controllers (MVC)**.

## Justificativa

A decisão privilegia três fatores concretos no contexto desta entrega:

1. **Hook direto para o contrato literal de erro da spec.** `[ApiController]` permite sobrescrever a resposta automática 400 via `InvalidModelStateResponseFactory` em uma linha, retornando `{"error":"invalid_request"}` exato. Em Minimal API a equivalência funcional existe (`ThrowOnBadRequest`), mas é uma pegadinha de configuração não-óbvia que adiciona ruído à narrativa.
2. **Estrutura por convenção facilita defesa em banca.** `Controllers/DebtsController.cs` é imediatamente legível por qualquer dev .NET sênior — *isto é uma controller, isto é um service, isto é a regra de domínio*. Em Minimal API a organização precisa ser ensinada (REPR via IEndpoint custom funciona, mas exige introdução).
3. **Custo/benefício a 48h da apresentação.** A migração para Minimal API seria refator de baixo valor (zero capacidade nova demonstrada, zero teste novo passando) com risco não-zero de regressão no contrato de erro literal. O tempo restante até 2026-05-04 tem aplicação melhor no polimento da narrativa e no item 9 (skills de modificação ao vivo).

## Reconhecimento honesto

Para um projeto com este perfil — 1 endpoint, hexagonal manual, zero-dep em Domain — **Minimal API com REPR via IEndpoint custom seria igualmente defensável e provavelmente mais coerente esteticamente**. A ADR registra essa avaliação para que a defesa em banca não seja "Controllers porque é o que sempre uso" e sim "Controllers porque o gancho do `InvalidModelStateResponseFactory` resolveu o contrato literal da spec em uma linha, e o custo de migrar agora não compensa."

## Quando reavaliaríamos

- **Crescimento para múltiplos endpoints stateless** (>5): o boilerplate de Controllers começa a pesar; REPR via IEndpoint cresce melhor.
- **Adoção de versionamento amplo** (`v1`, `v2`, `v3`): `MapGroup` em Minimal API tem ergonomia mais limpa que `[Route("api/v2/...")]` espalhado em controllers.
- **Pivô para microsserviço de propósito único**: Minimal API é o estilo desenhado pra esse perfil.

## Notas

- Mesmo com Controllers, o `Program.cs` é mantido enxuto: serviços agrupados em métodos de extensão (`AddDokJson`, `AddDokErrorHandling`, `AddDokOpenApi`), pipeline de middleware claro.
- O que **de fato** se aproveita do `[ApiController]` neste projeto é apenas o hook `InvalidModelStateResponseFactory`. As outras features (validação `DataAnnotations`, inferência de `[FromBody]`, `ProblemDetails` automático) **não são usadas** — o `required` do C# 11 substitui `[Required]`, e o `ProblemDetails` é explicitamente sobrescrito.

---
