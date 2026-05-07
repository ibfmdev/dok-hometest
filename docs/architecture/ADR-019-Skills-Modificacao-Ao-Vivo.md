# ADR-019 — Skills de modificação ao vivo (Claude Code) para a banca

**Status:** aceito
**Data:** 2026-04-27

## Contexto

O item **9 do guia de apresentação** (`docs/APRESENTACAO.md` — *"Modificação ao vivo com IA"*) prevê que a banca peça mudanças no código durante a call. Os três cenários esperados são:

1. **Adicionar um Provider C** — mais um adapter HTTP encadeado no `DebtProviderChain`.
2. **Adicionar um tipo de débito novo** (ex: `LICENCIAMENTO`) — enum + mapper + `IInterestRule` + DI + testes.
3. **Mudar a taxa de juros** de uma rule existente — alterar uma constante.

Hoje o guia descreve os passos manuais. Em uma demo ao vivo, três fatores conspiram contra a execução manual:

- **Pressão de palco** aumenta a chance de erro humano (typo, esquecer de registrar o DI, esquecer de adicionar o teste).
- **Tempo limitado** na call: cada cenário tem que caber em ~2-3 minutos.
- **Visibilidade da capacidade sênior**: a banca quer ver não só *que* a IA modifica código, mas *como* a engenharia em torno dela é estruturada.

A pergunta arquitetural é: **devemos tratar essas modificações ao vivo como prompts ad-hoc digitados na hora, ou empacotá-las como artefatos versionados (Claude Code Skills) que vivem no repositório**?

Sub-decisões:

1. **Empacotar como skills vs. ad-hoc**: existência mesma das skills.
2. **Localização**: onde os arquivos ficam dentro do repo.
3. **Escopo das skills**: quantas, cobrindo o quê.
4. **Forma de coleta de parâmetros**: argumentos posicionais vs. perguntas estruturadas (`AskUserQuestion`).
5. **Política de segurança**: como evitar que a skill faça estrago num ambiente quente.
6. **Cobertura de testes**: como ensaiar antes da banca.

## Sub-decisão 1 — Empacotar como skills vs. ad-hoc

### Opção A — Skills versionadas no repo (recomendada)

Cada cenário do item 9 vira um arquivo `SKILL.md` em `.claude/skills/<nome>/` com instruções determinísticas (passos numerados, arquivos a editar, comandos de validação). Invocação: `/<nome>` na call.

- ✅ **Determinismo**: a skill executa os mesmos passos toda vez, removendo variabilidade do prompting ad-hoc.
- ✅ **Auditabilidade**: o "prompt" deixa de ser efêmero — vira artefato de código revisável e diffável.
- ✅ **Sinalização sênior**: mostra que a candidata trata *prompt engineering como engenharia* (versionado, testado, idempotente), não como improviso.
- ✅ **Treino antes da banca**: pode ser executada N vezes em branches descartáveis até estar 100% confiável.
- ❌ Custo inicial: ~3 arquivos `SKILL.md` + ensaio de cada um.

### Opção B — Prompt ad-hoc na hora

A candidata digita o pedido em linguagem natural ("adiciona um provider C que retorna JSON na URL X").

- ✅ Mostra a IA "raciocinando do zero" — pode parecer mais impressionante.
- ❌ **Não-determinismo**: se a IA esquecer um passo (ex: registrar o DI, atualizar o appsettings), o erro acontece em tela.
- ❌ Mais lento em média; mais frágil sob pressão.
- ❌ Perde a oportunidade de mostrar engenharia em torno do uso de IA.

### Opção C — Híbrido (skills disponíveis + ad-hoc se a banca pedir algo fora do escopo)

- ✅ Cobre o caso esperado com determinismo e o caso inesperado com flexibilidade.
- ✅ É o que vai acontecer naturalmente: skills cobrem os 3 cenários previstos; qualquer pedido fora deles é ad-hoc.
- ❌ Nenhuma desvantagem real além de exigir explicar a distinção em 1 frase.

## Sub-decisão 2 — Localização

### Opção A — `.claude/skills/<nome>/SKILL.md` (recomendada)

Skills de **escopo de projeto**: ficam versionadas com o código, todo mundo que clona o repo as enxerga.

- ✅ Versionamento e revisão com o resto do repo.
- ✅ Quem clonar o repositório e abrir no Claude Code recebe as skills automaticamente.
- ✅ Alinhado com `.claude/settings.local.json` que já existe no projeto.

### Opção B — `~/.claude/skills/` (escopo de usuário)

- ❌ Não vive com o repo. Se a banca quiser inspecionar depois, precisa da máquina da candidata.
- ❌ Invisível em revisão de código.

## Sub-decisão 3 — Escopo das skills

### Decisão proposta — exatamente as 3 skills do item 9

| Skill | Objetivo | Arquivos tocados | Duração esperada |
|---|---|---|---|
| `/add-provider` | Adicionar Provider C (ou D…) ao chain | `src/Dok.Infrastructure/Providers/Provider<X>Adapter.cs`, `src/Dok.Infrastructure/DependencyInjection.cs`, `src/Dok.Infrastructure/Options/ProvidersOptions.cs`, `src/Dok.Api/appsettings.json`, `docker-compose.yml`, `src/Dok.FakeProviders/data/` | ~90s |
| `/add-debt-type` | Adicionar novo `DebtType` com sua rule | `src/Dok.Domain/DebtType.cs`, `src/Dok.Domain/DebtTypeMapper.cs`, `src/Dok.Domain/Rules/<X>InterestRule.cs`, `src/Dok.Application/DependencyInjection.cs`, `tests/Dok.Domain.Tests/Rules/<X>InterestRuleTests.cs` | ~120s |
| `/change-interest-rate` | Mudar taxa diária ou cap de uma rule existente | `src/Dok.Domain/Rules/<X>InterestRule.cs`, eventualmente `tests/Dok.Domain.Tests/Rules/<X>InterestRuleTests.cs` | ~30s |

**Não cobrir** com skill (deixa ad-hoc):

- Mudanças de DTO de resposta — interferem no contrato literal da spec; risco alto de quebrar todos os testes; não vale empacotar.
- Mudanças no payment simulator — fórmulas matemáticas com dependências sutis (HALF_UP, monotonicidade); melhor revisar à mão.
- Mudanças em resilience/Polly — efeitos colaterais sistêmicos; revisar à mão.

A linha geral: **skills cobrem extensões aditivas em pontos do código já preparados como pontos de extensão** (Strategy, Adapter). Qualquer outra mudança fica ad-hoc.

## Sub-decisão 4 — Coleta de parâmetros

### Opção A — `AskUserQuestion` estruturado (recomendada)

A skill pergunta cada parâmetro com opções claras quando faz sentido (ex: formato JSON ou XML), e texto livre quando não (ex: nome do tipo de débito).

- ✅ Banca vê o fluxo guiado, sem ambiguidade.
- ✅ Reduz risco da candidata digitar errado ou esquecer um campo.
- ❌ Mais cliques na call (mas cada um é trivial).

### Opção B — Argumentos posicionais via `/skill arg1 arg2`

- ✅ Mais rápido se a candidata já sabe os parâmetros.
- ❌ Frágil (ordem importa) e invisível pra banca enquanto digita.

### Decisão da sub-decisão

Default `AskUserQuestion`. Aceitar overrides via argumentos quando óbvios (ex: `/change-interest-rate ipva 0.0040`).

## Sub-decisão 5 — Política de segurança e fluxo Git

### Decisão — fluxo Git completo + guardrails

Toda skill segue o **mesmo workflow Git** antes/depois das edições:

1. **Pré-flight (antes de qualquer edição)**:
   - Verifica que `git status --porcelain` está limpo. Se não, **aborta** com instrução pra commitar/stashar.
   - `git fetch origin main && git checkout main && git pull --ff-only origin main` — garante base atualizada.
   - Cria a feature branch: `git checkout -b feat/<skill-slug>-<param-slug>` (ex: `feat/add-provider-c`, `feat/add-debt-type-licenciamento`, `feat/change-interest-rate-ipva`).

2. **Edições + validação**:
   - Faz as alterações em arquivos.
   - Roda `dotnet build` (sem warning-as-error específico). Se falhar, **aborta sem commitar** e reporta o erro pro usuário.
   - Roda `dotnet test` no escopo afetado (ou suite inteira se for incerto). Se falhar, **aborta sem commitar** e reporta.

3. **Post-flight (após validação verde)**:
   - `git add` apenas dos arquivos esperados (lista explícita; nunca `git add -A`).
   - `git commit` com mensagem padronizada: `feat(<skill>): <descrição derivada dos parâmetros>` + Co-Authored-By Claude.
   - `git push -u origin feat/<skill-slug>-<param-slug>`.
   - `gh pr create --title "..." --body "..."` com título derivado dos parâmetros e body explicando o que mudou + checklist de teste manual.
   - Imprime a URL do PR no final.

4. **Escopo de arquivos restrito**: skills **nunca tocam** em `Directory.Build.props`, `Dok.slnx`, `Dockerfile`, `Makefile`, `.github/`, `docs/architecture/`, ou `.claude/`. Apenas em arquivos de domínio/aplicação/infra/api/testes/fakes/appsettings/docker-compose.

5. **Reversibilidade**: se a banca rejeitar a mudança, basta `gh pr close` + `git checkout main && git branch -D feat/...`. `main` permanece intocado.

## Sub-decisão 6 — Ensaio antes da banca

Cada skill precisa rodar **com sucesso 3× consecutivas** em branches descartáveis antes de ser considerada pronta. Critério de sucesso:

- `dotnet build` verde.
- `dotnet test` verde (dentro do escopo afetado).
- Mudança aparece de fato (`git diff --stat` lista os arquivos esperados).
- A skill produz resumo final dizendo o que mudou e onde.

O ensaio é parte da entrega. Branches de teste podem ser deletados depois; o que importa é a confiança operacional na call.

## Recomendação consolidada

1. **3 skills** em `.claude/skills/`: `add-provider`, `add-debt-type`, `change-interest-rate`.
2. **Coleta de parâmetros via `AskUserQuestion`** estruturado.
3. **Workflow Git completo**: feature branch a partir de `main` atualizado, validação build+test, commit, push, PR aberto via `gh pr create`. URL do PR é o output final visível pra banca.
4. **Escopo restrito** a pontos de extensão já preparados (Strategy/Adapter); resto fica ad-hoc.
5. **Cada skill ensaiada 3× consecutivas** antes de ser declarada pronta para a banca.
6. **Item 9 do `APRESENTACAO.md` reescrito** apontando para as skills, mantendo o passo-a-passo manual como apêndice/fallback caso a skill quebre.

## Decisão

1. **Adotar 3 skills de projeto** em `.claude/skills/{add-provider,add-debt-type,change-interest-rate}/SKILL.md`, cobrindo exatamente os 3 cenários do item 9 do `APRESENTACAO.md`.
2. **Coleta de parâmetros via `AskUserQuestion`** como default; argumentos posicionais aceitos como atalho quando óbvio.
3. **Workflow Git obrigatório** em toda skill:
   - **Pré-flight**: working tree limpo + `git fetch origin main && git checkout main && git pull --ff-only` + `git checkout -b feat/<skill>-<param>`.
   - **Validação**: `dotnet build` + `dotnet test` (ou subset relevante) verde antes de commit. Falha aborta sem commitar.
   - **Post-flight**: `git add <arquivos explícitos>` + commit padronizado + `git push -u origin <branch>` + `gh pr create` com título e body. URL do PR é o output final.
4. **Escopo de arquivos restrito**: skills **nunca tocam** em `Directory.Build.props`, `Dok.slnx`, `Dockerfile`, `Makefile`, `.github/`, `docs/architecture/`, ou `.claude/`.
5. **Ensaio**: cada skill deve rodar com sucesso 3× consecutivas em branches descartáveis antes de ser marcada como "pronta para banca". Os PRs do ensaio podem ser fechados sem merge.
6. **Item 9 do `APRESENTACAO.md`** é reescrito apontando para as skills, com o passo-a-passo manual mantido como fallback caso uma skill falhe ao vivo.
7. **Fora do escopo**: mudanças em DTO de resposta, payment simulator, resilience/Polly, ou outros pontos sem extension point preparado — esses ficam ad-hoc se a banca pedir.

## Justificativa

1. **Determinismo sob pressão** é o ganho principal: empacotar os passos remove a única coisa que pode dar errado em demo ao vivo — esquecer um arquivo. As skills passam por ensaio antes da banca; prompts ad-hoc não passam.
2. **Sinalização sênior**: tratar prompts como artefatos versionados, com guardrails e ensaio, é uma diferenciação real em 2026 — a maioria dos candidatos ainda usa IA como autocomplete improvisado. Prompt-as-code é a próxima inflexão.
3. **Alinhamento com a arquitetura existente**: as 3 skills só funcionam porque ADRs anteriores criaram pontos de extensão limpos — Adapter (ADR-004), Strategy (ADR-006/Domain), DI explícito (ADR-005/007). A skill **demonstra o ROI** dos ADRs anteriores em vez de competir com eles.
4. **Reversibilidade total + auditoria pública**: feature branch + PR garantem que (a) `main` permanece intocado se a banca rejeitar; (b) a banca tem **um link de PR** com diff revisável e descrição como evidência objetiva da mudança feita ao vivo, não apenas um diff local efêmero.
5. **Workflow real, não simulação**: a skill replica o fluxo profissional (branch → build → test → push → PR) que qualquer time de produção espera. Mostra que IA não é desculpa pra pular processo — pelo contrário, automatiza a parte chata do processo.
6. **Escopo defensivo**: limitar skills a pontos de extensão já desenhados evita o risco real de empacotar mudança em código sensível (resilience, contrato HTTP) onde o erro silencioso seria caro. Híbrido (skills + ad-hoc) cobre o resto.
7. **Mensagem para a banca**: *"O item 9 vocês podem disparar com `/add-provider`, `/add-debt-type` ou `/change-interest-rate`. Cada skill abre uma feature branch a partir da main, faz as edições, valida com build+test, e termina abrindo um PR no GitHub. No fim eu mando o link aqui pra vocês revisarem o diff. Se quiserem algo fora desses 3, eu disparo ad-hoc."*

## Limitação reconhecida

A skill `/change-interest-rate` ajusta a constante na rule **e** os testes que dependem dela (testes de regra são `value-coupled` à própria taxa, então alterar uma sem a outra deixa o build vermelho — desejável como tripwire). Consequência: se a IA gerar uma taxa numericamente errada (ex: `0.0040` em vez de `0.0033` solicitado), o build/test verde **não detecta** porque a skill atualizou os esperados em conjunto. Defesa final é o **review humano do PR** que a skill abre — por isso a skill termina abrindo PR via `gh pr create`, não fazendo merge. O link do PR é o output visível para a banca exatamente para essa inspeção.

Em produção, defesa adicional viria de um **golden-test layer** com valores literais blindados das skills (cenários da spec HomeTest §1: `1500/121dias → 1800.00`, `300.50/85dias → 555.93`) que residem em arquivo fora do escopo de edição da skill — caso a IA mude a taxa errada, esses testes quebram independentemente. Não está implementado nesse repo porque os testes de regra atuais já incluem os exemplos literais da spec; movê-los para camada blindada é trabalho mecânico se a estratégia for escalada para mais rules.

**Resposta pronta para a banca:** *"A skill é guardrail operacional, não verificador semântico. Se a banca pedir taxa 0,5% e a IA escrever 0,4%, build verde não captura porque a skill mexe nos próprios testes que validam a constante. Defesa final é review do PR — por isso a skill termina abrindo PR, não fazendo merge. Em produção eu adicionaria uma camada de golden tests blindada das skills com os valores literais da spec, fora do raio de edição da IA."*

## Status de implementação

- [x] `.claude/skills/add-provider/SKILL.md`
- [x] `.claude/skills/add-debt-type/SKILL.md`
- [x] `.claude/skills/change-interest-rate/SKILL.md`
- [x] Ensaio 3× para cada skill — refinamentos aplicados em commit `4acb22d` (*docs(skills): refine 3 skills with ensaio findings*).
- [x] `APRESENTACAO.md` item 9 reescrito — commit `2bf760b` (*docs(apresentacao): reframe item 9 — skills as rehearsed evidence, not live performance*).
- [x] `README.md` linka as skills na seção de decisões.
- [x] `PLANO-IMPLEMENTACAO.md` adiciona estágio dedicado.

---
