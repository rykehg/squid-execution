-- 0003_messages.sql — SPEC-0010 section B (TASK-0034), the message row of data-model section 2.5.1.
-- One row per message of a thread: the thread that owns the total order (FR-030), the sender, the
-- Content sanitizado on the server (GAP-24's rule already ran before the row is written) and seq —
-- the number the store allocated under the thread's version guard (GAP-29), taken together with the
-- row in one transaction so an allocated number is never visible without its message.
-- This is a NEW version, not an edit of 0002: SchemaMigrator's own rule says a script that has run
-- is immutable and is followed by a higher number — 0002 never carried a message table, so the
-- family grows from two scripts to three instead of rewriting a recorded checksum.
-- Every statement is IF NOT EXISTS, so applying it twice changes nothing on the same database:
-- the applier additionally skips a version whose checksum is recorded and refuses a text that
-- changed under it (SchemaMigrator's two rules).
-- The family 0003 rides the same human gate as 0001/0002 (2026-11-09, dev/test/CI only); applying
-- it to PRODUCTION remains a human gate.

CREATE TABLE IF NOT EXISTS message (
    id              TEXT    NOT NULL PRIMARY KEY,
    thread_id       TEXT    NOT NULL,
    author_actor_id TEXT    NOT NULL,
    content         TEXT    NOT NULL,
    seq             INTEGER NOT NULL,
    created_at      TEXT    NOT NULL
);

-- The pagination index of SPEC-0010's non-functional section, (thread_id, seq): the delta read
-- (WHERE thread_id = ? AND seq > ?, contract §9.3) walks it, and the UNIQUE is the last line of
-- defence behind the CAS of GAP-29 — the same number can never be stored twice for a thread.
CREATE UNIQUE INDEX IF NOT EXISTS ix_message_thread_seq ON message (thread_id, seq);
