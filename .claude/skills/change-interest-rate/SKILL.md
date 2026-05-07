---
name: change-interest-rate
description: Altera a taxa diária de juros (e/ou o cap) de uma rule existente — IpvaInterestRule ou MultaInterestRule. Edita a constante na rule e ajusta os testes que dependem dela. Abre feature branch a partir de main, valida com build+test, dá commit/push e abre um PR via gh. Use quando a banca pedir "muda a taxa do IPVA" ou similar durante a apresentação. Definida em ADR-019.
---

# /change-interest-rate — alterar taxa de juros

Skill do **item 9 da apresentação** (ADR-019). Edita uma constante numérica numa `IInterestRule` existente e atualiza os testes que dependem dela.

## Pré-requisitos rígidos (abortar se não atendidos)

1. **`git status --porcelain` deve ser vazio.**
2. **Diretório de trabalho** = raiz do repo (`/home/iberefm/ibfm/dok`).
3. **`gh auth status`** autenticado.

Se algum falhar, **não faça nenhuma edição**. Reporte e pare.

## Coleta de parâmetros (use AskUserQuestion)

Pergunte os 3 parâmetros via `AskUserQuestion` numa única chamada:

1. **`rule`** — qual rule alterar. Opções fixas: `ipva`, `multa`. (Se a banca pedir uma rule criada pela skill `/add-debt-type` na mesma sessão, instrua o usuário a editar manualmente — esta skill cobre apenas as 2 originais para evitar adivinhação de path.)
2. **`field`** — qual constante alterar. Opções: `daily_rate` (taxa diária) e `cap` (apenas se `rule == ipva`; multa não tem cap). Se rule==multa e o usuário pedir cap, abortar com instrução clara.
3. **`new_value_pct`** — novo valor em **percentual** (texto livre). Ex: `0.5` → 0,5% (vira `0.0050m`); `25` para cap → 25% (vira `0.25m`).

Derive:
- `rule_class` = `IpvaInterestRule` ou `MultaInterestRule`
- `tests_class` = `IpvaInterestRuleTests` ou `MultaInterestRuleTests`
- `field_const` — nome **exato** da constante na rule:
  - `field == daily_rate` → `DailyInterestRate`
  - `field == cap` (válido apenas em `IpvaInterestRule`) → `InterestCapRatio`
  > As rules na main usam esses nomes pra rastrear origem na spec; o nome antigo (`DailyRate`/`Cap`) foi descontinuado.
- `new_value_decimal` = `new_value_pct / 100` formatado com `m`. Ex: `0.5` → `0.0050m`; `25` → `0.25m`
- Branch name = `feat/change-interest-rate-<rule>-<field>` (ex: `feat/change-interest-rate-ipva-daily_rate`)

## Pré-flight Git

```bash
git fetch origin main
git checkout main
git pull --ff-only origin main
git checkout -b feat/change-interest-rate-<rule>-<field>
```

## Edições — exatamente estes arquivos

### 1. `src/Dok.Domain/Rules/<rule_class>.cs` (EDIT)

Localize a linha da constante e substitua o valor:

```csharp
private const decimal <field_const> = <novo_valor_decimal>;
```

(Mantenha o tipo, a visibilidade e o sufixo `m` exatamente como estavam. Apenas o número muda.)

### 2. `tests/Dok.Domain.Tests/<tests_class>.cs` (EDIT)

**Atenção crítica**: os testes existentes contêm valores **calculados** com a taxa antiga, em comentários e em assertions `ShouldBe(...m)`. Mudar a taxa **vai quebrar** esses testes se eles não forem atualizados.

Estratégia: para cada teste que valida um valor numérico calculado a partir da taxa, **recalcule o valor esperado** usando a nova taxa e atualize:
- O `ShouldBe(...m)` literal.
- O comentário acima do teste (se mencionar o cálculo).

Se a fórmula for ambígua ou o teste validar caso de borda (`days <= 0` retorna original, sem dependência da taxa), **não toque** — só atualize o que depende numericamente do `field_const` que mudou.

Use a fórmula correta:

- **Para `DailyInterestRate` em `IpvaInterestRule`**: `interest = min(base × new_rate × days, base × InterestCapRatio)`. Se a quantidade de dias do teste fizer atingir o cap (`base × new_rate × days > base × InterestCapRatio`), o teste passa como antes — não muda o valor esperado.
- **Para `InterestCapRatio` em `IpvaInterestRule`**: `interest = min(base × DailyInterestRate × days, base × new_cap)`. Atualize valores onde o cap virava o limite.
- **Para `DailyInterestRate` em `MultaInterestRule`**: `interest = base × new_rate × days`. Sem cap.

Aplique HALF_UP (arredondamento `MidpointRounding.AwayFromZero` em 2 casas decimais) ao valor final via `Money.Of(...)` — replica o que a rule faz em produção. Resultado em `result.UpdatedAmount.Value.ShouldBe(<valor>m)`.

Se você não tem certeza absoluta de algum valor recalculado, **não chute**. Em vez disso, deixe o teste sem atualização, deixe o build/test rodar, e o resultado do `dotnet test` mostrará o esperado vs obtido — daí ajuste com o número exato.

## Validação obrigatória (antes de commitar)

```bash
dotnet build
```

Se falhar: **NÃO commite**.

```bash
dotnet test
```

> Rode a suite **completa** (Domain + Application + Integration), não só Domain. Mudanças de constantes podem cascatar para Application e Integration tests que validam exemplos numéricos da spec (ex: `Apply_with_121_days_overdue → 1800.00`). Achado capturado durante o ensaio.

Se falhar: **NÃO commite**. Reveja os valores recalculados — provavelmente é cálculo desatualizado em algum teste. Use o output do `dotnet test` (mostra `Expected: X but was: Y`) para corrigir os literais e rode de novo. Inclua na lista de `git add` qualquer arquivo de teste adicional que tenha sido modificado.

> Faça **no máximo 3 iterações** de ajuste. Se ainda não passar, abortar e instruir o usuário a investigar manualmente.

## Post-flight Git

```bash
git add \
  src/Dok.Domain/Rules/<rule_class>.cs \
  tests/Dok.Domain.Tests/<tests_class>.cs
# Inclua também qualquer outro arquivo de teste (Application/Integration) que tenha
# sido ajustado durante a iteração de validação acima. Ex:
#   tests/Dok.Application.Tests/DebtsCalculatorTests.cs
#   tests/Dok.Integration.Tests/DebtsApiTests.cs
```

```bash
git commit -m "$(cat <<'EOF'
feat(domain): change <rule_class> <field_const> to <new_value_pct>%

Updates the <field_const> constant in <rule_class> from the previous
value to <new_value_decimal> (= <new_value_pct>%). Test expectations
recalculated to match the new rate.

Generated by /change-interest-rate skill (ADR-019).

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

```bash
git push -u origin feat/change-interest-rate-<rule>-<field>
```

```bash
gh pr create --title "feat: change <rule_class>.<field_const> to <new_value_pct>%" --body "$(cat <<'EOF'
## Summary
- `<rule_class>.<field_const>` updated to `<new_value_decimal>` (= **<new_value_pct>%**)
- Test expectations in `<tests_class>` recalculated to match the new rate

## Generated by
`/change-interest-rate` skill — ADR-019. Live modification during the HomeTest presentation.

## Test plan
- [ ] `dotnet build` (gating commit, already green)
- [ ] `dotnet test` Domain (gating commit, already green)
- [ ] Optional: re-run integration `curl` to observe the new rate applied end-to-end

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

## Output final

```
✅ PR aberto: <url>
```

Resumo: *"`<rule_class>.<field_const>` agora é `<new_value_decimal>` (`<new_value_pct>%`). 2 arquivos modificados. Build e tests verdes. Mande o link acima pra banca."*

## Em caso de erro

- Reporte exatamente onde falhou.
- Se já criou a branch: *"Para descartar: `git checkout main && git branch -D feat/change-interest-rate-<rule>-<field>`."*
