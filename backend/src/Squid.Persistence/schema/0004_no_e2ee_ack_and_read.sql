-- 0004_no_e2ee_ack_and_read.sql — SPEC-0010 section D (TASK-0036): the consent of FR-032 and the
-- delivery state of FR-033, one row per (thread, actor).
-- thread_no_e2ee_ack is the acceptance the §9.6 gate reads BEFORE any append — a sender without
-- its row is refused with 428 precondition_required —, and its PRIMARY KEY is what makes the
-- route idempotent (repeating the ack changes nothing, one row per pair — GAP-37 keeps only the
-- record here; the UI that SHOWS the notice comes in P-08).
-- thread_read is the monotonic read marker of FR-033: last_read_seq only ever moves forward
-- (GAP-38), it is one value per participant — no per-device state (GAP-33).
-- This is a NEW version, not an edit of 0002/0003: SchemaMigrator's own rule says a script that
-- has run is immutable and is followed by a higher number, so the family grows from three scripts
-- to four instead of rewriting a recorded checksum.
-- Every statement is IF NOT EXISTS, so applying it twice changes nothing on the same database:
-- the applier additionally skips a version whose checksum is recorded and refuses a text that
-- changed under it (SchemaMigrator's two rules).
-- The family 0004 rides the same human gate as 0001..0003 (2026-11-09, dev/test/CI only); applying
-- it to PRODUCTION remains a human gate.

CREATE TABLE IF NOT EXISTS thread_no_e2ee_ack (
    thread_id TEXT   NOT NULL,
    actor_id  TEXT   NOT NULL,
    acked_at  TEXT   NOT NULL,
    notice    TEXT   NOT NULL,
    PRIMARY KEY (thread_id, actor_id)
);

-- The marker of section D: INTEGER because seq is, one row per (thread, actor) — the primary key
-- is also what the monotonic upsert conflicts on (a smaller value never moves it back).
CREATE TABLE IF NOT EXISTS thread_read (
    thread_id     TEXT    NOT NULL,
    actor_id      TEXT    NOT NULL,
    last_read_seq INTEGER NOT NULL,
    PRIMARY KEY (thread_id, actor_id)
);
