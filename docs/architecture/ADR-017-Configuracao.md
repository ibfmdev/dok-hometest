# ADR-017 — Configuração e segredos: `appsettings.{env}.json`, IOptions, validação

**Status:** aceito
**Data:** 2026-04-27

## Contexto

Várias decisões anteriores produziram **valores configuráveis** que precisam morar em algum lugar:

- ADR-008: URLs dos provedores (`http://localhost:9001`, `http://localhost:9002`).
- ADR-009: parâmetros de Polly (timeouts, retry counts, circuit breaker).
- ADR-010: formato de log (`pretty` vs `json`).
- ADR-015: limite de body (1 MiB) — pode ser fixo, mas configurável dá flexibilidade.

Decisões a tomar neste ADR:

1. **Estrutura de arquivos** de configuração.
2. **Como ler em código**: `IOptions<T>` vs `IConfiguration` direto vs ambas.
3. **Validação de configuração** no startup (fail-fast).
4. **Segredos**: este desafio não tem segredos reais (sem auth, sem DB), mas mostrar a abordagem é diferencial.
5. **Override por ambiente**: Development local vs "Production" (simulada) do desafio.

## Sub-decisão 1 — Estrutura de arquivos

### Opção A — Hierarquia padrão ASP.NET (recomendada)

```
src/Dok.Api/
├── appsettings.json                  # defaults para todos os ambientes
├── appsettings.Development.json      # overrides para dev local
├── appsettings.Production.json       # overrides para "produção"
└── appsettings.Tests.json            # overrides para testes (opcional)
```

ASP.NET carrega na ordem: `appsettings.json` → `appsettings.{Environment}.json` → variáveis de ambiente → command line. Cada nível sobrescreve o anterior.

- ✅ Padrão ASP.NET; o avaliador reconhece sem fricção.
- ✅ Override por ambiente é nativo (basta `ASPNETCORE_ENVIRONMENT=Production`).
- ✅ Variáveis de ambiente sobrescrevem (útil para CI/Docker/Kubernetes).

### Opção B — Apenas `appsettings.json` único

- ✅ Mínimo.
- ❌ Não dá pra mostrar override por ambiente — perde o argumento operacional.

## Sub-decisão 2 — Como ler em código

### Opção A — `IOptions<T>` com classes tipadas (recomendada)

```csharp
// classe POCO
public sealed class ProvidersOptions
{
    public const string SectionName = "Providers";

    [Required, Url] public required string ProviderAUrl { get; init; }
    [Required, Url] public required string ProviderBUrl { get; init; }
}

// Program.cs
builder.Services.AddOptions<ProvidersOptions>()
    .Bind(builder.Configuration.GetSection(ProvidersOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// uso (DI)
public class DebtProviderChain(IOptions<ProvidersOptions> opts) { ... }
```

- ✅ Tipado, com IntelliSense e validação no startup.
- ✅ Fail-fast: app não sobe se config estiver inválida.
- ✅ Padrão Microsoft `IOptions<T>` — argumento direto.
- ❌ Boilerplate da classe POCO + binding (~10 linhas por seção).

### Opção B — `IConfiguration` direto

```csharp
var url = config["Providers:ProviderAUrl"];
```

- ✅ Zero boilerplate.
- ❌ Sem tipagem, sem validação.
- ❌ Strings mágicas espalhadas.
- ❌ Não defensável em sênior.

## Sub-decisão 3 — Validação no startup

`ValidateDataAnnotations()` + `ValidateOnStart()` faz com que **se faltar configuração obrigatória, o app não sobe** — falha imediata, mensagem clara, em vez de bug silencioso em runtime.

Argumento de banca: *"configuração inválida é detectada no startup, não em produção. Fail-fast."*

## Sub-decisão 4 — Segredos

Este desafio **não tem segredos reais** (sem auth, sem DB, providers fake). Mas mostrar a abordagem certa é diferencial sênior:

| Tipo de segredo | Onde mora |
|---|---|
| Dev local | **User Secrets** (`dotnet user-secrets`) — fora do repo |
| CI/Production | **Variáveis de ambiente** ou cofre (Azure Key Vault, AWS Secrets Manager, HashiCorp Vault) |
| Repositório | **Nunca**. `.gitignore` cuida de `appsettings.*.local.json` e User Secrets ficam fora por design |

Para este desafio, registrar no README:

> "Não há segredos reais. Caso houvesse (ex: API keys de provider real), usaríamos User Secrets em dev e variáveis de ambiente / Key Vault em produção. `appsettings.*.json` no repo só contêm valores não-sensíveis."

## Sub-decisão 5 — Override por ambiente

Exemplo de `appsettings.json` (defaults, todos ambientes):

```json
{
  "Logging": {
    "LogLevel": { "Default": "Information" },
    "Format": "json"
  },
  "Providers": {
    "ProviderAUrl": "http://localhost:9001",
    "ProviderBUrl": "http://localhost:9002"
  },
  "Resilience": {
    "TotalTimeoutSeconds": 10,
    "PerAttemptTimeoutSeconds": 3,
    "RetryCount": 2,
    "RetryBaseDelayMs": 200,
    "CircuitBreakerFailures": 5,
    "CircuitBreakerWindowSeconds": 30,
    "CircuitBreakerBreakDurationSeconds": 30
  },
  "RequestLimits": {
    "MaxBodyBytes": 1048576
  }
}
```

### Reload de configuração — quais settings exigem restart

Mudar `appsettings.json` (ou variáveis de ambiente) **não exige rebuild** do binário/imagem — sempre que reiniciar o processo, os novos valores entram em vigor. Mas o **escopo do reload** depende de onde o setting é consumido:

| Seção | Hot reload? | Como aplicar |
|---|---|---|
| `Logging` (níveis e overrides do Serilog) | ✅ sim | reload automático ao salvar `appsettings.json` |
| `Resilience` (Polly/Http.Resilience) | ⚠️ não | restart obrigatório (pipeline construída uma vez no startup) |
| `Providers` (URLs dos provedores) | ⚠️ não | restart obrigatório (`HttpClient` é registrado uma vez) |
| `RequestLimits` (Kestrel) | ❌ não | restart obrigatório (Kestrel decide limites no startup, antes do DI) |

**Convenção**: ao alterar config em produção, o procedimento padrão é editar → restart do serviço. Hot reload é "bônus" para Logging, não regra geral. Documentado no README.

`appsettings.Development.json` (overrides para dev local):

```json
{
  "Logging": {
    "LogLevel": { "Default": "Debug" },
    "Format": "pretty"
  }
}
```

## Recomendação consolidada

1. **Estrutura**: `appsettings.json` + `appsettings.Development.json` + `appsettings.Production.json` (placeholder).
2. **Leitura**: `IOptions<T>` com classes POCO tipadas — `ProvidersOptions`, `ResilienceOptions`, `LoggingOptions`.
3. **Validação**: `ValidateDataAnnotations()` + `ValidateOnStart()` em todas as `IOptions`.
4. **Segredos**: documentar no README como segredos seriam tratados (User Secrets dev, env vars / Key Vault prod).
5. **Override por ambiente**: defaults em `appsettings.json`; logging mais verboso em Development; Production restritivo.

## Decisão

1. **Estrutura de arquivos**: `appsettings.json` (defaults para todos os ambientes) + `appsettings.Development.json` (dev local) + `appsettings.Production.json` (placeholder do desafio). Hierarquia padrão ASP.NET com override em cascata.
2. **Leitura**: `IOptions<T>` com classes POCO tipadas (`ProvidersOptions`, `ResilienceOptions`, `LoggingOptions`). DataAnnotations (`[Required]`, `[Url]`, `[Range]`) para validação.
3. **Validação no startup**: `ValidateDataAnnotations()` + `ValidateOnStart()` em todas as `IOptions` — app não sobe se config inválida (fail-fast).
4. **Segredos**: este desafio não tem segredos reais. Documentar no README a abordagem que usaríamos: User Secrets (`dotnet user-secrets`) em dev local; variáveis de ambiente / Key Vault / Secrets Manager em produção; `appsettings.*.json` no repo apenas com valores não-sensíveis.
5. **Override por ambiente**: defaults em `appsettings.json`; `Logging.LogLevel.Default=Debug` e `Logging.Format=pretty` em Development; restante (Production) herda defaults.

## Justificativa

1. **Hierarquia padrão ASP.NET** é reconhecida sem fricção pelo avaliador, e o override em cascata (`appsettings → {env} → env vars → command line`) cobre todos os cenários reais (dev, CI, container, cloud).
2. **`IOptions<T>` tipado** elimina strings mágicas espalhadas, dá IntelliSense, e habilita validação estruturada.
3. **`ValidateOnStart()`** é diferencial sênior: configuração inválida vira falha **no startup**, não bug silencioso em runtime. Em apresentação, é argumento direto: *"se eu tiver typo na config, o app falha imediatamente com mensagem clara — não chega em produção quebrado"*.
4. **Documentar segredos no README** mesmo sem segredos reais mostra que a equipe sabe operar pra produção. Defesa: *"não temos segredos neste desafio, mas registrei a abordagem (User Secrets em dev, variáveis de ambiente / Key Vault em prod) para deixar o caminho claro"*.
5. **Tudo configurável sem rebuild**: URLs de provider, timeouts Polly, formato de log — todos em JSON. Argumento operacional: *"ajuste em produção sem redeploy do binário"*.

---
