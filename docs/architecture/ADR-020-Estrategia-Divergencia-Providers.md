# ADR-020 — Estratégia para divergência de providers

**Status:** aceito
**Data:** 2026-05-02

## Contexto

O enunciado (`docs/HomeTest-2.pdf`, seção "Casos de borda") pede explicitamente:

> *Provedores retornando dados divergentes para a mesma placa (descreva sua estratégia, mesmo que não a implemente).*

Hoje o sistema implementa **fallback sequencial** via `DebtProviderChain`: a primeira resposta de sucesso encerra o fluxo; o segundo provider só é consultado se o primeiro falhar (timeout, 5xx, payload malformado, CB aberto). Como consequência, a divergência **não é observável** no design atual — só uma das fontes é consultada por consulta de placa.

Esta ADR documenta a estratégia escolhida, as alternativas rejeitadas e os gatilhos para reavaliar.

## Decisão

**Sequential first-success** — o primeiro provider que responder com sucesso encerra a consulta. Não há cross-check entre providers para a mesma placa.

A ordem da chain é fixada em `Dok.Infrastructure/DependencyInjection.cs` (Provider A antes de Provider B) — é a "ordem configurada" mencionada no enunciado.

## Alternativas consideradas

### A — Paralelo + cross-check

Consultar A e B em paralelo, comparar resultados, sinalizar divergência.

- ✅ Detecta divergência ativamente.
- ❌ Custo dobrado por consulta (2× chamadas HTTP, 2× CPU de parse, 2× pressão na rede).
- ❌ Política de reconciliação na divergência é ambígua: qual provider é fonte de verdade? Quem decide?
- ❌ Latência da resposta passa a ser `max(latency_A, latency_B)` em vez de `min` — pior em geral.
- ❌ Não há requisito de auditoria que justifique o custo. O enunciado pede a **descrição da estratégia**, não a implementação.

### B — Sequencial + verify-on-suspect-zero

Quando o primeiro provider responde "zero débitos", consultar o segundo como verificação.

- ✅ Trata o caso ambíguo "sem débito vs sem registro" — risco real (cliente acha que não deve nada e é cobrado depois).
- ❌ "Suspeito" é heurística frágil: se uma placa realmente não tem débitos, o segundo provider vai dizer o mesmo, e gastamos uma chamada extra para nada.
- ❌ Política para divergência (A: zero; B: 1 débito) ainda precisa ser definida — quem ganha?
- ❌ Aumenta latência no caso mais comum (placa sem débitos).

### C — Authoritative provider per debt type

Configurar mapping `IPVA → Provider A`, `MULTA → Provider B` (cada tipo tem sua fonte canônica).

- ✅ Resolve divergência por design — não há sobreposição de responsabilidade.
- ❌ Exige conhecimento de domínio que o enunciado não fornece (não diz qual provider é canônico para qual tipo).
- ❌ Quebra a propriedade de "fallback" — se Provider A cair, IPVA fica indisponível.
- ❌ Aumenta complexidade sem ganho proporcional para o escopo do desafio.

## Trade-offs explícitos da decisão

| Dimensão | Sequential first-success |
|---|---|
| Latência média | Melhor (`min(latency_A, latency_B)` quando A está saudável; só paga B na falha) |
| Custo por consulta | Mínimo (1 chamada no caminho feliz, 2 no fallback) |
| Detecção de divergência | **Não detecta** — explicitamente fora do escopo |
| Detecção de provider stale | **Não detecta** — se A retorna dados desatualizados consistentemente, B nunca é consultado |
| Determinismo do resultado | Alto — ordem de provider determina qual fonte é usada |
| Complexidade arquitetural | Baixa — é o que `IDebtProviderChain` já implementa |

## Justificativa

1. **Aderência ao enunciado.** A spec diz "descreva a estratégia, mesmo que não a implemente". A descrição é honesta: *escolhemos não implementar cross-check; aqui está o porquê e o trade-off.*
2. **Latência mínima.** Para um endpoint operacional (consulta de débitos para pagamento), latência baixa é mais valiosa que detecção proativa de inconsistência entre providers.
3. **Custo de chamadas externas.** Providers reais cobram por chamada ou têm rate limits. Dobrar o custo por consulta na chain saudável é regressão para resolver problema raro.
4. **Política de reconciliação não-trivial.** Cross-check só faz sentido se houver política clara de "quem ganha em divergência" — política que depende do contrato do provider real (SLA, frescor dos dados, autoridade legal). Sem esse contrato, cross-check vira detecção sem ação.

## Mitigações operacionais (sem mudar o design)

A divergência silenciosa é um risco real. Sem implementar cross-check, ainda é possível mitigar:

- **Métricas via `IMeterFactory`** (introduzidas para observabilidade dos providers): contar `dok.providers.requests{provider, outcome}` permite ver se um provider passou a falhar consistentemente — sintoma indireto de degradação que pode incluir staleness.
- **Logs estruturados com `X-Dok-Provider`** no response: cada consulta indica qual provider respondeu. Auditoria post-hoc consegue correlacionar reclamações com a fonte específica.
- **ProviderUsage em Application**: já rastreia qual provider foi consultado por requisição.

## Quando reavaliar

Esta decisão muda se:

- Surgir requisito regulatório / de auditoria que exija prova de consistência entre fontes.
- Aparecer evidência operacional de divergência prejudicial (ex: tickets de suporte recorrentes "paguei IPVA mas o sistema diz que ainda devo").
- O contrato dos providers reais incluir SLA de frescor diferenciado, justificando authoritative-per-type (alternativa C).
- O custo das chamadas externas cair a ponto de paralelo + cross-check ser viável economicamente.

## Resposta pronta para a banca

> *"A spec pede descrever a estratégia para divergência de providers. Eu escolhi não implementar cross-check — fallback sequencial first-success. Trade-off explícito: latência mínima e custo de uma chamada por consulta no caminho feliz, em troca de não detectar divergência ativa. As alternativas que considerei foram paralelo+cross-check (custo dobrado e política de reconciliação ambígua), sequencial+verify-on-suspect-zero (heurística frágil), e authoritative-per-type (exige conhecimento de domínio que a spec não fornece). Mitigação operacional vem das métricas por provider e do header `X-Dok-Provider` no response — divergência prejudicial fica observável post-hoc para correlação com reclamações. Está documentado em ADR-020."*

---
