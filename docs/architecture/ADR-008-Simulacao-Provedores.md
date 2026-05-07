# ADR-008 — Estratégia de simulação dos provedores

**Status:** aceito
**Data:** 2026-04-27

## Contexto

O enunciado descreve dois "Provedores simulados" (A em JSON, B em XML) com payload exemplo, mas não fornece serviços HTTP reais. Precisamos decidir como representá-los para que:

1. A aplicação rode localmente e responda à request real (`POST /api/v1/debitos`) — necessário para demo na banca.
2. Os testes de integração possam exercitar o **fallback** (A falha → B responde) e cenários de erro (timeout, 5xx, payload inválido).
3. Os adapters de produção sejam **iguais** ao que rodaria contra um provedor real — sem código "fake" misturado com código de produção.

A escolha aqui dita: o adapter usa `HttpClient` de verdade ou tem implementações em memória? Como demonstrar resiliência (retry, circuit breaker) se nada falha de fato?

## Opções consideradas

### Opção A — Stubs em memória (sem `HttpClient`)

Implementações de `IDebtProvider` que retornam dados hardcoded ou de arquivo JSON/XML em disco. Sem rede, sem `HttpClient`, sem timeout real.

```csharp
public class ProviderAStub : IDebtProvider {
    public Task<IReadOnlyList<Debt>> FetchAsync(Plate p, CancellationToken ct) =>
        Task.FromResult(SampleData.ForPlate(p));
}
```

- ✅ Mais simples possível — meia hora de implementação.
- ✅ Testes muito rápidos.
- ❌ **Adapter de produção não existe**. Quando integrar com provider real, precisa reescrever do zero.
- ❌ **Resiliência (Polly) fica decorativa**: como demonstrar retry/circuit breaker se o stub nunca falha de verdade?
- ❌ Não exercita: deserialização de JSON/XML, headers, content-type, timeout, falhas transientes.
- ❌ Demo na banca fica fraca: *"esse é o adapter, mas não chama nada real"*.

### Opção B — WireMock.Net in-process (HttpClient real contra fake HTTP server)

`WireMock.Net` sobe servidores HTTP fake na inicialização da aplicação (em portas locais), e os adapters chamam `https://localhost:9001` (Provider A) e `https://localhost:9002` (Provider B) com `HttpClient` real.

- ✅ **Adapter é exatamente o de produção**: mesmo `HttpClient`, mesmas configurações de Polly, mesma deserialização. Trocar para provedor real = trocar URL.
- ✅ **Resiliência testável de verdade**: WireMock pode responder 500, 200 com payload corrompido, 200 com delay (timeout), ou simplesmente desligar (conexão recusada). Ideal para exercitar Polly.
- ✅ **Demo na banca poderosa**: você pode pedir ao avaliador *"agora desligue o Provider A"* e mostrar o fallback acontecendo ao vivo (basta `wireMockA.Stop()`).
- ✅ **Cobertura realista nos testes de integração**: subir os WireMock servers nos testes, configurar respostas, exercitar fluxo HTTP completo.
- ❌ Mais setup: ~50 linhas para configurar os dois WireMock servers + arquivos JSON/XML com os payloads de exemplo.
- ❌ Portas locais precisam ficar livres (ou portas dinâmicas com discovery via configuração).
- ⚠️ Decisão sobre **onde** subir: in-process do mesmo app (mais simples, mas mistura escopo) ou em projeto separado (mais limpo, mas requer dois processos).

### Opção C — Endpoints `/_mock/...` no próprio app

Adicionar rotas como `GET /_mock/providerA/{plate}` e `GET /_mock/providerB/{plate}` que retornam JSON/XML hardcoded. Os adapters chamam essas rotas via `HttpClient`.

- ✅ Apenas um processo rodando.
- ✅ Aparece no Swagger.
- ❌ **Mistura escopos**: a aplicação que serve o caso de uso também serve dados de mock. Confunde a narrativa: *"isso é parte do produto ou é teste?"*.
- ❌ Self-call: o app chama a si mesmo via `HttpClient`. Funciona, mas é estranho — overhead, complica timeout, parece amador.
- ❌ Difícil simular "Provider A está fora" sem desligar o próprio app.

### Opção D — Híbrida (recomendada): WireMock.Net + script de inicialização

WireMock.Net sobe os dois fakes — **separadamente** da aplicação principal (em projeto auxiliar `Dok.FakeProviders` ou via script `make fakes`). A aplicação real consome `https://localhost:9001` e `9002` via configuração.

- Para a banca: rodar `dotnet run --project src/Dok.FakeProviders` em um terminal e `dotnet run --project src/Dok.Api` no outro. Avaliador vê dois processos como seriam dois serviços.
- Para testes de integração: usar `WireMockServer.Start(port)` dentro do teste, configurar respostas por cenário, e desligar após.
- ✅ Melhor narrativa: dois "provedores" são processos visivelmente distintos.
- ✅ Demo de fallback ao vivo é trivial: `Ctrl+C` no terminal do Provider A e refazer a request.
- ❌ Dois `dotnet run` para subir tudo (mitigado com docker-compose ou um script de make).

## Tradeoffs principais

| Critério | A (stubs) | B (WireMock in-process) | C (endpoints `_mock`) | D (WireMock externo) |
|---|---|---|---|---|
| Adapter de produção é igual ao usado | ❌ | ✅ | ✅ (com reservas) | ✅ |
| Exercita Polly de verdade | ❌ | ✅ | ⚠️ | ✅ |
| Demo de fallback ao vivo | ❌ | ✅ (controlado por código) | ❌ | ✅ (visualmente convincente) |
| Setup | trivial | médio | trivial | médio-alto |
| Mistura escopos | n/a | leve | alto | nenhum |
| "Cara" sênior | fraca | sólida | discutível | mais sólida |

## Decisão

**Opção D — WireMock.Net externo**, com:

- Projeto auxiliar `src/Dok.FakeProviders/` (não faz parte da entrega de produção; está na solution para conveniência) que sobe dois `WireMockServer` em portas configuráveis (default `9001` e `9002`).
- `Dok.Api` consome URLs vindas de `appsettings.json` — código de produção idêntico ao que rodaria contra provedores reais.
- `tests/Dok.Integration.Tests/` sobe seus próprios WireMock em portas dinâmicas, com setup/teardown por teste — não compartilha estado com `Dok.FakeProviders`.
- Script auxiliar (`make up` ou `scripts/run.sh`) sobe os 3 processos em paralelo para a demo.

## Justificativa

1. **Adapter de produção idêntico ao real**: HttpClient + Polly + deserialização exercitados de fato.
2. **Demo de fallback ao vivo é dramática**: Ctrl+C no Provider A e o avaliador vê o sistema reagir nos logs estruturados.
3. **Polly não vira teatro**: WireMock pode responder 500/timeout/payload corrompido — Polly executa de verdade.
4. **Separação de escopo limpa**: o app de produção não tem código fake misturado.
5. **Custo amortizado**: ~80 linhas no `FakeProviders` + payloads JSON/XML em arquivos = trivial para o ganho narrativo.

---
