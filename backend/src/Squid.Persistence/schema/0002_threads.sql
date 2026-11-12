-- 0002_threads.sql — SPEC-0010 section A (TASK-0033), the messaging base of P-05.
-- Creates the thread aggregate of data-model section 2.7 plus the GAP-30 amendment: one row per
-- conversation carrying message_seq (the total order of FR-030) and the version column the store
-- guards every write with — GAP-29's decision puts that order in SQLite under an ETag, never in
-- a grain —, and one row per participant with its role (the creator of a group lands as admin)
-- and joined_at. created_by is the GAP-30 amendment's field: the original section 2.7 stays as
-- adopted, the amendment lives here and in the emenda text.
-- Every statement is IF NOT EXISTS, so applying it twice changes nothing on the same database:
-- the applier additionally skips a version whose checksum is recorded and refuses a text that
-- changed under it (SchemaMigrator's two rules).
-- The family 0002 rides the same human gate as 0001 (2026-11-09, dev/test/CI only); applying it
-- to PRODUCTION remains a human gate.

-- GAP-29: the thread row is the serialization point — message_seq only moves through the
-- version-guarded UPDATE of the store, so two concurrent senders never take the same number.
CREATE TABLE IF NOT EXISTS thread (
    id          TEXT    NOT NULL PRIMARY KEY,
    kind        TEXT    NOT NULL,
    created_by  TEXT    NOT NULL,
    message_seq INTEGER NOT NULL DEFAULT 0,
    created_at  TEXT    NOT NULL,
    version     INTEGER NOT NULL DEFAULT 1
);

-- data-model 2.7 + GAP-30: one row per (thread, actor); role carries the admin seat, and the
-- actor_id index is what GET /api/threads uses to list the conversations of one actor.
CREATE TABLE IF NOT EXISTS thread_participant (
    thread_id TEXT NOT NULL,
    actor_id  TEXT NOT NULL,
    role      TEXT NOT NULL,
    joined_at TEXT NOT NULL,
    PRIMARY KEY (thread_id, actor_id)
);

CREATE INDEX IF NOT EXISTS ix_thread_participant_actor ON thread_participant (actor_id);
