---
name: add-debt-type
description: Adiciona um novo tipo de débito (ex: LICENCIAMENTO) ao domínio. Estende o enum DebtType, atualiza DebtTypeMapper, cria uma nova IInterestRule (com taxa diária e cap opcional), registra no DI da Application e gera testes em Dok.Domain.Tests. Abre feature branch a partir de main, valida com build+test, dá commit/push e abre um PR via gh. Use quando a banca pedir "adiciona o tipo X" durante a apresentação. Definida em ADR-019.
---

# /add-debt-type — adicionar tipo de débito

Skill do **item 9 da apresentação** (ADR-019). Adiciona um novo `DebtType` ao domínio reaproveitando o padrão Strategy do `IInterestRule`.

## Pré-requisitos rígidos (abortar se não atendidos)

1. **`git status --porcelain` deve ser vazio.** Se houver mudanças locais, **pare** e instrua: *"Working tree não está limpo. Commit ou stash antes de rodar `/add-debt-type`."*
2. **Diretório de trabalho** = raiz do repo (`/home/iberefm/ibfm/dok`). Verificar com `git rev-parse --show-toplevel`.
3. **`gh auth status`** deve estar autenticado. Se não, abortar e instruir: *"`gh` não autenticado. Rode `gh auth login` antes."*

Se algum pré-requisito falhar, **não faça nenhuma edição**.

## Coleta de parâmetros (use AskUserQuestion)

Pergunte os 3 parâmetros via `AskUserQuestion` numa única chamada:

1. **`type_name`** — nome do tipo em maiúsculas (texto livre). Ex: `LICENCIAMENTO`, `DPVAT`, `IPTU`. Será usado tanto no enum (PascalCase) quanto na string wire (UPPER).
2. **`daily_rate_pct`** — taxa diária em **percentual** (texto livre). Ex: `0.5` significa 0,5% ao dia. Default sugerido: `0.33` (igual ao IPVA).
3. **`cap_pct`** — teto opcional de juros, também em percentual sobre o valor original (texto livre, aceitar vazio ou `0` para "sem cap"). Ex: `20` significa cap de 20%. Default sugerido: vazio (sem cap, comporta como Multa).

Derive os slugs:
- `type_pascal` = `type_name` em PascalCase (ex: `LICENCIAMENTO` → `Licenciamento`, `DPVAT` → `Dpvat`)
- `type_lower` = `type_name` em minúsculas (ex: `licenciamento`)
- `type_upper` = `type_name` em maiúsculas (já vem assim; reforçar)
- `daily_rate_decimal` = `daily_rate_pct / 100` formatado com `m` no fim. Ex: `0.5` → `0.0050m`; `0.33` → `0.0033m`
- `cap_decimal` = `cap_pct / 100` formatado com `m`. Ex: `20` → `0.20m`. Se vazio/zero: sem cap.
- Branch name = `feat/add-debt-type-<type_lower>`

## Pré-flight Git

```bash
git fetch origin main
git checkout main
git pull --ff-only origin main
git checkout -b feat/add-debt-type-<type_lower>
```

Se qualquer passo falhar, **aborte e reporte o erro literal**. Não tente "consertar".

## Edições — exatamente estes arquivos, nesta ordem

### 1. `src/Dok.Domain/DebtType.cs` (EDIT)

Adicione `<type_pascal>,` ao enum, mantendo a vírgula final:

```csharp
public enum DebtType
{
    Ipva,
    Multa,
    <type_pascal>,
}
```

### 2. `src/Dok.Domain/DebtTypeMapper.cs` (EDIT)

No switch do `Parse`, adicione um case **antes** dos catches de erro:

```csharp
"<type_upper>" => DebtType.<type_pascal>,
```

No switch do `ToWire`, adicione **antes** do default:

```csharp
DebtType.<type_pascal> => "<type_upper>",
```

### 3. `src/Dok.Domain/Rules/<type_pascal>InterestRule.cs` (NOVO)

> **Convenção de nomes:** `IpvaInterestRule` e `MultaInterestRule` na main usam `DailyInterestRate` e `InterestCapRatio` (com XML doc citando a spec). Mantenha **os mesmos nomes e o mesmo padrão de XML doc** na nova rule pra preservar consistência — o nome `DailyRate`/`Cap` antigo foi descontinuado.

**Sem cap** (cap_decimal vazio/zero) — modele como `MultaInterestRule`:

```csharp
namespace Dok.Domain.Rules;

public sealed class <type_pascal>InterestRule : IInterestRule
{
    /// <summary><daily_rate_pct>% ao dia, sem teto, conforme parâmetros de <type_upper>.</summary>
    private const decimal DailyInterestRate = <daily_rate_decimal>;

    public DebtType Type => DebtType.<type_pascal>;

    public UpdatedDebt Apply(Debt debt, DateOnly today)
    {
        var days = today.DayNumber - debt.DueDate.DayNumber;
        if (days <= 0)
            return new UpdatedDebt(debt.Type, debt.OriginalAmount, debt.OriginalAmount, debt.DueDate, 0);

        var interest = debt.OriginalAmount.Value * DailyInterestRate * days;
        var updated = Money.Of(debt.OriginalAmount.Value + interest);
        return new UpdatedDebt(debt.Type, debt.OriginalAmount, updated, debt.DueDate, days);
    }
}
```

**Com cap** — modele como `IpvaInterestRule`:

```csharp
namespace Dok.Domain.Rules;

public sealed class <type_pascal>InterestRule : IInterestRule
{
    /// <summary><daily_rate_pct>% ao dia conforme parâmetros de <type_upper>.</summary>
    private const decimal DailyInterestRate = <daily_rate_decimal>;

    /// <summary><cap_pct>% do valor original aplicado ao <em>valor de juros</em>, não ao total.</summary>
    private const decimal InterestCapRatio = <cap_decimal>;

    public DebtType Type => DebtType.<type_pascal>;

    public UpdatedDebt Apply(Debt debt, DateOnly today)
    {
        var days = today.DayNumber - debt.DueDate.DayNumber;
        if (days <= 0)
            return new UpdatedDebt(debt.Type, debt.OriginalAmount, debt.OriginalAmount, debt.DueDate, 0);

        var raw = debt.OriginalAmount.Value * DailyInterestRate * days;
        var capValue = debt.OriginalAmount.Value * InterestCapRatio;
        var interest = Math.Min(raw, capValue);
        var updated = Money.Of(debt.OriginalAmount.Value + interest);
        return new UpdatedDebt(debt.Type, debt.OriginalAmount, updated, debt.DueDate, days);
    }
}
```

### 4. `src/Dok.Application/DependencyInjection.cs` (EDIT)

Após as linhas `AddSingleton<IInterestRule, ...>` existentes, adicione:

```csharp
services.AddSingleton<IInterestRule, <type_pascal>InterestRule>();
```

(O dicionário `IReadOnlyDictionary<DebtType, IInterestRule>` já é construído via `GetServices<IInterestRule>().ToDictionary(r => r.Type)`, então não precisa tocá-lo.)

### 5. `tests/Dok.Domain.Tests/DebtTypeMapperTests.cs` (EDIT — pode ou não ser necessário)

Esse arquivo tem 3 `[Theory]` que **podem** colidir com o novo tipo. Verifique cada uma:

1. **`Parse_with_known_returns_enum`** — adicione duas `[InlineData("<type_upper>", DebtType.<type_pascal>)]` (uma maiúscula, uma minúscula `"<type_lower>"`) para o novo tipo passar pelo round-trip.
2. **`Parse_with_unknown_throws`** — se houver `[InlineData("<type_upper>")]` listada como exemplo de tipo desconhecido (ex: `LICENCIAMENTO`, `DPVAT`), **remova** essa linha. Senão o teste vai falhar porque o tipo agora é conhecido.
3. **`ToWire_returns_uppercase`** — adicione `[InlineData(DebtType.<type_pascal>, "<type_upper>")]` para cobrir o lado oposto.

> Sem esse passo, `Parse_with_unknown_throws("<type_upper>")` quebra. Achado capturado durante o ensaio.

### 6. `tests/Dok.Domain.Tests/<type_pascal>InterestRuleTests.cs` (NOVO)

Estrutura mínima espelhando `IpvaInterestRuleTests` (com cap) ou `MultaInterestRuleTests` (sem cap). 4 testes:

- `Type_is_<type_pascal>` — confirma `_rule.Type == DebtType.<type_pascal>`.
- `Apply_when_not_overdue_returns_original` — débito futuro retorna valor original com `DaysOverdue == 0`.
- `Apply_when_due_today_returns_original` — débito vencendo hoje retorna valor original.
- `Apply_with_overdue_applies_daily_rate` — calcula juros simples manualmente (`base × daily_rate × days`) e compara. Se houver cap e os juros excederem o cap, o teste deve usar uma quantidade de dias que **não atinge** o cap (caso "abaixo do teto") — adicione um segundo teste `Apply_when_exceeding_cap_uses_cap_value` se for o caso com cap.

Use a mesma data de referência (`new DateOnly(2024, 5, 10)`) e padrão de assertions com Shouldly (`result.UpdatedAmount.Value.ShouldBe(...m)`).

## Validação obrigatória (antes de commitar)

```bash
dotnet build
```

Se falhar: **NÃO commite**. Reporte o erro pro usuário e pare.

```bash
dotnet test --filter "FullyQualifiedName~Domain.Tests|FullyQualifiedName~Application.Tests"
```

Se testes falharem: **NÃO commite**. Reporte e pare.

> Nota: integration tests não cobrem o novo tipo (precisariam de um WireMock customizado). Isso é esperado.

## Post-flight Git

```bash
git add \
  src/Dok.Domain/DebtType.cs \
  src/Dok.Domain/DebtTypeMapper.cs \
  src/Dok.Domain/Rules/<type_pascal>InterestRule.cs \
  src/Dok.Application/DependencyInjection.cs \
  tests/Dok.Domain.Tests/<type_pascal>InterestRuleTests.cs \
  tests/Dok.Domain.Tests/DebtTypeMapperTests.cs   # se foi tocado no passo 5
```

(Liste os arquivos exatos. Não use `git add -A`. Inclua `DebtTypeMapperTests.cs` apenas se foi modificado.)

```bash
git commit -m "$(cat <<'EOF'
feat(domain): add <type_upper> debt type with interest rule

Extends DebtType enum and adds <type_pascal>InterestRule following
the existing Strategy pattern (ADR-006). Daily rate <daily_rate_pct>%
[, cap <cap_pct>% applied / no cap].

- Enum entry DebtType.<type_pascal>
- DebtTypeMapper Parse/ToWire round-trip via "<type_upper>"
- <type_pascal>InterestRule registered as IInterestRule (DI auto-discovery)
- Domain.Tests covering Type identity, no-overdue path, and overdue calculation

Generated by /add-debt-type skill (ADR-019).

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

```bash
git push -u origin feat/add-debt-type-<type_lower>
```

```bash
gh pr create --title "feat: add <type_upper> debt type" --body "$(cat <<'EOF'
## Summary
- New `DebtType.<type_pascal>` enum entry
- `DebtTypeMapper` Parse/ToWire round-trip via `"<type_upper>"`
- New `<type_pascal>InterestRule : IInterestRule` (Strategy pattern, ADR-006) with daily rate **<daily_rate_pct>%**[, cap **<cap_pct>%** / no cap]
- Auto-discovered by the DI dictionary `IReadOnlyDictionary<DebtType, IInterestRule>` — no orchestration code touched
- New unit tests in `Dok.Domain.Tests` covering identity, no-overdue, and overdue calculation paths

## Generated by
`/add-debt-type` skill — ADR-019. Live modification during the HomeTest presentation.

## Test plan
- [ ] `dotnet build` (gating commit, already green)
- [ ] `dotnet test` Domain + Application (gating commit, already green)
- [ ] Optional: configure a fake provider response with `"<type_upper>"` and observe the new rule applied end-to-end
- [ ] Optional: configure provider with a typo (`<type_upper>X`) and confirm 422 still triggers (mapper rejects unknown types)

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

## Output final

```
✅ PR aberto: <url>
```

Resumo: *"Tipo `<type_upper>` adicionado ao domínio. 5 arquivos modificados. Build e tests verdes. Mande o link acima pra banca."*

## Em caso de erro em qualquer ponto

- Reporte exatamente o que falhou e em qual passo.
- Se já criou a branch mas falhou: *"Para descartar: `git checkout main && git branch -D feat/add-debt-type-<type_lower>`."*
