# URL Shortener — Design

[English](design.md) · **Português**

> Lab de system design poliglota sobre o framework **Tars**. O objetivo não é
> "ter um encurtador", é **exercitar as decisões** que um encurtador força:
> read path vs write path, caching, geração distribuída de IDs, mensageria
> assíncrona, database-per-service e gateway.

- **Status:** design (pré-código)
- **Data:** 2026-09-19
- **Stack:** .NET (serviços) · React (front) · Tars (framework) · Postgres · MongoDB · Redis · RabbitMQ (ou Kafka)
- **Solution:** `src/Pottmayer.UrlShortener.slnx`
- **Namespaces:** `Pottmayer.UrlShortener.*`

---

## 1. Objetivos e não-objetivos

### Objetivos
- Praticar a **tensão read-heavy vs write-light** com serviços separados.
- Usar caching (Redis, cache-aside) como cidadão de primeira classe no hot path.
- Geração de short codes sem colisão via **KGS** (Key Generation Service).
- Analytics **assíncrono** desacoplado do redirect, via mensageria + **outbox**.
- **Polyglot persistence** / database-per-service (Postgres onde precisa de
  transação/outbox, Mongo onde o modelo é documento append-heavy).
- Exercitar os eixos do Tars: Caching, Data (Relational + Document), Messaging
  (+ outbox EF Core), Web.Http.AspNetCore, Observability.

### Não-objetivos (por ora)
- Multi-região / geo-DNS.
- Contas de usuário / links privados (fica como extensão futura via Identity).
- SLA/HA real — é um lab; um nó de cada infra basta.
- Sharding real do Postgres (discutimos o conceito, não implementamos Citus).

---

## 2. Por que 5 serviços (e a honestidade do trade-off)

Um encurtador de verdade seria **1–2 serviços**. Estamos fatiando em 5 **de
propósito**, porque cada fronteira é uma lição:

| Serviço | Lição que ensina |
|---|---|
| **Gateway** | edge routing, rate limiting, ponto único de entrada |
| **Shortening** (write) | source of truth, transação, **outbox** (dual-write) |
| **Redirect** (read) | hot path, **cache-aside**, latência, 301 vs 302 |
| **Analytics** | consumo assíncrono, agregação, DB próprio |
| **KGS** | geração distribuída de IDs sem colisão, pré-alocação |

> Se a cerimônia de infra pesar, o plano de fuga é colapsar Redirect+Shortening
> num serviço só — as fronteiras foram desenhadas pra permitir isso.

---

## 3. Arquitetura

```
                         ┌──────────────────────────┐
       React SPA ───────▶│         Gateway          │  YARP
                         │  rate-limit · roteamento │
                         └───────┬──────────┬───────┘
              GET /{code}        │          │   POST /api/urls
              (read, quente)     │          │   (write)
                    ┌────────────▼──┐    ┌──▼──────────────┐
                    │   Redirect    │    │   Shortening    │
                    │   Service     │    │   Service       │
                    │  cache-aside  │    │  dono do link   │
                    └───┬────────┬──┘    └──┬───────────┬──┘
                        │        │          │           │
              ┌─────────▼─┐   ┌──▼───┐   ┌──▼─────┐  ┌──▼──────┐
              │  Redis    │   │broker│   │Postgres│  │  KGS    │
              │ code→url  │   │ pub  │   │ (links)│  │ (ranges)│
              └───────────┘   └──┬───┘   └────────┘  └──┬──────┘
                                 │ UrlAccessed          │ Postgres
                        ┌────────▼────────┐             │ (counter)
                        │   Analytics     │             ▼
                        │   Service       │        Redis buffer
                        │  (agrega)       │        de chaves
                        └───────┬─────────┘
                                ▼
                          MongoDB (eventos + agregados)
```

### Fluxos-chave

**Encurtar (write):**
1. `POST /api/urls { longUrl, customAlias?, ttl? }` → Gateway → Shortening.
2. Shortening pega um short code (do **buffer local** abastecido pelo KGS; ver §6).
3. Grava `(code, longUrl, ...)` no Postgres **na mesma transação** que enfileira
   o evento `UrlCreated` no **outbox**.
4. Responde `201 { shortUrl }`.

**Redirecionar (read — hot path):**
1. `GET /{code}` → Gateway → Redirect.
2. Redirect consulta **Redis** (`code→longUrl`). Hit ≈ 99% → `302`.
3. Miss → consulta Postgres (via API interna do Shortening ou read replica),
   popula o Redis (cache-aside), responde `302`.
4. Publica `UrlAccessed { code, ts, ua, referrer, ip }` **fire-and-forget** no
   broker (não bloqueia o redirect).

**Analytics (async):**
1. Analytics consome `UrlAccessed`.
2. Persiste o evento cru + atualiza agregados (cliques por code/dia) no Mongo.
3. Expõe `GET /api/stats/{code}`.

---

## 4. Serviços em detalhe

### 4.1 Gateway (`Pottmayer.UrlShortener.Gateway`)
- **Stack:** ASP.NET + **YARP** (reverse proxy).
- **Responsabilidades:** roteamento (`/{code}`→Redirect, `/api/*`→Shortening/Analytics),
  **rate limiting** (ASP.NET RateLimiter, por IP), CORS pro SPA.
- **Sem estado.** Não conhece regra de negócio.
- **Tars:** Web.Http.AspNetCore, Observability.

### 4.2 Shortening (`Pottmayer.UrlShortener.Shortening`)
- **Dono** da entidade `ShortLink` (source of truth).
- **API:** `POST /api/urls`, `GET /api/urls/{code}` (interno/admin), `DELETE /api/urls/{code}`.
- **Storage:** **Postgres** (migrations via **migris**).
- **Geração de código:** consome ranges do KGS, mantém buffer em memória/Redis (§6).
- **Outbox:** publica `UrlCreated` na mesma transação da escrita, via o relay do tars
  (`AddTarsOutboxBrokerDelivery`) entregando no Kafka.
- **Tars:** Data.Relational, Messaging.MassTransit.Kafka (+ outbox relay do tars), Web.Http.

### 4.3 Redirect (`Pottmayer.UrlShortener.Redirect`)
- **API:** `GET /{code}` → `302 Location: longUrl` (ou `404`/`410` se expirado).
- **Storage:** **Redis** (primário no read path) + fallback de leitura ao Postgres.
- **Caching:** cache-aside, TTL, **bloom filter** anti cache-penetration (§7).
- Publica `UrlAccessed` sem bloquear.
- **Tars:** Caching.Redis, Messaging, Web.Http, Observability.

### 4.4 Analytics (`Pottmayer.UrlShortener.Analytics`)
- **Consumer** de `UrlAccessed` (idempotente por `eventId`).
- **Storage:** **MongoDB** (base própria).
- **API:** `GET /api/stats/{code}` (total, por dia, top referrers).
- **Tars:** Messaging (consumer), Data.Document.MongoDB, Web.Http.

### 4.5 KGS — Key Generation Service (`Pottmayer.UrlShortener.Kgs`)
- **Responsabilidade:** entregar short codes únicos **sem colisão**.
- **Storage:** **Postgres** (um contador/tabela de ranges).
- **API interna:** `POST /api/keys/allocate { size } → { rangeStart, rangeEnd }`.
- Aloca ranges de forma transacional (`UPDATE counter SET value = value + :size
  RETURNING`), Shortening converte cada número do range em base62 (§6).
- **Tars:** Data.Relational, Web.Http.

---

## 5. Modelo de dados

### Postgres — `shortening`
```
short_link
  code           text  PK        -- base62, ex: "aX9k2"
  long_url       text  NOT NULL
  created_at     timestamptz
  expires_at     timestamptz NULL -- TTL opcional
  custom_alias   bool             -- alias escolhido pelo usuário?
  status         text             -- active | expired | disabled
```
> Convenção tars: valores de enum com **hífen** (`custom-alias` não; aqui é bool,
> mas `status` = `active`/`disabled`), colunas em **snake_case**.

### Postgres — `kgs`
```
key_counter
  id       int  PK
  value    bigint NOT NULL   -- último número alocado
```

### MongoDB — `analytics`
```
access_event   { _id, eventId, code, ts, ua, referrer, ipHash }
click_daily    { _id: "{code}:{yyyy-mm-dd}", code, day, count }
```

---

## 6. Geração de short code — o coração do lab

**Estratégia: KGS + base62.** Racional (comparação no §14 / ADR-002):

1. KGS mantém um **contador global** no Postgres.
2. Shortening pede um **range** (ex.: 1.000 números) e o cacheia localmente
   (memória + Redis pra sobreviver a restart).
3. Cada `POST /api/urls` consome o **próximo número do buffer** e o codifica em
   **base62** (`[0-9a-zA-Z]`, 62 símbolos → 7 chars cobrem ~3.5 trilhões).
4. Buffer acabando (< limiar) → pede novo range **em background**.

**Ganhos:** zero colisão (não precisa de SELECT de checagem), zero coordenação
por request (o range é local), curto. **Custo:** códigos meio sequenciais →
mitigável embaralhando dentro do range ou com passo/base62 permutado (fica como
melhoria; ADR-002).

**Custom alias:** caminho separado — grava direto com o alias, com `UNIQUE`
constraint no Postgres pegando colisão (aí sim erro 409 é aceitável, é escolha do usuário).

---

## 7. Read path & caching

- **Cache-aside** no Redis: miss → lê Postgres → popula Redis (TTL, ex. 24h).
- **302 (Found)**, não 301, pra o browser **continuar batendo** e o analytics
  seguir contando. (301 seria cacheado pelo browser e mataria as métricas.)
- **Cache-penetration:** códigos inexistentes batendo no banco. Mitigação:
  **bloom filter** (ou cache de negativos com TTL curto) → responde 404 sem tocar
  o Postgres.
- **Expiração:** `expires_at` no valor cacheado; Redirect responde `410 Gone`.
- **Invalidação:** delete/disable no Shortening publica evento → Redirect
  invalida a chave no Redis (ou TTL curto resolve de forma preguiçosa).

---

## 8. Mensageria & eventos

Broker: **Kafka** (Tars Messaging.MassTransit.Kafka). Todos os três são **eventos**
(fatos no passado), não comandos — por isso Kafka, não RabbitMQ. RabbitMQ só
entraria se surgisse um **comando imperativo** (ex.: `SendEmail`, `GenerateQrCode`).

| Evento | Produtor | Consumidor | Entrega |
|---|---|---|---|
| `UrlCreated { code, longUrl, ts }` | Shortening (**outbox**) | (futuro: cache warmup) | transacional |
| `UrlAccessed { eventId, code, ts, ua, referrer, ipHash }` | Redirect | Analytics | fire-and-forget |
| `UrlDisabled { code }` | Shortening (**outbox**) | Redirect (invalida cache) | transacional |

- **Outbox no Kafka funciona** via o relay próprio do tars — `AddTarsOutboxBrokerDelivery(key)`
  → `BrokerOutboxDelivery` (`IOutboxRelayDelivery`): a linha é gravada na mesma transação
  do banco e drenada depois pro tópico Kafka. Provado por `OutboxKafkaDeliveryTests` no
  tars-sandbox. **Atenção:** é o outbox do tars, NÃO o `UseBusOutbox` nativo do MassTransit
  — esse último não pega o Kafka (o `ITopicProducer`/rider não passa pelo `IPublishEndpoint`
  que ele intercepta).
- `UrlAccessed` é **best-effort** — não por limitação do Kafka, mas porque no cache hit do
  Redirect **não há transação de banco** pra ancorar um outbox; é publish direto. Perder um
  clique não é crítico.
- **Idempotência** no Analytics via `eventId` (dedup).

---

## 9. Observabilidade

- Tars.Observability em todos: traces distribuídos (Gateway→Redirect→broker→Analytics),
  métricas (taxa de hit do cache, latência do redirect, tamanho do buffer do KGS),
  logs estruturados (Serilog).
- Métrica-estrela do lab: **cache hit ratio** e **p99 do redirect**.

---

## 10. Infra (docker-compose)

Serviços de infra pro lab local: `postgres`, `mongo`, `redis`, `kafka` (KRaft, sem
Zookeeper), (opcional) `otel-collector` + `grafana`/`prometheus`. Os 5 serviços .NET
+ o SPA React sobem por cima. Um nó de cada — sem HA.

---

## 11. Frontend (React)

SPA simples: formulário de encurtar, lista dos meus links (localStorage por
enquanto, sem auth), e página de stats consumindo `GET /api/stats/{code}`.
Fala **só com o Gateway**. Vite + o stack que você já usa (antd/Tailwind/TanStack).

---

## 12. Roadmap fatiado

Cada fase entrega algo rodável e isola uma lição.

- **Fase 0 — Scaffolding.** slnx, 5 projetos + refs de pacotes tars, docker-compose
  da infra, `Directory.Build.props`, migris no Shortening/KGS.
- **Fase 1 — Caminho feliz sem cache.** Shortening grava no Postgres (código random
  provisório), Redirect lê direto do banco, `302`. Ponta a ponta pelo Gateway.
- **Fase 2 — KGS.** Extrai a geração pro KGS (ranges + base62 + buffer). Mata a
  colisão. Custom alias.
- **Fase 3 — Caching.** Redis cache-aside no Redirect, TTL, bloom filter, métrica
  de hit ratio.
- **Fase 4 — Mensageria + Analytics.** Kafka; outbox do tars no Shortening
  (`AddTarsOutboxBrokerDelivery`), `UrlAccessed`, Analytics no Mongo, `GET /api/stats`.
- **Fase 5 — Observabilidade + Front.** Traces/métricas/Grafana, SPA React.
- **Extensões:** Identity (links privados), expiração/limpeza, rate-limit por
  usuário, Kafka no lugar de RabbitMQ, permutação base62.

---

## 13. Convenções (herdadas do tars)

- Enum no DB com **hífen** (`api-key`, `pending-confirmation`); colunas snake_case.
- DI: **um método de extensão por peça**, sem "registra tudo"; só o entry point
  compõe. `TryAdd*`. Fail-fast na config.
- Sufixo `Options` só pra classe bindada a section do appsettings; classe
  só-ação usa `Configuration`.
- Migrations via **migris** (gitignore + `.example`).

---

## 14. Decisões (ADR resumido)

- **ADR-001 — Polyglot persistence.** Postgres p/ Shortening+KGS (transação +
  outbox do tars sobre a tabela + migris), Mongo p/ Analytics (append-heavy,
  agregação, schema flexível). Read path não sofre: Redis está na frente. → *aceito*.
- **ADR-002 — KGS + base62** em vez de random+colisão ou hash. Zero colisão sem
  SELECT de checagem; range local elimina coordenação por request. Custo:
  sequencialidade (mitigável). → *aceito*.
- **ADR-003 — 302, não 301**, no redirect, pra preservar analytics. → *aceito*.
- **ADR-004 — `UrlAccessed` sem outbox** (best-effort), `UrlCreated`/`UrlDisabled`
  com outbox (consistência). → *aceito*.
- **ADR-005 — 5 serviços por didática**, com plano de fuga p/ colapsar
  Redirect+Shortening. → *aceito*.

---

## 15. Decisões pós-design & questões em aberto

**Resolvido (2026-09-19):**
- **Broker: Kafka pra tudo.** O eixo de escolha é **semântico**: Kafka é pra
  **eventos** (fatos no passado — "isso aconteceu"); RabbitMQ é pra **comandos
  imperativos** ("faça isso", ex.: enviar email). Os três eventos aqui são fatos →
  Kafka. RabbitMQ não entra neste projeto (não há comando).
- **Outbox no Kafka funciona no tars** — via o relay próprio (`AddTarsOutboxBrokerDelivery`),
  não via o `UseBusOutbox` do MassTransit (esse não pega o rider Kafka). Provado por
  `OutboxKafkaDeliveryTests`. Corrige memória antiga que dizia "Kafka+outbox não é
  transacional" — verdade só pro outbox nativo do MassTransit, não pro do tars.
- **Cache miss no Redirect:** lê o **Postgres direto** (read-only / read replica),
  sem hop pela API do Shortening. Menos latência no hot path. → *aceito*.

**Em aberto:**
- Bloom filter em processo (por instância) ou compartilhado no Redis? (proposta:
  começar em processo; simples.)
- IP em claro nunca é gravado — `ipHash` (sha256 + salt). Confirmar granularidade
  de privacidade no analytics.
