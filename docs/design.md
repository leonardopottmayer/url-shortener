# URL Shortener — Design

**English** · [Português](design.pt-BR.md)

> Polyglot system-design lab built on the **Tars** framework. The goal isn't
> "to have a URL shortener", it's to **exercise the decisions** a shortener
> forces: read path vs write path, caching, distributed ID generation,
> asynchronous messaging, database-per-service and a gateway.

- **Status:** design (pre-code)
- **Date:** 2026-09-19
- **Stack:** .NET (services) · React (front) · Tars (framework) · Postgres · MongoDB · Redis · RabbitMQ (or Kafka)
- **Solution:** `src/Pottmayer.UrlShortener.slnx`
- **Namespaces:** `Pottmayer.UrlShortener.*`

---

## 1. Goals and non-goals

### Goals
- Practice the **read-heavy vs write-light tension** with separate services.
- Use caching (Redis, cache-aside) as a first-class citizen on the hot path.
- Collision-free short-code generation via a **KGS** (Key Generation Service).
- **Asynchronous** analytics decoupled from the redirect, via messaging + **outbox**.
- **Polyglot persistence** / database-per-service (Postgres where transactions/outbox
  are needed, Mongo where the model is an append-heavy document).
- Exercise the Tars axes: Caching, Data (Relational + Document), Messaging
  (+ EF Core outbox), Web.Http.AspNetCore, Observability.

### Non-goals (for now)
- Multi-region / geo-DNS.
- User accounts / private links (kept as a future extension via Identity).
- Real SLA/HA — it's a lab; one node of each piece of infra is enough.
- Real Postgres sharding (we discuss the concept, we don't implement Citus).

---

## 2. Why 5 services (and being honest about the trade-off)

A real shortener would be **1–2 services**. We're slicing it into 5 **on purpose**,
because each boundary is a lesson:

| Service | Lesson it teaches |
|---|---|
| **Gateway** | edge routing, rate limiting, single entry point |
| **Shortening** (write) | source of truth, transaction, **outbox** (dual-write) |
| **Redirect** (read) | hot path, **cache-aside**, latency, 301 vs 302 |
| **Analytics** | asynchronous consumption, aggregation, its own DB |
| **KGS** | distributed collision-free ID generation, pre-allocation |

> If the infra ceremony gets heavy, the escape hatch is to collapse
> Redirect+Shortening into a single service — the boundaries were drawn to allow it.

---

## 3. Architecture

```
                         ┌──────────────────────────┐
       React SPA ───────▶│         Gateway          │  YARP
                         │   rate-limit · routing   │
                         └───────┬──────────┬───────┘
              GET /{code}        │          │   POST /api/urls
              (read, hot)        │          │   (write)
                    ┌────────────▼──┐    ┌──▼──────────────┐
                    │   Redirect    │    │   Shortening    │
                    │   Service     │    │   Service       │
                    │  cache-aside  │    │ owns the link   │
                    └───┬────────┬──┘    └──┬───────────┬──┘
                        │        │          │           │
              ┌─────────▼─┐   ┌──▼───┐   ┌──▼─────┐  ┌──▼──────┐
              │  Redis    │   │broker│   │Postgres│  │  KGS    │
              │ code→url  │   │ pub  │   │ (links)│  │ (ranges)│
              └───────────┘   └──┬───┘   └────────┘  └──┬──────┘
                                 │ UrlAccessed          │ Postgres
                        ┌────────▼────────┐             │ (counter)
                        │   Analytics     │             ▼
                        │   Service       │        Redis key
                        │  (aggregates)   │        buffer
                        └───────┬─────────┘
                                ▼
                          MongoDB (events + aggregates)
```

### Key flows

**Shorten (write):**
1. `POST /api/urls { longUrl, customAlias?, ttl? }` → Gateway → Shortening.
2. Shortening takes a short code (from the **local buffer** fed by the KGS; see §6).
3. Writes `(code, longUrl, ...)` to Postgres **in the same transaction** that enqueues
   the `UrlCreated` event in the **outbox**.
4. Responds `201 { shortUrl }`.

**Redirect (read — hot path):**
1. `GET /{code}` → Gateway → Redirect.
2. Redirect looks up **Redis** (`code→longUrl`). Hit ≈ 99% → `302`.
3. Miss → queries Postgres (via Shortening's internal API or a read replica),
   populates Redis (cache-aside), responds `302`.
4. Publishes `UrlAccessed { code, ts, ua, referrer, ip }` **fire-and-forget** to the
   broker (doesn't block the redirect).

**Analytics (async):**
1. Analytics consumes `UrlAccessed`.
2. Persists the raw event + updates aggregates (clicks per code/day) in Mongo.
3. Exposes `GET /api/stats/{code}`.

---

## 4. Services in detail

### 4.1 Gateway (`Pottmayer.UrlShortener.Gateway`)
- **Stack:** ASP.NET + **YARP** (reverse proxy).
- **Responsibilities:** routing (`/{code}`→Redirect, `/api/*`→Shortening/Analytics),
  **rate limiting** (ASP.NET RateLimiter, per IP), CORS for the SPA.
- **Stateless.** Knows no business rule.
- **Tars:** Web.Http.AspNetCore, Observability.

### 4.2 Shortening (`Pottmayer.UrlShortener.Shortening`)
- **Owner** of the `ShortLink` entity (source of truth).
- **API:** `POST /api/urls`, `GET /api/urls/{code}` (internal/admin), `DELETE /api/urls/{code}`.
- **Storage:** **Postgres** (migrations via **migris**).
- **Code generation:** consumes ranges from the KGS, keeps a buffer in memory/Redis (§6).
- **Outbox:** publishes `UrlCreated` in the same transaction as the write, via the tars relay
  (`AddTarsOutboxBrokerDelivery`) delivering to Kafka.
- **Tars:** Data.Relational, Messaging.MassTransit.Kafka (+ the tars outbox relay), Web.Http.

### 4.3 Redirect (`Pottmayer.UrlShortener.Redirect`)
- **API:** `GET /{code}` → `302 Location: longUrl` (or `404`/`410` if expired).
- **Storage:** **Redis** (primary on the read path) + a read fallback to Postgres.
- **Caching:** cache-aside, TTL, **bloom filter** against cache penetration (§7).
- Publishes `UrlAccessed` without blocking.
- **Tars:** Caching.Redis, Messaging, Web.Http, Observability.

### 4.4 Analytics (`Pottmayer.UrlShortener.Analytics`)
- **Consumer** of `UrlAccessed` (idempotent by `eventId`).
- **Storage:** **MongoDB** (its own database).
- **API:** `GET /api/stats/{code}` (total, per day, top referrers).
- **Tars:** Messaging (consumer), Data.Document.MongoDB, Web.Http.

### 4.5 KGS — Key Generation Service (`Pottmayer.UrlShortener.Kgs`)
- **Responsibility:** hand out unique short codes **without collisions**.
- **Storage:** **Postgres** (a counter/ranges table).
- **Internal API:** `POST /api/keys/allocate { size } → { rangeStart, rangeEnd }`.
- Allocates ranges transactionally (`UPDATE counter SET value = value + :size
  RETURNING`); Shortening converts each number of the range to base62 (§6).
- **Tars:** Data.Relational, Web.Http.

---

## 5. Data model

### Postgres — `shortening`
```
short_link
  code           text  PK        -- base62, e.g. "aX9k2"
  long_url       text  NOT NULL
  created_at     timestamptz
  expires_at     timestamptz NULL -- optional TTL
  custom_alias   bool             -- alias chosen by the user?
  status         text             -- active | expired | disabled
```
> Tars convention: enum values with a **hyphen** (`custom-alias` isn't one; here it's a bool,
> but `status` = `active`/`disabled`), columns in **snake_case**.

### Postgres — `kgs`
```
key_counter
  id       int  PK
  value    bigint NOT NULL   -- last allocated number
```

### MongoDB — `analytics`
```
access_event   { _id, eventId, code, ts, ua, referrer, ipHash }
click_daily    { _id: "{code}:{yyyy-mm-dd}", code, day, count }
```

---

## 6. Short-code generation — the heart of the lab

**Strategy: KGS + base62.** Rationale (comparison in §14 / ADR-002):

1. The KGS keeps a **global counter** in Postgres.
2. Shortening asks for a **range** (e.g. 1,000 numbers) and caches it locally
   (memory + Redis to survive a restart).
3. Each `POST /api/urls` consumes the **next number from the buffer** and encodes it in
   **base62** (`[0-9a-zA-Z]`, 62 symbols → 7 chars cover ~3.5 trillion).
4. Buffer running low (< threshold) → asks for a new range **in the background**.

**Wins:** zero collisions (no checking SELECT needed), zero per-request coordination
(the range is local), short codes. **Cost:** somewhat sequential codes →
mitigable by shuffling within the range or with a permuted step/base62 (kept as an
improvement; ADR-002).

**Custom alias:** a separate path — writes directly with the alias, with a `UNIQUE`
constraint in Postgres catching collisions (there a 409 error is acceptable, it's the user's choice).

---

## 7. Read path & caching

- **Cache-aside** in Redis: miss → read Postgres → populate Redis (TTL, e.g. 24h).
- **302 (Found)**, not 301, so the browser **keeps hitting us** and analytics keeps
  counting. (301 would be cached by the browser and would kill the metrics.)
- **Cache penetration:** non-existent codes hitting the database. Mitigation:
  a **bloom filter** (or a negative cache with a short TTL) → responds 404 without touching
  Postgres.
- **Expiration:** `expires_at` on the cached value; Redirect responds `410 Gone`.
- **Invalidation:** delete/disable in Shortening publishes an event → Redirect
  invalidates the key in Redis (or a short TTL resolves it lazily).

---

## 8. Messaging & events

Broker: **Kafka** (Tars Messaging.MassTransit.Kafka). All three are **events**
(facts in the past), not commands — that's why Kafka, not RabbitMQ. RabbitMQ would
only come in if an **imperative command** appeared (e.g. `SendEmail`, `GenerateQrCode`).

| Event | Producer | Consumer | Delivery |
|---|---|---|---|
| `UrlCreated { code, longUrl, ts }` | Shortening (**outbox**) | (future: cache warmup) | transactional |
| `UrlAccessed { eventId, code, ts, ua, referrer, ipHash }` | Redirect | Analytics | fire-and-forget |
| `UrlDisabled { code }` | Shortening (**outbox**) | Redirect (invalidates cache) | transactional |

- **The outbox on Kafka works** via the tars-owned relay — `AddTarsOutboxBrokerDelivery(key)`
  → `BrokerOutboxDelivery` (`IOutboxRelayDelivery`): the row is written in the same database
  transaction and drained afterwards to the Kafka topic. Proven by `OutboxKafkaDeliveryTests` in
  the tars-sandbox. **Note:** it's the tars outbox, NOT MassTransit's native `UseBusOutbox`
  — the latter doesn't cover Kafka (the `ITopicProducer`/rider doesn't go through the
  `IPublishEndpoint` it intercepts).
- `UrlAccessed` is **best-effort** — not because of a Kafka limitation, but because on a
  Redirect cache hit **there's no database transaction** to anchor an outbox; it's a direct
  publish. Losing a click isn't critical.
- **Idempotency** in Analytics via `eventId` (dedup).

---

## 9. Observability

- Tars.Observability everywhere: distributed traces (Gateway→Redirect→broker→Analytics),
  metrics (cache hit ratio, redirect latency, KGS buffer size),
  structured logs (Serilog).
- The lab's star metrics: **cache hit ratio** and **redirect p99**.

---

## 10. Infra (docker-compose)

Infra services for the local lab: `postgres`, `mongo`, `redis`, `kafka` (KRaft, no
Zookeeper), (optional) `otel-collector` + `grafana`/`prometheus`. The 5 .NET services
+ the React SPA run on top. One node of each — no HA.

---

## 11. Frontend (React)

A simple SPA: shorten form, my-links list (localStorage for now, no auth), and a stats
page consuming `GET /api/stats/{code}`. It talks **only to the Gateway**.
Vite + the stack you already use (antd/Tailwind/TanStack).

---

## 12. Sliced roadmap

Each phase delivers something runnable and isolates one lesson.

- **Phase 0 — Scaffolding.** slnx, 5 projects + tars package refs, infra docker-compose,
  `Directory.Build.props`, migris in Shortening/KGS.
- **Phase 1 — Happy path without cache.** Shortening writes to Postgres (temporary random
  code), Redirect reads straight from the database, `302`. End-to-end through the Gateway.
- **Phase 2 — KGS.** Extracts generation into the KGS (ranges + base62 + buffer). Kills
  collisions. Custom alias.
- **Phase 3 — Caching.** Redis cache-aside in Redirect, TTL, bloom filter, hit-ratio metric.
- **Phase 4 — Messaging + Analytics.** Kafka; the tars outbox in Shortening
  (`AddTarsOutboxBrokerDelivery`), `UrlAccessed`, Analytics on Mongo, `GET /api/stats`.
- **Phase 5 — Observability + Front.** Traces/metrics/Grafana, React SPA.
- **Extensions:** Identity (private links), expiration/cleanup, per-user rate-limit,
  Kafka instead of RabbitMQ, base62 permutation.

---

## 13. Conventions (inherited from tars)

- DB enums with a **hyphen** (`api-key`, `pending-confirmation`); snake_case columns.
- DI: **one extension method per piece**, no "register everything"; only the entry point
  composes. `TryAdd*`. Fail-fast on config.
- The `Options` suffix only for a class bound to an appsettings section; an action-only
  class uses `Configuration`.
- Migrations via **migris** (gitignore + `.example`).

---

## 14. Decisions (ADR summary)

- **ADR-001 — Polyglot persistence.** Postgres for Shortening+KGS (transaction +
  the tars outbox over the table + migris), Mongo for Analytics (append-heavy,
  aggregation, flexible schema). The read path doesn't suffer: Redis is in front. → *accepted*.
- **ADR-002 — KGS + base62** instead of random+collision or hash. Zero collisions without
  a checking SELECT; a local range removes per-request coordination. Cost:
  sequentiality (mitigable). → *accepted*.
- **ADR-003 — 302, not 301**, on the redirect, to preserve analytics. → *accepted*.
- **ADR-004 — `UrlAccessed` without outbox** (best-effort), `UrlCreated`/`UrlDisabled`
  with outbox (consistency). → *accepted*.
- **ADR-005 — 5 services for didactics**, with an escape hatch to collapse
  Redirect+Shortening. → *accepted*.

---

## 15. Post-design decisions & open questions

**Resolved (2026-09-19):**
- **Broker: Kafka for everything.** The deciding axis is **semantic**: Kafka is for
  **events** (facts in the past — "this happened"); RabbitMQ is for **imperative
  commands** ("do this", e.g. send an email). The three events here are facts →
  Kafka. RabbitMQ doesn't enter this project (there's no command).
- **The outbox on Kafka works in tars** — via its own relay (`AddTarsOutboxBrokerDelivery`),
  not via MassTransit's `UseBusOutbox` (which doesn't cover the Kafka rider). Proven by
  `OutboxKafkaDeliveryTests`. This corrects an old memory that said "Kafka+outbox isn't
  transactional" — true only for MassTransit's native outbox, not for the tars one.
- **Cache miss in Redirect:** reads **Postgres directly** (read-only / read replica),
  without a hop through Shortening's API. Less latency on the hot path. → *accepted*.

**Open:**
- Bloom filter in-process (per instance) or shared in Redis? (proposal:
  start in-process; simpler.)
- Plaintext IP is never stored — `ipHash` (sha256 + salt). Confirm the privacy
  granularity in analytics.
