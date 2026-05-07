---
name: add-provider
description: Adiciona um novo provider de débitos (Provider C/D/...) à chain. Cria adapter HTTP, registra DI com Polly, atualiza ProvidersOptions, appsettings.json, docker-compose.yml e Dok.FakeProviders. Abre feature branch a partir de main, valida com build+test, dá commit/push e abre um PR via gh. Use quando a banca pedir "adiciona um Provider X" durante a apresentação. Definida em ADR-019.
---

# /add-provider — adicionar provider à chain

Skill do **item 9 da apresentação** (ADR-019). Adiciona um novo `IDebtProvider` ao `DebtProviderChain` reaproveitando o padrão Adapter já existente (A=JSON, B=XML).

## Pré-requisitos rígidos (abortar se não atendidos)

1. **`git status --porcelain` deve ser vazio.** Se houver mudanças locais, **pare** e instrua: *"Working tree não está limpo. Commit ou stash antes de rodar `/add-provider`."*
2. **Diretório de trabalho** = raiz do repo (`/home/iberefm/ibfm/dok`). Verificar com `git rev-parse --show-toplevel`.
3. **`gh auth status`** deve estar autenticado. Se não, abortar e instruir: *"`gh` não autenticado. Rode `gh auth login` antes."*

Se algum pré-requisito falhar, **não faça nenhuma edição**.

## Coleta de parâmetros (use AskUserQuestion)

Pergunte os 4 parâmetros via `AskUserQuestion` numa única chamada (4 questions), com opções estruturadas onde fizer sentido:

1. **`letter`** — letra do provider, com opções `C`, `D`, `E`, `F` (default: `C`). Texto livre se quiser outra letra.
2. **`format`** — formato da resposta, com opções `JSON` e `XML`.
3. **`base_url`** — URL base do provider (texto livre). Sugira `http://localhost:900<N>` onde N = índice da letra (C→3, D→4, E→5, F→6). Exemplo: para C, sugerir `http://localhost:9003`.
4. **`fake_port`** — porta interna do container fake (texto livre, mesmo número da URL). Default = mesma porta da URL.

Derive os slugs:
- `letter_lower` = `letter` em minúscula (ex: `c`)
- `letter_upper` = `letter` em maiúscula (ex: `C`)
- Branch name = `feat/add-provider-<letter_lower>`

## Pré-flight Git

Execute em sequência (cada um precisa passar antes do próximo):

```bash
git fetch origin main
git checkout main
git pull --ff-only origin main
git checkout -b feat/add-provider-<letter_lower>
```

Se qualquer passo falhar, **aborte e reporte o erro literal**. Não tente "consertar" automaticamente.

## Edições — exatamente estes arquivos, nesta ordem

### 1. `src/Dok.Infrastructure/Providers/Provider<letter_upper><Format>Adapter.cs` (NOVO)

- Se `format == JSON`: copie a estrutura de `ProviderAJsonAdapter.cs`. Substitua `ProviderA` → `Provider<letter_upper>`, `ProviderAJsonAdapter` → `Provider<letter_upper>JsonAdapter`, `Name => "ProviderA"` → `Name => "Provider<letter_upper>"`. **Mantenha o helper `EnsureJsonContentType` e a chamada a ele** — é a defesa contra provider retornando 200 com payload de outro tipo (HTML de erro com Content-Type errado, etc.); cobre o caso "200 com lixo" disparando fallback.
- Se `format == XML`: copie a estrutura de `ProviderBXmlAdapter.cs` com substituições análogas (`ProviderB` → `Provider<letter_upper>`). **Mantenha o helper `EnsureXmlContentType`** pelo mesmo motivo.

### 2. `src/Dok.Infrastructure/Options/ProvidersOptions.cs` (EDIT)

Adicione, após `ProviderBUrl`:

```csharp
[Required, Url]
public string Provider<letter_upper>Url { get; init; } = string.Empty;
```

### 3. `src/Dok.Infrastructure/DependencyInjection.cs` (EDIT)

Após o bloco do `ProviderBXmlAdapter`, adicione bloco análogo para o novo adapter (use `Provider<letter_upper>JsonAdapter` ou `Provider<letter_upper>XmlAdapter` dependendo do formato):

```csharp
services.AddHttpClient<Provider<letter_upper><Format>Adapter>()
    .ConfigureHttpClient((sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<ProvidersOptions>>().Value;
        client.BaseAddress = new Uri(opts.Provider<letter_upper>Url);
    })
    .AddStandardResilienceHandler(o => ApplyResilience(o, resilience));
```

E após o último `services.AddTransient<IDebtProvider>(...)`, adicione:

```csharp
services.AddTransient<IDebtProvider>(sp => sp.GetRequiredService<Provider<letter_upper><Format>Adapter>());
```

A ordem de registro define a ordem do fallback — o novo provider entra **depois** dos existentes (último na chain).

### 4. `src/Dok.Api/appsettings.json` (EDIT)

Na seção `"Providers"`, adicione a entrada:

```json
"Provider<letter_upper>Url": "<base_url>"
```

### 5. `docker-compose.yml` (EDIT)

Após o bloco `provider-b:`, adicione service análogo:

```yaml
  provider-<letter_lower>:
    build:
      context: .
      dockerfile: src/Dok.FakeProviders/Dockerfile
    container_name: dok-provider-<letter_lower>
    environment:
      Provider__Name: Provider<letter_upper>
      Provider__Port: "<fake_port>"
      Provider__DataFile: data/provider<letter_upper>.<json|xml>
      Provider__ContentType: application/<json|xml>
    ports: ["<fake_port>:<fake_port>"]
```

E na seção `api:`, adicione:
- `Providers__Provider<letter_upper>Url: http://provider-<letter_lower>:<fake_port>` (em `environment:`)
- `provider-<letter_lower>` na lista `depends_on:`

### 6. `src/Dok.FakeProviders/data/provider<letter_upper>.<json|xml>` (NOVO)

Copie o conteúdo de `providerA.json` (se JSON) ou `providerB.xml` (se XML). Mantenha a placa `ABC1234` e os mesmos débitos — assim o fake responde igual aos outros.

### 7. `tests/Dok.Integration.Tests/WireMockApiFactory.cs` (EDIT — **obrigatório**)

`ProvidersOptions.Provider<letter_upper>Url` é `[Required, Url]` + `ValidateOnStart`, então o `WebApplicationFactory<Program>` **falha ao bootar** se o config in-memory não tiver a URL. Sem esse passo, todos os integration tests quebram.

Edite assim:

1. Adicione propriedade `WireMockServer Provider<letter_upper> { get; }` ao lado dos `ProviderA`/`ProviderB` existentes; inicialize no construtor com `WireMockServer.Start()`.
2. No `ConfigureWebHost → ConfigureAppConfiguration`, adicione:
   ```csharp
   ["Providers:Provider<letter_upper>Url"] = Provider<letter_upper>.Url,
   ```
3. No `Dispose(bool disposing)`, dentro do bloco `if (disposing)`, adicione:
   ```csharp
   Provider<letter_upper>.Stop();
   Provider<letter_upper>.Dispose();
   ```
4. No `ResetMocks()`, adicione `Provider<letter_upper>.Reset();`.
5. **Não** crie helpers `StubProvider<letter_upper>` — testes existentes não cobrem o novo provider, então ele fica sem stub e responde 404 a qualquer request, o que cai no `IsProviderFailure(HttpRequestException)` e dispara fallback. Isso é OK: testes existentes passam porque ProviderA/B respondem antes de a chain chegar nele.

## Validação obrigatória (antes de commitar)

```bash
dotnet build
```

Se falhar: **NÃO commite**. Reporte o erro pro usuário e pare. Diga: *"Build falhou. Reveja o output acima. Branch `feat/add-provider-<letter_lower>` permanece com as mudanças mas sem commit."*

```bash
dotnet test
```

> Rode a suite completa. Integration tests **devem continuar verdes** porque (a) o passo 7 configurou o `WireMockServer` do novo provider em `WireMockApiFactory`, garantindo que `ValidateOnStart` da `ProvidersOptions` aceite o boot; e (b) o `DebtProviderChain` ignora providers que não respondem se algum anterior já respondeu (ProviderA/B continuam servindo as respostas reais). Se a suite quebrar com mensagem `OptionsValidationException` ou `[Required, Url]`, é sintoma de o passo 7 ter sido pulado — volte e corrija o `WireMockApiFactory`.

Se testes falharem: **NÃO commite**. Reporte e pare.

## Post-flight Git

Apenas se build + tests passaram:

```bash
git add \
  src/Dok.Infrastructure/Providers/Provider<letter_upper><Format>Adapter.cs \
  src/Dok.Infrastructure/Options/ProvidersOptions.cs \
  src/Dok.Infrastructure/DependencyInjection.cs \
  src/Dok.Api/appsettings.json \
  docker-compose.yml \
  src/Dok.FakeProviders/data/provider<letter_upper>.<json|xml> \
  tests/Dok.Integration.Tests/WireMockApiFactory.cs
```

(Liste os 7 arquivos exatos. Não use `git add -A`.)

```bash
git commit -m "$(cat <<'EOF'
feat(provider): add Provider<letter_upper> (<format>) to debt provider chain

Adds a new IDebtProvider implementation following the existing Adapter
pattern (ADR-004). Registered last in the chain so existing fallback
order (A → B → <letter_upper>) is preserved.

- New adapter: Provider<letter_upper><Format>Adapter
- ProvidersOptions.Provider<letter_upper>Url
- DI registration with isolated Polly pipeline (ADR-009)
- docker-compose service provider-<letter_lower>
- Fake data file mirroring providerA/B sample

Generated by /add-provider skill (ADR-019).

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

```bash
git push -u origin feat/add-provider-<letter_lower>
```

```bash
gh pr create --title "feat: add Provider<letter_upper> (<format>) to chain" --body "$(cat <<'EOF'
## Summary
- New `IDebtProvider` adapter `Provider<letter_upper><Format>Adapter` following the established Adapter pattern (ADR-004)
- Registered last in `DebtProviderChain` so the existing fallback order (A → B → <letter_upper>) is preserved
- Polly resilience pipeline isolated per-client (ADR-009)
- `docker-compose` service `provider-<letter_lower>` on port `<fake_port>` mirroring the existing fake structure

## Generated by
`/add-provider` skill — ADR-019. Live modification during the HomeTest presentation.

## Test plan
- [ ] `dotnet build` (already green, gating commit)
- [ ] `dotnet test` (Domain + Application green, gating commit)
- [ ] `docker compose up --build` and verify `provider-<letter_lower>` container starts
- [ ] `curl -i -X POST http://localhost:8080/api/v1/debitos -H 'Content-Type: application/json' -d '{"placa":"ABC1234"}'` — header `X-Dok-Provider` should still show `ProviderA` (chain order preserved)
- [ ] `docker compose stop provider-a provider-b` then re-run curl — header should now show `Provider<letter_upper>` (new provider serves as last-resort fallback)

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

## Output final

Imprima a URL do PR retornada pelo `gh pr create` em uma linha clara, formato:

```
✅ PR aberto: <url>
```

E uma linha resumo: *"Provider<letter_upper> adicionado à chain como último fallback. Arquivos modificados: 6. Build e tests verdes. Mande o link acima pra banca."*

## Em caso de erro em qualquer ponto

- **Não faça `git checkout main` automaticamente** — o usuário pode querer inspecionar o estado.
- **Reporte exatamente o que falhou** e em qual passo.
- Se já criou a branch mas falhou nos commits, instrua: *"Para descartar: `git checkout main && git branch -D feat/add-provider-<letter_lower>`."*
