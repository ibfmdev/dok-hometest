# ADR-010 — Logging estruturado: Serilog vs Microsoft.Extensions.Logging puro

**Status:** aceito
**Data:** 2026-04-27

## Contexto

O enunciado pede:

- Logs estruturados.
- Mascaramento de placa para LGPD (`ABC1234` → `ABC****`).
- Idealmente: explicar uso de logs na apresentação.

A decisão tem 4 camadas:

1. **Lib**: Serilog vs Microsoft.Extensions.Logging (MEL) puro.
2. **Estratégia de mascaramento de placa**.
3. **Formato e sinks**: console pretty em dev, JSON em prod, sinks adicionais (Seq?).
4. **Correlação**: TraceId por request.

## Camada 1 — Serilog vs MEL puro

### Opção A — Serilog (recomendada)

Lib clássica de logging estruturado em .NET. Integra com MEL via `Serilog.Extensions.Logging` — código usa `ILogger<T>` normalmente, Serilog é o provider por baixo.

- ✅ **Padrão da comunidade .NET para logging sério**: o avaliador reconhece de imediato.
- ✅ **JSON estruturado de primeira**: `Serilog.Formatting.Compact.CompactJsonFormatter` produz logs prontos para qualquer agregador (Datadog, Splunk, Elasticsearch).
- ✅ **Enrichers e Destructuring**: facilitam mascaramento de placa, adicionar TraceId, MachineName, ThreadId.
- ✅ **Sinks variados**: console, arquivo, Seq, etc., apenas adicionando pacotes.
- ✅ **Configuração via `appsettings.json`** (`Serilog.Settings.Configuration`).
- ❌ Dependência externa (várias libs Serilog: core, Sinks.Console, Settings.Configuration, Extensions.Logging, Enrichers...).
- ❌ Bootstrap em `Program.cs` é mais verboso.

### Opção B — Microsoft.Extensions.Logging puro

Logging built-in do .NET. API `ILogger<T>` direto, com providers `AddConsole`, `AddJsonConsole`, `AddOpenTelemetry`.

- ✅ **Zero dependência externa**: argumento de minimalismo.
- ✅ **Source generators em .NET 8+** (`LoggerMessage` attribute) → logging zero-allocation, performance superior em hot path.
- ✅ Idiomático em apps minimalistas.
- ❌ JSON nativo (`AddJsonConsole`) é menos polido que Serilog Compact JSON. Renderização menos legível.
- ❌ Mascaramento custom exige escrever próprio `ILogEnricher` ou processador — Serilog tem isso pronto.
- ❌ Menos enriquecimento out-of-the-box.

## Camada 2 — Estratégia de mascaramento de placa (LGPD)

Quatro opções, em ordem de robustez crescente:

| Opção | Como funciona | Risco |
|---|---|---|
| **2.1 — Manual** | Sempre passar `plate.Masked()` no log. *"Cuidado humano"* | Alguém vai esquecer um dia |
| **2.2 — Override do `ToString()` no `Plate`** | `Plate.ToString()` retorna mascarado por default | Quebra qualquer lugar que esperava o valor cru (ex: serialização de DTO via `ToString`) |
| **2.3 — Destructuring policy** (Serilog) | Serilog vê `Plate` e aplica regra de mascaramento na formatação do log automaticamente | Funciona apenas em Serilog; precisa de configuração inicial |
| **2.4 — Enricher de log** | Antes do log sair, varre as propriedades e mascara qualquer campo "plate" | Mais genérico mas mais intrusivo |

**Recomendado**: **2.3 — Destructuring policy** com Serilog. Mantém `plate.ToString()` retornando o valor real (útil em outros contextos), e o mascaramento acontece **automaticamente** no momento do log.

```csharp
public sealed class PlateDestructuringPolicy : IDestructuringPolicy
{
    public bool TryDestructure(object value, ILogEventPropertyValueFactory factory, out LogEventPropertyValue? result)
    {
        if (value is Plate plate)
        {
            result = new ScalarValue(plate.Masked());
            return true;
        }
        result = null;
        return false;
    }
}
```

E o uso fica natural no código de produção:

```csharp
_logger.LogInformation("Consulting debts for {@Plate}", plate);
// log emitido: "Consulting debts for ABC****"
```

O `@` força o Serilog a aplicar destructuring (em vez de `ToString()`), e a policy entra em ação. **Impossível esquecer**, porque é a configuração da lib que faz o trabalho.

## Camada 3 — Formato e sinks

| Cenário | Formato sugerido |
|---|---|
| Dev local (apresentação) | **Console pretty** com cores: `outputTemplate` legível para o avaliador acompanhar ao vivo |
| Testes | Console pretty (mesmo da dev, ajuda em debug de teste) |
| "Produção" do desafio | **JSON Compact** no console (pronto para qualquer agregador) — ativado por flag `Logging:Format=json` |

Sinks: **apenas console** para o desafio. Mencionar no README como melhoria futura: Seq local para experiência de banca ainda melhor (consultas estruturadas, filtros) — mas adicionar Seq no setup pesa contra simplicidade.

## Camada 4 — Correlação (TraceId)

ASP.NET Core já gera **W3C TraceContext** automaticamente em cada request (`Activity.Current.TraceId`). Serilog tem enricher (`Serilog.Enrichers.Span` ou propriedade nativa) que adiciona `TraceId` em todos os logs daquela request.

**Recomendado**: ativar enricher de TraceId. Cada log emitido por uma request carrega o mesmo `TraceId` — facilita correlacionar tudo que aconteceu (incluindo retries, fallback entre providers, deserialização) em uma única request.

## Resumo da recomendação

- **Lib**: Serilog.
- **Mascaramento**: `IDestructuringPolicy` para `Plate` — mascaramento automático e impossível de esquecer.
- **Formato**: console pretty em dev/teste, JSON Compact em produção (toggle por configuração).
- **Sinks**: apenas console; mencionar Seq como melhoria futura.
- **Correlação**: enricher de TraceId habilitado.

## Decisão

- **Lib**: **Serilog** (substitui o provider default; código continua usando `ILogger<T>` via `Serilog.Extensions.Logging`).
- **Mascaramento de placa (LGPD)**: `IDestructuringPolicy` para `Plate` que retorna `plate.Masked()`. Uso natural no código de produção: `_logger.LogInformation("Consulting debts for {@Plate}", plate)`. Mascaramento aplicado automaticamente — impossível esquecer.
- **Formato e sinks**: console pretty (`outputTemplate` legível) em desenvolvimento e testes; JSON Compact (`CompactJsonFormatter`) em "produção" do desafio, ativado por flag `Logging:Format=json`. Único sink: console. Seq mencionado no README como melhoria futura.
- **Correlação**: enricher de TraceId habilitado (W3C TraceContext nativo do ASP.NET → propriedade `TraceId` em todos os logs da request).

## Justificativa

1. **Serilog** é o padrão da comunidade .NET para logging estruturado sério; o avaliador reconhece de imediato e o argumento *"Compact JSON pronto para qualquer agregador"* é direto.
2. **`IDestructuringPolicy`** transforma o mascaramento em configuração da lib — não em política humana esquecível. Em apresentação sênior, mostrar que LGPD foi tratada como configuração e não como disciplina é diferencial.
3. **Console + dois formatos** atende dev (legibilidade) e produção (estruturado) sem complexificar com sinks externos. Demonstra maturidade operacional sem over-engineering.
4. **TraceId enricher** dá correlação entre logs da mesma request (incluindo retries Polly e fallback A→B) sem código adicional — o ASP.NET já gera o W3C TraceId; só estamos propagando.

---
