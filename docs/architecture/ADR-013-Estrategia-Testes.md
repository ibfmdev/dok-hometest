# ADR-013 — Estratégia de testes: framework, asserções, mocks, integração, property-based

**Status:** aceito
**Data:** 2026-04-27

## Contexto

O enunciado pede testes automatizados como "seria bacana se" e cita explicitamente:

- Unitários (regras de juros, simulador de pagamento, VOs).
- Integração para o **fallback** (provedor A falha → B responde).
- Cenários de erro (timeout, indisponibilidade, payload inválido, tipo desconhecido, placa fora do padrão).

Decisões a tomar:

1. **Test framework**: xUnit vs NUnit vs MSTest.
2. **Asserções**: FluentAssertions vs Shouldly vs `Assert` nativo do framework.
3. **Mocks**: Moq vs NSubstitute vs FakeItEasy vs **fakes manuais**.
4. **Test data**: AutoFixture vs Bogus vs builders manuais.
5. **HTTP fakes**: WireMock.Net (já decidido em ADR-008 para integração).
6. **Property-based testing**: FsCheck/Hedgehog vs ignorar.
7. **Cobertura**: Coverlet vs ignorar.
8. **Estrutura dos projetos de teste**: como dividir testes unitários, de Application, de integração — em linha com ADR-007 (3 projetos de testes propostos).

## Camada 1 — Test framework

### Opção A — xUnit (recomendada)

Padrão de fato em .NET moderno (ASP.NET, .NET runtime, e a maioria das libs Microsoft usam xUnit).

- ✅ Mais usado na comunidade .NET — sinal padrão para sênior.
- ✅ Sintaxe enxuta: classes por suite, sem `[TestFixture]`/`[SetUp]`. Constructor é setup, `IDisposable` ou `IAsyncLifetime` é teardown.
- ✅ Bom suporte para teorias parametrizadas (`[Theory]` + `[InlineData]` / `[MemberData]`).
- ❌ Algumas convenções "diferentes" (sem `[TestInitialize]`) confundem quem vem de NUnit/MSTest.

### Opção B — NUnit

Tradicional, ainda popular em times .NET veteranos.

- ✅ API rica, atributos para virtualmente tudo.
- ✅ Setup/teardown explícitos (`[SetUp]`, `[TearDown]`, `[OneTimeSetUp]`).
- ❌ Menos idiomático em projetos modernos Microsoft.

### Opção C — MSTest

Default histórico do Visual Studio. Em 2026, considerado legado em projetos novos.

- ❌ Comunidade menor, evolução mais lenta. Sem motivo para escolher em projeto novo.

## Camada 2 — Asserções

### Contexto adicional (mudança de cenário em 2024)

Em julho de 2024, o **FluentAssertions** (lib historicamente dominante em .NET) lançou a versão 8 e mudou para uma **licença comercial paga**. As versões 7.x permanecem sob Apache 2.0, mas ficam congeladas — sem patches futuros nem evolução. Em 2026, **adotar FA 7.x em projeto novo é apostar em uma lib morta em prazo médio**: compatibilidade com versões futuras do .NET pode degradar, e a sinalização de "escolhi uma versão freezada" é frágil em entrevista sênior.

A comunidade reagiu de dois modos: forkando (AwesomeAssertions) ou migrando para alternativas estabelecidas (Shouldly).

### Opção A — FluentAssertions 7.x (frozen)

- ✅ API conhecida.
- ❌ **Sem evolução futura**. Em 2026, é apostar em código congelado.
- ❌ Patches de segurança pararam.
- ❌ Mensagem de banca frágil: *"usei a última versão antes de virar paga"* abre a pergunta "e quando deixar de funcionar?".

### Opção B — Shouldly (recomendada)

Lib de asserção estabelecida desde ~2010. License Apache 2.0/MIT. Sem mudanças de licença na história.

```csharp
result.ShouldBe(expected);
result.ShouldNotBeNull();
result.ShouldBeOfType<Money>();
action.ShouldThrow<InvalidPlateException>();
list.ShouldContain(item);
list.ShouldNotBeEmpty();
```

- ✅ **15+ anos de estrada** sob licença permissiva — sem risco político.
- ✅ Mensagens de falha legíveis (mostra o "subject" da asserção, não só os valores).
- ✅ API limpa e bem aceita em projetos sênior.
- ✅ Defesa direta: *"escolhi Shouldly por causa da mudança de licença do FluentAssertions em 2024. Lib estabelecida há mais de uma década, sem risco de fricção comercial futura."*
- ❌ Adoção menor que FA historicamente — mas em 2026 é praticamente o "FA gratuito de fato".
- ❌ API não é drop-in com FA (`ShouldBe` em vez de `Should().Be`) — quem vem de FA sente diferença pequena.

### Opção C — AwesomeAssertions (fork drop-in do FA)

Fork criado em 2024 quando a licença do FA mudou, mantendo API praticamente idêntica.

- ✅ **Drop-in**: trocar o pacote NuGet, manter `Should().Be(...)`.
- ✅ License MIT.
- ⚠️ **Fork novo**: em 2026 vale verificar a saúde atual (releases recentes, issues abertas) antes de adotar — risco de "fork morto em silêncio" existe. Decisão deixada para verificação manual no momento da adoção.
- ⚠️ Adoção ainda em consolidação — menos reconhecível que Shouldly em entrevistas.

### Opção D — `Assert` nativo do framework (xUnit v3)

xUnit v3 (em 2024+) ganhou `Assert.Equivalent` para comparação estrutural — substituto razoável de `BeEquivalentTo`.

- ✅ Zero dependência adicional.
- ✅ Mantido pela equipe do xUnit.
- ❌ API menos fluente: `Assert.Equal(expected, actual)` é menos legível que `actual.ShouldBe(expected)`.
- ❌ Mensagens de falha menos ricas que Shouldly/AwesomeAssertions.

### Tradeoffs lado a lado

| Critério | FA 7.x (frozen) | Shouldly | AwesomeAssertions | xUnit nativo |
|---|---|---|---|---|
| License futura segura | ⚠️ frozen | ✅ Apache/MIT | ✅ MIT | ✅ Apache |
| Mensagens de falha | excelente | excelente | excelente (= FA) | OK |
| Manutenção ativa | ❌ não | ✅ 15 anos | ⚠️ verificar | ✅ |
| Drop-in pra quem vem de FA | n/a | ❌ | ✅ | ❌ |
| Adoção em sênior .NET | ❌ frozen | ✅ alta | ⚠️ crescente | ⚠️ |
| Risco político futuro | alto | baixo | médio | baixíssimo |

### Recomendação preliminar

**Shouldly**. Argumento de banca: *"FluentAssertions virou comercial em 2024; usar uma versão freezada (7.x) é apostar em código morto. Shouldly é estabelecida há 15 anos com licença permissiva — sem risco político, sem dependência de fork incipiente, mensagens de falha excelentes."*

## Camada 3 — Mocks

### Opção A — NSubstitute (recomendada)

Lib de mocks com sintaxe natural: `provider.FetchAsync(plate, Arg.Any<CT>()).Returns(...)`.

- ✅ Sintaxe limpa, sem `It.IsAny` cerimônia.
- ✅ Open source, sem polêmica recente.
- ✅ Padrão crescente em projetos novos .NET.

### Opção B — Moq

Lib mais antiga e mais usada historicamente.

- ✅ Maior base instalada.
- ❌ **Polêmica recente**: Moq 4.20 (ago/2023) incluiu o "SponsorLink" que coletava emails dos devs sem aviso claro — controvérsia significativa. Apesar de removido, deixou marca de desconfiança. Em apresentação sênior em 2026, escolher Moq convida a pergunta "você sabia da SponsorLink?".

### Opção C — Fakes manuais

Implementar `class FakeProvider : IDebtProvider { ... }` para cada teste.

- ✅ Sem lib adicional.
- ✅ Comportamento explícito — fácil de raciocinar.
- ❌ Repete boilerplate.

### Opção D — Híbrida: NSubstitute para mocks de comportamento, fakes manuais para colaboradores estáveis

Para o `DebtProviderChain` (já temos `FakeProviders` com WireMock externamente — ADR-008), os testes de integração nem precisam mockar HTTP. Para `DebtsCalculator` testes, mockamos `IDebtProviderChain` com NSubstitute. Para regras (`IInterestRule`), são puras — sem mock necessário.

## Camada 4 — Test data

### Opção A — Builders manuais (recomendada)

Helpers explícitos: `DebtBuilder.Ipva().WithAmount(1500).WithDueDate(...).Build()`.

- ✅ Explícito; fácil de ler em testes.
- ✅ Auto-documentado.
- ❌ Boilerplate inicial (~50 linhas).

### Opção B — AutoFixture

Gera dados aleatórios.

- ✅ Cobre casos não pensados.
- ❌ Para domínio com invariantes (placa Mercosul, valores monetários positivos), AutoFixture quebra ou gera lixo até configurar customizações. Não vale para escopo do desafio.

### Opção C — Bogus

Gerador de dados realistas (nomes, endereços, etc.).

- ✅ Bom para cenários com variedade visual.
- ❌ Não é o problema deste desafio.

## Camada 5 — Property-based testing

### Opção A — FsCheck (com integração xUnit via FsCheck.Xunit)

Permite escrever propriedades como *"para todo `Money m`, arredondado HALF_UP, `m + (-m) == Money.Zero`"*.

- ✅ Excelente para validar invariantes de `Money` e arredondamento HALF_UP.
- ✅ Argumento de banca forte: *"property-based encontra casos de borda que exemplos pontuais não pegam"*.
- ❌ Curva de aprendizado se o time não conhece. Para o desafio é factível porque é solo.

### Opção B — Ignorar property-based

- ✅ Menos uma lib.
- ❌ Perde uma oportunidade de diferenciação em sênior.

## Camada 6 — Cobertura

Coverlet + ReportGenerator (relatório HTML). Apenas configuração, não decisão arquitetural.

## Camada 7 — Estrutura dos projetos de teste (em linha com ADR-007)

```
tests/
├── Dok.Domain.Tests/         # VOs, IInterestRule, PaymentSimulator (puro)
├── Dok.Application.Tests/    # DebtsService, DebtsCalculator (com NSubstitute para colaboradores)
└── Dok.Integration.Tests/    # WireMock + WebApplicationFactory; testa fluxo HTTP completo + fallback
```

## Recomendação consolidada

| Camada | Escolha |
|---|---|
| Test framework | **xUnit** |
| Asserções | **Shouldly** (estabelecida há 15+ anos; licença permissiva sem histórico de mudança; recomendada após FA virar comercial em 2024) |
| Mocks | **NSubstitute** (NSub para colaboradores; rules puras dispensam mock) |
| Test data | **builders manuais** (DebtBuilder, MoneyBuilder) |
| Property-based | **FsCheck.Xunit** — aplicado a `Money` (HALF_UP) e `IInterestRule` (idempotência, monotonicidade) |
| Cobertura | Coverlet + ReportGenerator (HTML) |
| Estrutura de projetos | 3 projetos: Domain.Tests / Application.Tests / Integration.Tests |
| Integration via HTTP | `WebApplicationFactory<Program>` + `WireMockServer` ad-hoc por teste |

## Decisão

| Camada | Escolha |
|---|---|
| Test framework | **xUnit** |
| Asserções | **Shouldly** (após mudança de licença do FluentAssertions em 2024) |
| Mocks | **NSubstitute** (evita polêmica da SponsorLink no Moq) |
| Test data | **Builders manuais** (`DebtBuilder`, etc.) — explícitos e auto-documentados |
| Property-based | **FsCheck.Xunit** aplicado a `Money` (HALF_UP) e `IInterestRule` (idempotência, monotonicidade) |
| Cobertura | Coverlet + ReportGenerator (HTML local) |
| Estrutura de projetos (em linha com ADR-007) | `Dok.Domain.Tests` (puro), `Dok.Application.Tests` (NSubstitute para colaboradores), `Dok.Integration.Tests` (`WebApplicationFactory<Program>` + `WireMockServer` ad-hoc) |
| Tempo em testes | `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) — fixar 2024-05-10T00:00:00Z (em linha com ADR-012) |

## Justificativa

1. **xUnit** é o padrão de fato em .NET moderno; o avaliador reconhece sem fricção.
2. **Shouldly** elimina o risco político do FluentAssertions (mudança de licença em jul/2024) sem cair em fork incipiente — 15+ anos de estrada com licença permissiva.
3. **NSubstitute** evita a polêmica da SponsorLink do Moq 4.20 (ago/2023) e tem sintaxe mais limpa (`Returns(...)` direto).
4. **Builders manuais** são auto-documentados e mais legíveis que AutoFixture/Bogus para domínios com invariantes (Plate, Money).
5. **FsCheck.Xunit** é diferencial sênior — propriedades (`para todo m: Money, m + (-m) == zero`) encontram bugs de arredondamento que exemplos pontuais não pegam.
6. **3 projetos de teste** alinham com a Dependency Rule (ADR-007) — Domain.Tests não carrega ASP.NET; Integration.Tests usa `WebApplicationFactory` para subir a Api real e WireMock para os providers fake.
7. Defesa em uma frase: *"xUnit + Shouldly + NSubstitute + FsCheck + builders manuais. Sem libs com risco político (FA, Moq), property-based onde paga aluguel (Money/HALF_UP), e integração HTTP real com WireMock."*

---
