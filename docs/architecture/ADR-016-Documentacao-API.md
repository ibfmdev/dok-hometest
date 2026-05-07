# ADR-016 — Documentação de API: OpenAPI/Swagger

**Status:** aceito
**Data:** 2026-04-27

## Contexto

A spec **não menciona** OpenAPI/Swagger — nem como obrigatório nem como "bacana se". Mesmo assim, em uma API HTTP entregue como desafio sênior, ter documentação navegável é prática comum e tem dois ganhos práticos:

1. **Demo na banca**: avaliador pode interagir com a API via UI no navegador (sem precisar curl/Postman).
2. **Auto-documentação**: o schema OpenAPI gerado documenta o contrato de forma padrão, navegável, e importável em ferramentas (Postman, Insomnia, gen de clientes).

## Sub-decisões

1. **Implementar OpenAPI ou pular?**
2. **Qual lib gera o spec OpenAPI** (Swashbuckle vs NSwag vs `Microsoft.AspNetCore.OpenApi` nativo).
3. **Qual UI** (Swagger UI vs Scalar vs Redoc vs nenhuma).
4. **Como expor os VOs (`Plate`, `Money`) no schema** — strings, com formato/exemplo.
5. **Como documentar os erros** (400/422/503).

## Sub-decisão 1 — Implementar ou pular?

### Opção A — Implementar (recomendada)

- ✅ Demo de banca fica visualmente forte (Swagger UI / Scalar interativos).
- ✅ Documentação do contrato gerada automaticamente.
- ✅ Custo trivial em .NET 10 (`AddOpenApi()` + 1 endpoint).
- ❌ Mais 1 dependência (mínima) e mais 1 endpoint exposto.

### Opção B — Pular, documentar contrato no README

- ✅ Foco no que a spec pediu.
- ❌ Demo da banca fica via curl ou Postman — menos visual.
- ❌ Perde ponto de "auto-documentação como código".

## Sub-decisão 2 — Qual lib

### Importante: separação entre **gerador da spec** e **UI**

Em .NET 9+, a Microsoft separou explicitamente as duas responsabilidades:

- **Gerador da spec OpenAPI (JSON)**: a Microsoft introduziu `Microsoft.AspNetCore.OpenApi` como pacote **first-party** — esse é o "nativo".
- **UI** (Swagger UI, Scalar, Redoc): **não há UI nativa**. A Microsoft removeu o Swagger UI do template default em .NET 9, deixando a escolha ao dev.

Antes do .NET 9, o template default vinha com Swashbuckle pré-configurado (spec + UI). Em .NET 9+, o template inclui `AddOpenApi()` mas **nenhuma UI** — você adiciona separadamente conforme preferência.

### Opção A — `Microsoft.AspNetCore.OpenApi` nativo (.NET 9+) — recomendada para spec

A Microsoft introduziu o gerador OpenAPI nativo no .NET 9 como substituto preferencial do Swashbuckle. Em .NET 10 é first-class.

```csharp
builder.Services.AddOpenApi();      // gera spec OpenAPI 3
app.MapOpenApi("/openapi/{documentName}.json");  // expõe a spec
```

- ✅ **First-party Microsoft** em .NET 10 — argumento direto na banca.
- ✅ Zero dependência externa para gerar a spec.
- ✅ Performance superior (otimizado pelo time da Microsoft).
- ❌ **Não inclui UI**. Precisa adicionar Scalar / Swagger UI / Redoc separadamente.
- ❌ Menos features extras que Swashbuckle (sem `[SwaggerOperation]`, `[SwaggerResponseExample]`, etc.). Em compensação, aceita atributos do BCL como `ProducesResponseType`, `EndpointSummary`, etc.

### Opção B — Swashbuckle.AspNetCore (spec + UI no mesmo pacote)

Padrão histórico em .NET. Gera spec + Swagger UI integrada.

- ✅ Maduro, muitas opções de customização.
- ✅ Spec + UI numa lib só.
- ❌ Lib externa; em .NET 10 perdeu o status de "default" para o nativo da Microsoft.
- ❌ Em apresentação sênior em 2026, escolher Swashbuckle convida a pergunta *"por que não OpenAPI nativo do .NET 10?"*.

### Opção C — NSwag

- ✅ Pode gerar clients C# a partir da spec (não usaremos aqui).
- ❌ Complexo demais para o escopo. Inadequado.

## Sub-decisão 3 — Qual UI

### Opção A — Scalar (recomendada)

UI moderna lançada em 2024 pela startup Scalar (scalar.com), focada em ferramental de documentação de API. Pacote NuGet `Scalar.AspNetCore` — **lib externa, não Microsoft**, license MIT.

```csharp
app.MapScalarApiReference();  // expõe /scalar/v1
```

- ✅ **UI moderna**, dark mode nativo, navegação fluida — visualmente impactante.
- ✅ Plug direto na spec gerada pelo `Microsoft.AspNetCore.OpenApi`.
- ✅ Crescente adoção em projetos .NET modernos — sinal "atualizado" na banca.
- ❌ Lib externa relativamente nova (~2 anos em 2026) — vale verificar releases recentes antes de adotar.

### Opção B — Swagger UI

UI tradicional. Pacote `Swashbuckle.AspNetCore.SwaggerUI` (pode ser usado standalone com a spec do nativo).

- ✅ Padrão histórico, todo dev .NET reconhece.
- ✅ Funciona, é confiável, gratuito.
- ❌ **Visualmente datado**. Em 2026, Scalar passa imagem de modernidade.

### Opção C — Redoc

- ✅ Documentação estática mais bonita (para usuários finais lerem).
- ❌ Não interativo (não dá pra "Try it out"). Inadequado para demo de banca.

### Opção D — Sem UI (só JSON spec)

- ✅ Minimalista; quem quiser cola a spec em qualquer ferramenta.
- ❌ Demo da banca exige Postman/curl externos.

## Sub-decisão 4 — Como expor `Plate` e `Money` no schema

VOs do ADR-006 têm representação JSON específica:

- `Plate` é serializado como string (ex: `"ABC1234"`).
- `Money` é serializado como string decimal (ex: `"1500.00"`) — exigência da spec.

Configurar OpenAPI para reconhecer:

```csharp
builder.Services.AddOpenApi(o =>
{
    o.AddSchemaTransformer((schema, ctx, _) =>
    {
        if (ctx.JsonTypeInfo.Type == typeof(Plate))
        {
            schema.Type = "string";
            schema.Pattern = "^[A-Z]{3}\\d[A-Z]\\d{2}$|^[A-Z]{3}\\d{4}$";
            schema.Example = JsonValue.Create("ABC1234");
        }
        if (ctx.JsonTypeInfo.Type == typeof(Money))
        {
            schema.Type = "string";
            schema.Pattern = "^\\d+\\.\\d{2}$";
            schema.Example = JsonValue.Create("1500.00");
        }
        return Task.CompletedTask;
    });
});
```

Argumento de banca: *"VOs aparecem no schema como strings com pattern e exemplo — clientes consumindo a spec sabem exatamente o formato esperado"*.

## Sub-decisão 5 — Como documentar os erros

Atributos `[ProducesResponseType]` no Controller documentam cada possível resposta:

```csharp
[HttpPost]
[ProducesResponseType<DebtsResponse>(200)]
[ProducesResponseType<ErrorPayload>(400)]
[ProducesResponseType<UnknownDebtTypeErrorPayload>(422)]
[ProducesResponseType<ErrorPayload>(503)]
[ProducesResponseType<ErrorPayload>(413)]
public async Task<IActionResult> Consult(...) { ... }
```

Onde `ErrorPayload` é o record `{ string error }` e `UnknownDebtTypeErrorPayload` é `{ string error, string type }`.

## Recomendação consolidada

1. **Implementar OpenAPI** — diferencial visual e de auto-documentação por custo trivial.
2. **Lib geradora**: `Microsoft.AspNetCore.OpenApi` (nativo .NET 10).
3. **UI**: **Scalar** — moderna, integra com a spec nativa, visualmente impactante para demo.
4. **VOs**: registrados via `AddSchemaTransformer` com `type:"string"`, pattern, e example.
5. **Erros**: `[ProducesResponseType]` em cada Controller, com records dedicados para os payloads (`ErrorPayload`, `UnknownDebtTypeErrorPayload`).
6. **Endpoints expostos** (apenas em ambiente Development por default; opcional em Prod):
   - `GET /openapi/v1.json` — spec OpenAPI 3.
   - `GET /scalar/v1` — UI interativa.

## Decisão

1. **Implementar OpenAPI**: sim, mesmo sem estar na spec — diferencial visual e de auto-documentação por custo trivial.
2. **Geração da spec**: **`Microsoft.AspNetCore.OpenApi`** (nativo .NET 9+), via `AddOpenApi()` + `MapOpenApi("/openapi/v1.json")`.
3. **UI**: **Scalar** (`Scalar.AspNetCore`, lib externa MIT) via `MapScalarApiReference()` em `/scalar/v1`.
4. **VOs no schema**: `AddSchemaTransformer` registra `Plate` e `Money` como `type:"string"` com `pattern` e `example` — clientes consumidores da spec veem o formato exato.
5. **Documentação dos erros**: `[ProducesResponseType]` no Controller para todos os status (200, 400, 413, 422, 503), com records dedicados (`ErrorPayload`, `UnknownDebtTypeErrorPayload`).
6. **Endpoints expostos**: `/openapi/v1.json` (spec) e `/scalar/v1` (UI). Ambos disponíveis em todos os ambientes do desafio (Development e "Production"). Em produção real, restringir por flag — registrado como melhoria futura no README.

## Justificativa

1. **Spec via gerador nativo Microsoft** (.NET 9+) é o caminho moderno em 2026; first-party, zero dependência externa para a parte central, performance superior. Defesa direta: *"usei o gerador OpenAPI nativo do .NET 10"*.
2. **Scalar como UI** é a escolha visualmente moderna que casa com a "narrativa atualizada" da apresentação sênior. Reconhece-se que é lib externa relativamente nova — risco mitigado verificando saúde do pacote no momento da implementação.
3. **VOs no schema com pattern/example** transforma os Value Objects em contratos visíveis para clientes — argumento concreto contra "é só string".
4. **`[ProducesResponseType]` por status** documenta o contrato de erro completo (incluindo 413 e 422 da spec) — alinhado com ADR-014 e ADR-015.
5. **Custo total de implementação**: ~30 linhas em `Program.cs` + 1 schema transformer + atributos no Controller — desprezível.
6. **Mensagem na banca**: *"OpenAPI mesmo não estando na spec, gerador nativo do .NET 10, UI Scalar moderna; VOs aparecem como strings com pattern e example; todos os status documentados via `ProducesResponseType`. Custo: 30 linhas; ganho: demo interativa e contrato auto-documentado."*

---
