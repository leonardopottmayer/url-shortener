# URL Shortener

**English** · [Português](README.pt-BR.md)

Polyglot system-design lab built on the **Tars** framework. See [docs/design.md](docs/design.md)
for architecture, contracts, decisions (ADRs) and roadmap.

## Services

| Service | Role | Storage | Dev port |
|---|---|---|---|
| Gateway | entry point (YARP), rate-limit, routing | — | 8080 |
| Shortening | write / source of truth + outbox | Postgres | 8081 |
| Redirect | read / hot path (cache-aside) | Redis + Postgres | 8082 |
| Analytics | consumes clicks, aggregates | MongoDB | 8083 |
| Kgs | short-code generation (ranges) | Postgres | 8084 |

Broker: **Kafka** (events). Tars is consumed via **NuGet** (`Pottmayer.Tars.* 0.0.15`) from the
private GitHub Packages feed (see `nuget.config`).

## Run everything at once (Docker)

```bash
docker compose up --build
```

Brings up the **whole stack** — infra + the 5 .NET services + the frontend — in one shot. This is
what the Visual Studio run button triggers (the `docker-compose.dcproj` project in the solution):
select **docker-compose** in the startup dropdown and hit F5.

Before the first build, put your GitHub Packages PAT in `./github_token.txt` (a single line,
gitignored) — it's the secret used to restore the `Pottmayer.Tars.*` NuGets inside the container.

Once it's up:
- Frontend: http://localhost:5173
- Gateway (API + short links): http://localhost:8080 — generated codes point here
- Grafana: http://localhost:3000 · Prometheus: http://localhost:9090
- Services exposed for debugging: shortening 8081, redirect 8082, analytics 8083, kgs 8084

The migrations (migris) and Kafka topics are a schema prerequisite, but they persist in the volumes
(`pgdata`/`kafkadata`), so they survive across `up`s. On a fresh clone, run the migrations first (below).

## Run only the infra (host mode / F5 without Docker)

```bash
docker compose up -d postgres mongo mongo-init redis kafka kafka-init otel-collector prometheus grafana
```

Brings up Postgres (5434), Mongo (27017), Redis (6379) and Kafka (9092); the .NET services run on the
host (via F5/`dotnet run`, pointing at `localhost` in the appsettings). Postgres already creates the
`urlshortener_shortening` and `urlshortener_kgs` databases.

## Build (.NET, on the host)

```bash
dotnet build src/Pottmayer.UrlShortener.slnx
```

## Migrations (migris)

One migris project per database under `migrations/`. Copy `config.example.json` to `config.json`
(gitignored) and run `migris apply local` inside each database's folder.

## Status

Complete through the observability phase and the React frontend, plus a one-command Docker run
(and the Visual Studio `docker-compose` startup). See [docs/design.md](docs/design.md) §12 for the
sliced roadmap.
