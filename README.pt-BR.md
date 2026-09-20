# URL Shortener

[English](README.md) · **Português**

Lab de system design poliglota sobre o framework **Tars**. Ver [docs/design.pt-BR.md](docs/design.pt-BR.md)
para arquitetura, contratos, decisões (ADRs) e roadmap.

## Serviços

| Serviço | Papel | Storage | Porta dev |
|---|---|---|---|
| Gateway | entrada (YARP), rate-limit, roteamento | — | 8080 |
| Shortening | write / source of truth + outbox | Postgres | 8081 |
| Redirect | read / hot path (cache-aside) | Redis + Postgres | 8082 |
| Analytics | consome cliques, agrega | MongoDB | 8083 |
| Kgs | geração de short codes (ranges) | Postgres | 8084 |

Broker: **Kafka** (eventos). O tars é consumido via **NuGet** (`Pottmayer.Tars.* 0.0.15`) do
feed privado do GitHub Packages (ver `nuget.config`).

## Rodar tudo de uma vez (Docker)

```bash
docker compose up --build
```

Sobe **toda a stack** — infra + os 5 serviços .NET + o frontend — de uma vez. É o que o botão
de run do Visual Studio dispara (projeto `docker-compose.dcproj` na solution): selecione
**docker-compose** no seletor de startup e aperte F5.

Antes do primeiro build, coloque o PAT do GitHub Packages em `./github_token.txt` (uma linha,
gitignored) — é o segredo usado pra restaurar os NuGets `Pottmayer.Tars.*` dentro do container.

Depois de subir:
- Frontend: http://localhost:5173
- Gateway (API + short links): http://localhost:8080 — os códigos gerados apontam pra cá
- Grafana: http://localhost:3000 · Prometheus: http://localhost:9090
- Serviços expostos p/ debug: shortening 8081, redirect 8082, analytics 8083, kgs 8084

As migrations (migris) e os tópicos Kafka são pré-requisito do schema, mas persistem nos volumes
(`pgdata`/`kafkadata`), então valem entre `up`s. Num clone novo, rode as migrations antes (abaixo).

## Rodar só a infra (modo host / F5 sem Docker)

```bash
docker compose up -d postgres mongo mongo-init redis kafka kafka-init otel-collector prometheus grafana
```

Sobe Postgres (5434), Mongo (27017), Redis (6379) e Kafka (9092); os serviços .NET rodam no host
(via F5/`dotnet run`, apontando pra `localhost` nos appsettings). O Postgres já cria os bancos
`urlshortener_shortening` e `urlshortener_kgs`.

## Build (.NET, no host)

```bash
dotnet build src/Pottmayer.UrlShortener.slnx
```

## Migrations (migris)

Um projeto migris por banco em `migrations/`. Copie `config.example.json` para `config.json`
(gitignored) e rode `migris apply local` dentro da pasta do banco.

## Status

Completo até a fase de observabilidade e o frontend React, mais o run com um comando via Docker
(e o startup `docker-compose` do Visual Studio). Ver [docs/design.pt-BR.md](docs/design.pt-BR.md) §12
para o roadmap fatiado.
