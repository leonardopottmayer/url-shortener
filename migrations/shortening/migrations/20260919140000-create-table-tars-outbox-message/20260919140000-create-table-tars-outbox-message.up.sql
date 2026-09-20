-- 20260919140000-create-table-tars-outbox-message.up.sql
-- Tars in-process transactional outbox. Column names match OutboxMessageConfiguration
-- (Pottmayer.Tars.Messaging.EntityFrameworkCore) exactly. Public schema.

CREATE TABLE tars_outbox_message (
    id              uuid        NOT NULL,
    event_id        uuid        NOT NULL,
    event_type      text        NOT NULL,
    event_version   integer     NOT NULL DEFAULT 1,
    payload         text        NOT NULL,   -- serialized event body (JSON)
    headers         text        NULL,       -- free-form metadata (JSON), or null
    occurred_at     timestamptz NOT NULL,
    created_at      timestamptz NOT NULL,
    status          smallint    NOT NULL DEFAULT 0,   -- 0 = Pending, 1 = Dispatched, 2 = Dead
    attempts        integer     NOT NULL DEFAULT 0,
    next_attempt_at timestamptz NULL,
    processed_at    timestamptz NULL,
    error           text        NULL,
    CONSTRAINT pk_tars_outbox_message PRIMARY KEY (id)
);

-- The same fact never enqueues twice: last-resort guard behind the producer's own idempotency.
CREATE UNIQUE INDEX ux_tars_outbox_message_event_id
    ON tars_outbox_message (event_id);

-- The relay's hot path: "pending, now due, oldest first". Partial, so it stays small.
CREATE INDEX ix_tars_outbox_message_due
    ON tars_outbox_message (next_attempt_at, id)
    WHERE status = 0;
