# URL Shortener

Lab de system design poliglota sobre o framework **Tars**. Ver [docs/design.md](docs/design.md)
para arquitetura, contratos, decisões (ADRs) e roadmap.

## Serviços

| Serviço | Papel | Storage | Porta dev |
|---|---|---|---|
| Gateway | entrada (YARP), rate-limit, roteamento | — | 8080 |
| Shortening | write / source of truth + outbox | Postgres | 8081 |
| Redirect | read / hot path (cache-aside) | Redis + Postgres | 8082 |
| Analytics | consome cliques, agrega | MongoDB | 8083 |
| Kgs | geração de short codes (ranges) | Postgres | 8084 |

Broker: **Kafka** (eventos). O tars é consumido **do código-fonte** (sibling repo `../tars`),
via `$(TarsSrc)` no `Directory.Build.props`.

## Rodar a infra

```bash
docker compose up -d
```

Sobe Postgres (5434), Mongo (27017), Redis (6379) e Kafka (9092). O Postgres já cria os
bancos `urlshortener_shortening` e `urlshortener_kgs`.

## Build

```bash
dotnet build src/Pottmayer.UrlShortener.slnx
```

## Migrations (migris)

Um projeto migris por banco em `migrations/`. Copie `config.example.json` para `config.json`
(gitignored) e rode `migris apply local` dentro da pasta do banco.

## Status

Fase 0 (scaffolding) — projetos + refs tars + infra. Bootstrap mínimo (health endpoint por serviço).
Features vêm nas fases seguintes (ver design.md §12).
