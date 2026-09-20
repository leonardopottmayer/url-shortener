-- 20260919130000-create-table-key-counter.up.sql

CREATE TABLE key_counter (
    id     int     PRIMARY KEY,
    value  bigint  NOT NULL
);

-- Single global counter row. Seeded at 62^6 (56,800,235,584) so the first base62
-- code already has 7 characters and only grows to 8 far down the line.
INSERT INTO key_counter (id, value) VALUES (1, 56800235584);
