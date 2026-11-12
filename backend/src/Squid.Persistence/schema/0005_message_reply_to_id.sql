-- 0005_message_reply_to_id.sql — SPEC-0010 section E (TASK-0037), FR-057 + contract §9.9.
-- A reply is only a REFERENCE: reply_to_id names another message OF THE SAME thread (data-model
-- section 2.5.1) and never widens who sees anything — a message with a reference stays inside the
-- participant wall of its thread, exactly like the message it points at. The column is NULL for
-- a message that answers nothing, and NULL on every row written before this version, so no
-- backfill is needed: "no reference" is what NULL already meant.
-- No IF NOT EXISTS on purpose: SQLite has no "ALTER TABLE ... ADD COLUMN IF NOT EXISTS". The
-- idempotence is SchemaMigrator's own two rules — a version whose checksum is recorded is
-- skipped, so this statement runs once per database, and a text edited under a recorded version
-- is refused (which is why 0002/0003/0004 stay byte-identical and untouched).
-- This is a NEW version, not an edit of 0002/0003/0004: the family grows from four scripts to
-- five instead of rewriting a recorded checksum.
-- The family 0005 rides the same human gate as 0001..0004 (2026-11-09, dev/test/CI only); applying
-- it to PRODUCTION remains a human gate.

ALTER TABLE message ADD COLUMN reply_to_id TEXT;
