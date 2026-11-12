-- 0006_message_reaction.sql — SPEC-0010 section F (TASK-0038), FR-058 + data-model section 2.14.
-- A reaction is ONE row per (message, actor, emoji): the primary key IS the invariant — repeating
-- the same triple inserts nothing (ON CONFLICT DO NOTHING), removing a reaction deletes the row
-- (never decrements a counter), and the count of the read envelope is COUNT(*) of the rows that
-- exist right now. There is deliberately NO counter column on message or here: counts.reactions is
-- a projection reconciled at read time, never a source (data-model section 2.14).
-- emoji is a value of the instance whitelist (Reactions:AllowedEmojis, GAP-34), kind is the
-- section 2.14 Like|Custom word ("like" for the default 👍, "custom" for the rest); this phase has
-- no federated distinction (GAP-34 notes), and a message reaction never leaves the thread that
-- holds it — a thread does not federate (section 2.14 invariants).
-- This is a NEW version, not an edit of 0002..0005: SchemaMigrator's rule says a script that has
-- run is immutable and is followed by a higher number, so the family grows from five scripts to
-- six instead of rewriting a recorded checksum.
-- Every statement is IF NOT EXISTS, so applying it twice changes nothing on the same database:
-- the applier additionally skips a version whose checksum is recorded and refuses a text that
-- changed under it (SchemaMigrator's two rules).
-- The family 0006 rides the same human gate as 0001..0005 (2026-11-09, dev/test/CI only); applying
-- it to PRODUCTION remains a human gate.

CREATE TABLE IF NOT EXISTS message_reaction (
    message_id TEXT    NOT NULL,
    actor_id   TEXT    NOT NULL,
    emoji      TEXT    NOT NULL,
    kind       TEXT    NOT NULL,
    created_at TEXT    NOT NULL,
    PRIMARY KEY (message_id, actor_id, emoji)
);
