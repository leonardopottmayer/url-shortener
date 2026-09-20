-- 20260919120000-create-table-short-link.up.sql

CREATE TABLE short_link (
    code        varchar(16)  PRIMARY KEY,
    long_url    text         NOT NULL,
    created_at  timestamptz  NOT NULL
);
