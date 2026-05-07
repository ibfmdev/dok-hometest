# ADR-018 — Empacotamento: Dockerfile, docker-compose, scripts auxiliares

**Status:** aceito
**Data:** 2026-04-27

## Contexto

A solução tem **3 processos** rodando em paralelo durante a demo:

1. `Dok.Api` — a API principal.
2. `Dok.FakeProviders` (instância A) — WireMock fake do Provider A na porta 9001.
3. `Dok.FakeProviders` (instância B) — WireMock fake do Provider B na porta 9002.

A spec exige README com "como rodar". O avaliador precisa de **um comando** para subir tudo e validar a solução. Decisão: como entregar isso?

Sub-decisões:

1. **Containerização**: Dockerfile para os processos? Quais?
2. **Orquestração local**: docker-compose, scripts shell, Makefile, ou combinação?
3. **Imagem base**: `runtime` vs `aspnet` vs `sdk` vs Alpine vs Chiseled.
4. **Build**: multi-stage vs single-stage.
5. **Atalhos para o dev / avaliador**: Makefile com targets comuns.

## Sub-decisão 1 — Containerização

### Opção A — Tudo containerizado (recomendada)

Dockerfile para `Dok.Api` e Dockerfile para `Dok.FakeProviders`. Avaliador roda `docker compose up` e tudo sobe.

- ✅ **Reproducibilidade total**: avaliador não precisa ter .NET 10 instalado.
- ✅ Demo é **um comando**.
- ✅ Mostra que sabe empacotar pra produção (sênior).
- ✅ Fácil simular fallback ao vivo: `docker compose stop provider-a` derruba só o A.
- ❌ Custo inicial: ~2 Dockerfiles + 1 docker-compose.yml (~80 linhas total).

### Opção B — Apenas `dotnet run` com scripts

- ✅ Sem Docker, ambiente .NET cru.
- ❌ Avaliador precisa do .NET 10 SDK instalado.
- ❌ Não demonstra capacidade de empacotamento — perde ponto sênior.

### Opção C — Híbrido: Dockerfile da Api, FakeProviders em `dotnet run`

- ✅ Production-ready para Api.
- ❌ Inconsistência: por que Api tem Dockerfile e FakeProviders não? Argumento fraco.

## Sub-decisão 2 — Orquestração

### Opção A — `docker-compose.yml` (recomendada)

Define os 3 services, suas dependências, redes, e mapeamentos de porta.

```yaml
services:
  provider-a:
    build: ./src/Dok.FakeProviders
    environment:
      Provider__Port: 9001
      Provider__DataFile: /data/providerA.json
    volumes:
      - ./src/Dok.FakeProviders/data:/data:ro
    ports: ["9001:9001"]

  provider-b:
    build: ./src/Dok.FakeProviders
    environment:
      Provider__Port: 9002
      Provider__DataFile: /data/providerB.xml
    volumes:
      - ./src/Dok.FakeProviders/data:/data:ro
    ports: ["9002:9002"]

  api:
    build: ./src/Dok.Api
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      Providers__ProviderAUrl: http://provider-a:9001
      Providers__ProviderBUrl: http://provider-b:9002
    depends_on:
      - provider-a
      - provider-b
    ports: ["8080:8080"]
```

- ✅ Padrão da indústria; sintaxe declarativa clara.
- ✅ Network interno entre containers (DNS por nome de service).
- ✅ Variáveis de ambiente sobrescrevem `appsettings.json` (override em cascata, ADR-017).
- ✅ Demo de fallback ao vivo: `docker compose stop provider-a`.

### Opção B — Scripts shell paralelos

- ❌ Não há network virtual nativa.
- ❌ Difícil parar serviços individuais.
- ❌ Não faz sentido em 2026 quando docker-compose existe.

## Sub-decisão 3 — Imagem base e Build

### Opção A — Multi-stage com `mcr.microsoft.com/dotnet/aspnet:10.0` final (recomendada)

```dockerfile
# build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Dok.Api/Dok.Api.csproj", "src/Dok.Api/"]
COPY ["src/Dok.Application/Dok.Application.csproj", "src/Dok.Application/"]
COPY ["src/Dok.Domain/Dok.Domain.csproj", "src/Dok.Domain/"]
COPY ["src/Dok.Infrastructure/Dok.Infrastructure.csproj", "src/Dok.Infrastructure/"]
RUN dotnet restore "src/Dok.Api/Dok.Api.csproj"
COPY . .
RUN dotnet publish "src/Dok.Api/Dok.Api.csproj" -c Release -o /app/publish --no-restore

# runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Dok.Api.dll"]
```

- ✅ **Multi-stage**: imagem final só carrega runtime + binário, sem SDK. Tamanho ~200 MB vs ~800 MB single-stage.
- ✅ `aspnet:10.0` é a imagem oficial Microsoft, com .NET 10 LTS (alinhado ADR-002).
- ✅ Build-cache friendly: copia `csproj` antes do código completo, restore cacheado entre builds.

### Opção B — Imagens chiseled (`mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`)

Imagens "chiseled" da Microsoft: distroless minimal, ~30 MB final. Roda como non-root por default.

- ✅ Menor superfície de ataque (sem shell, sem package manager).
- ✅ Tamanho menor.
- ✅ Diferencial sênior real em 2026.
- ❌ Sem shell — debugging dentro do container exige techniques especiais (kubectl debug, etc.). Fora do escopo desta demo.

### Opção C — Alpine

- ✅ Pequena.
- ❌ glibc/musl pode causar problemas com `System.Globalization` em alguns cenários (ex: `decimal.Parse` com cultura específica). Risco para nossa decisão de `Money`.
- ❌ Microsoft já oferece chiseled — Alpine perdeu seu diferencial.

### Recomendação dentro da sub-decisão

**`mcr.microsoft.com/dotnet/aspnet:10.0` multi-stage** como default seguro, com **opção de mudar para chiseled** se quiser bullet point extra de "imagem distroless minimal". Para o desafio, o default é mais defensável (sem riscos de debug).

## Sub-decisão 4 — Atalhos: Makefile

```makefile
.PHONY: up down build test clean

up:
\tdocker compose up --build

down:
\tdocker compose down -v

build:
\tdotnet build

test:
\tdotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage

coverage:
\treportgenerator -reports:./coverage/**/coverage.cobertura.xml -targetdir:./coverage/report -reporttypes:Html

clean:
\tdotnet clean && docker compose down -v --rmi local
```

- ✅ Atalhos para os comandos mais usados (sobe tudo, derruba tudo, roda testes).
- ✅ Avaliador roda `make up` e tudo está rodando.
- ✅ `make test` + `make coverage` para a parte de testes.
- ❌ Requer `make` instalado (universal em Linux/Mac; Windows precisa Git Bash ou WSL).

Alternativa para Windows-friendly: scripts `up.sh` / `up.ps1` em vez de Makefile.

## Sub-decisão 5 — Documentação no README

Comandos do README devem ser:

```bash
# desenvolvimento local (sem Docker)
dotnet run --project src/Dok.FakeProviders -- --port 9001 --data data/providerA.json &
dotnet run --project src/Dok.FakeProviders -- --port 9002 --data data/providerB.xml &
dotnet run --project src/Dok.Api

# OU containerizado (recomendado para demo)
docker compose up --build

# testes
make test
```

E uma seção curta explicando os 3 processos, suas portas, e como derrubar um para ver o fallback.

## Recomendação consolidada

1. **Dockerfile multi-stage** para `Dok.Api` e `Dok.FakeProviders` — `mcr.microsoft.com/dotnet/aspnet:10.0` como runtime (chiseled como melhoria futura no README).
2. **`docker-compose.yml`** orquestra os 3 services com network interno e mapeamentos explícitos.
3. **`Makefile`** com targets `up`, `down`, `build`, `test`, `coverage`, `clean`.
4. **README** com 3 seções de "como rodar": dev local com `dotnet run`, demo com `docker compose up`, testes com `make test`.
5. **Demo de fallback documentada**: `docker compose stop provider-a` → refazer request → ver fallback nos logs.

## Decisão

1. **Containerização**: Dockerfile **multi-stage** para `Dok.Api` e `Dok.FakeProviders` usando `mcr.microsoft.com/dotnet/aspnet:10.0` como runtime final.
2. **Orquestração**: `docker-compose.yml` na raiz definindo 3 services (`provider-a`, `provider-b`, `api`) com network interno, mapeamentos de porta explícitos e variáveis de ambiente sobrescrevendo `appsettings.json`.
3. **Atalhos**: `Makefile` com targets `up`, `down`, `build`, `test`, `coverage`, `clean`. README documenta também o caminho sem Docker (`dotnet run` em paralelo) para quem prefira.
4. **README**: três seções de "como rodar" (dev local, demo containerizada, testes), com instruções explícitas para a **demo de fallback ao vivo** (`docker compose stop provider-a`).
5. **Imagem chiseled e Windows-friendly scripts** (`up.ps1`/`up.sh`): registrados como melhorias futuras no README — não bloquear escopo.

## Justificativa

1. **Multi-stage com `aspnet:10.0`** entrega imagem ~200 MB (vs ~800 MB single-stage), build-cache friendly (csproj antes do código), e usa imagem oficial Microsoft alinhada ao .NET 10 LTS (ADR-002).
2. **`docker compose up --build`** é **um único comando** para o avaliador subir os 3 processos — é o tipo de detalhe que diferencia em apresentação sênior. Ele não precisa nem ter .NET 10 SDK instalado.
3. **Demo de fallback é trivial e dramática**: `docker compose stop provider-a` derruba o A; refazendo a request, o avaliador vê o fallback nos logs estruturados (alinhado ADR-008 e ADR-010).
4. **`Makefile` resolve atalhos comuns** sem reinventar; aceita-se que Windows precise de WSL/Git Bash — alternativa registrada como melhoria.
5. **Mensagem na banca**: *"empacotei com Dockerfile multi-stage e docker-compose. Para subir tudo: `docker compose up --build`. Para ver o fallback: `docker compose stop provider-a` e refazer a request — você vê nos logs estruturados o sistema reagindo."*

---
