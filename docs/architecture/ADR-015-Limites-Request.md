# ADR-015 — Limite de tamanho do body e rejeição de campos desconhecidos

**Status:** aceito
**Data:** 2026-04-27

## Contexto

A spec da v2 inclui no "Seria bacana se":

> "Limitar tamanho do corpo da requisição (ex.: 1 MiB) e rejeitar JSON com campos desconhecidos."

Não é obrigatório, mas **fazer e fazer bem** é uma diferenciação cheia de argumentos para a banca: defesa contra abuso, contrato HTTP estrito, "shift left" de bugs.

A request real do desafio é minúscula:

```json
{ "placa": "ABC1234" }
```

— ~25 bytes. Defaults do Kestrel permitem **30 MB** de body. Há larga margem para limitação sem afetar o caso real.

## Sub-decisões

1. **Limite de tamanho do body** (e qual valor).
2. **Onde aplicar** o limite (global vs por endpoint).
3. **Rejeição de campos desconhecidos** no JSON.
4. **Status code de cada falha** (`413` vs `400`).
5. **Payload de erro** alinhado ao ADR-014.

## Sub-decisão 1 — Limite de tamanho do body

### Opção A — Global no Kestrel: 1 MiB (recomendada)

```csharp
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = 1 * 1024 * 1024; // 1 MiB
});
```

- ✅ **Aplica a todos os endpoints** — futura adição não esquece.
- ✅ Valor sugerido pela spec.
- ✅ Generoso para o caso real (~40.000× o tamanho da request mínima).
- ❌ Mudança global; se um endpoint precisar de mais, requer override explícito (na verdade, é desejável).

### Opção B — Por endpoint via atributo `[RequestSizeLimit(...)]`

```csharp
[HttpPost, RequestSizeLimit(1_048_576)]
public async Task<IActionResult> Consult(...) { ... }
```

- ✅ Granular.
- ❌ Esquecer em um endpoint = brecha. Em projeto pequeno passa, em grande vira dor de cabeça.

### Opção C — Manter default (30 MB)

- ❌ Brecha de abuso desnecessária para o nosso caso.
- ❌ Perde o ponto de banca da spec.

### Sobre o valor (1 MiB é exagerado?)

Pode-se argumentar que 1 MiB ainda é generoso para `{"placa":"..."}`. Alternativas mais agressivas: 4 KiB, 8 KiB, 64 KiB.

| Limite | Trade-off |
|---|---|
| 4 KiB | Defensivo. Mas se a request crescer no futuro (ex: lista de placas), virou bloqueador. |
| 64 KiB | Defensivo, com margem para evolução. |
| **1 MiB** | Sugerido pela spec, generoso, alinha com a recomendação. |

**Recomendado: 1 MiB**, alinhado com a sugestão explícita da spec. Se alguém na banca questionar *"por que não menor?"*, defender com *"segui a sugestão do enunciado; reduzir mais sem necessidade seria divergência sem ganho real"*.

## Sub-decisão 2 — Rejeição de campos desconhecidos no JSON

Default do `System.Text.Json` em .NET 8+: campos extras na request **são silenciosamente ignorados**.

### Opção A — `JsonUnmappedMemberHandling.Disallow` (recomendada)

```csharp
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});
// para Controllers:
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});
```

Disponível desde .NET 8+. Configura globalmente.

- ✅ **Estrito**: campo desconhecido → desserialização falha → 400.
- ✅ Pega bugs do cliente cedo (ex: typo `placca` em vez de `placa`).
- ✅ Defensável: *"contrato HTTP estrito; clientes recebem feedback explícito ao mandar campos não documentados"*.
- ❌ Diverge de "be liberal in what you accept" (Postel's Law) — mas esse princípio se aplica melhor a protocolos de rede de baixo nível, não a APIs com contrato bem definido e cliente conhecido.

### Opção B — Default `Skip` (silenciosamente ignora)

- ✅ Tolerante.
- ❌ **Esconde bugs do cliente**. Typo passa, request "funciona", o cliente não percebe que mandou errado.
- ❌ Perde o ponto de banca da spec.

### Opção C — Atributo no DTO

`[JsonObjectCreationHandling]` ou `[UnmappedMemberHandlingAttribute]` no record do DTO.

- ✅ Granular por DTO.
- ❌ Esquecer em um DTO = brecha. Pior que global por endpoint.

## Sub-decisão 3 — Status code de cada falha

| Causa | Status | Payload | Origem |
|---|---|---|---|
| Body excede limite | `413 Content Too Large` | `{"error":"payload_too_large"}` | Kestrel rejeita antes do controller; ainda assim, mapear o handler para retornar payload da spec |
| Campo desconhecido | `400 Bad Request` | `{"error":"invalid_request"}` | falha na desserialização |
| JSON malformado | `400 Bad Request` | `{"error":"invalid_request"}` | já tratado pelo ASP.NET; só padronizar payload |

### Detalhe sobre o 413 do Kestrel

Kestrel responde 413 **antes** do pipeline do ASP.NET. Configurar `IExceptionHandler` para isso requer interceptar via middleware adicional ou configurar Kestrel para lançar exceção que o handler captura.

Alternativa simples: aceitar o comportamento default do Kestrel (413 com body vazio) e documentar como melhoria futura padronizar o payload. Decisão:

- **Mínimo viável**: limite no Kestrel ativo, payload padronizado é melhoria futura.
- **Versão completa**: middleware leve antes do `IExceptionHandler` que detecta `BadHttpRequestException` (Kestrel) e produz `{"error":"payload_too_large"}` com status 413.

Recomendado: **versão completa** — alinhamento total com o estilo do ADR-014.

## Sub-decisão 4 — Onde os erros desta categoria entram no `IExceptionHandler` do ADR-014

Adicionar caso ao pattern matching do `DomainExceptionHandler` ou criar novo `BadRequestExceptionHandler` na chain. Recomendado: **handler separado** (`HttpRequestErrorsHandler`) responsável por traduzir falhas de framework HTTP para o formato da spec — separação por origem do erro.

## Recomendação consolidada

1. **Limite de body**: 1 MiB global via `Kestrel.MaxRequestBodySize`.
2. **Campos desconhecidos**: `JsonUnmappedMemberHandling.Disallow` em `AddJsonOptions` (Controllers).
3. **Status codes e payloads**:
   - Body grande → `413` com `{"error":"payload_too_large"}`.
   - Campo desconhecido / JSON malformado → `400` com `{"error":"invalid_request"}`.
4. **Integração com `IExceptionHandler` (ADR-014)**: handler dedicado a erros de borda HTTP (`HttpRequestErrorsHandler`) na chain, antes do `DomainExceptionHandler`.
5. **Documentar no README**: limite, comportamento estrito de campos desconhecidos, e códigos retornados.

## Decisão

1. **Limite de body**: **1 MiB por padrão**, **configurável** via `RequestLimits:MaxBodyBytes` em `appsettings.json` (alinhado ADR-017). Aplicado globalmente via `Kestrel.Limits.MaxRequestBodySize`.
2. **Campos desconhecidos**: **`JsonUnmappedMemberHandling.Disallow`** configurado em `AddJsonOptions` para Controllers (e `ConfigureHttpJsonOptions` para Minimal API, se algum dia adotada).
3. **Status codes e payloads**:
   - Body excede limite → `413 Content Too Large` com `{"error":"payload_too_large"}`.
   - Campo desconhecido / JSON malformado → `400 Bad Request` com `{"error":"invalid_request"}`.
4. **Integração com `IExceptionHandler` (ADR-014)**: handler dedicado `HttpRequestErrorsHandler` na chain, **antes** do `DomainExceptionHandler` — separação por origem do erro (borda HTTP vs domínio).
5. **Documentar no README**: limite, comportamento estrito de campos desconhecidos, códigos retornados — para clientes da API saberem o contrato.

### Configuração e ciclo de vida

```json
{
  "RequestLimits": {
    "MaxBodyBytes": 1048576
  }
}
```

Como `ConfigureKestrel` roda na **fase de startup do host** (antes do `IServiceProvider` estar pronto para `IOptions<T>`), o valor é lido diretamente de `builder.Configuration` no `Program.cs`:

```csharp
var maxBodyBytes = builder.Configuration.GetValue<long?>("RequestLimits:MaxBodyBytes")
                   ?? 1L * 1024 * 1024;
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = maxBodyBytes);
```

**Hot reload**: limites do Kestrel **não** são reconfigurados em runtime — alterações em `appsettings.json` exigem **reiniciar o processo** para entrar em vigor. Isso é uma limitação do próprio Kestrel, não do design da aplicação. Documentado no README.

## Justificativa

1. **Spec sugere explicitamente** (`"Seria bacana se: limitar tamanho do corpo (ex.: 1 MiB) e rejeitar JSON com campos desconhecidos"`). Implementar fecha a coleção dos "bacana se" da v2.
2. **Custo trivial** (~30 linhas de produção total) frente ao ganho narrativo: contrato HTTP estrito, defesa contra abuso, bugs de cliente pegos cedo.
3. **`Disallow`** é mais defensável que "be liberal in what you accept" para APIs com contrato bem definido — Postel's Law se aplica melhor a protocolos de baixo nível.
4. **Handler dedicado para erros de borda HTTP** mantém a separação de responsabilidades do ADR-014: domínio lança suas exceções, borda HTTP traduz suas próprias falhas — cada handler com uma única razão para mudar.
5. **Mensagem na banca**: *"implementei mesmo sendo opcional pelo custo trivial e pelo argumento de contrato HTTP estrito. 1 MiB é o valor sugerido pela spec; campos desconhecidos rejeitados pegam typos cedo (`placca` em vez de `placa`) em vez de propagarem como request 'aparentemente válida' com placa nula."*

---
