-- 0010_backup.sql — SPEC-0012 section A (TASK-0054), data-model section 2.10.
-- backup_envelope is the registry of what was backed up and verified: one row per envelope with
-- exactly the columns 2.10 fixes (Id, KeyEpoch, SeqFrom/SeqTo, ContentHash, SizeBytes, TargetId,
-- CreatedAt, VerifiedAt). key_epoch is NOT NULL, so "nenhum envelope sem KeyEpoch valida" is
-- enforced by the column itself — a row without an epoch cannot be written at all (AC-01).
-- VerifiedAt stays NULL until the re-read after PutAsync confirms the hash; a divergent hash
-- (SPEC-0012 section A: estado Corrupt) leaves it NULL, and a Corrupt envelope is never used by
-- restore (contract backup-recovery section 3).
-- key_epoch is the epoch ledger 2.10 puts beside it: the epoch number is the key, CreatedAt when
-- the chain started, PublicFingerprint identifies the material without exposing it, RetiredAt when
-- a later epoch replaced it. Rows are written by key rotation (TASK-0058), never here.
-- Every statement is IF NOT EXISTS, so applying the family twice changes nothing on the same
-- database: the applier additionally skips a version whose checksum is recorded and refuses a
-- text that changed under it (SchemaMigrator's two rules).
-- The family 0010 rides the same human gate as 0001..0009 (2026-11-09, dev/test/CI only);
-- applying it to PRODUCTION remains a human gate.

CREATE TABLE IF NOT EXISTS backup_envelope (
    id           TEXT    NOT NULL PRIMARY KEY,
    key_epoch    INTEGER NOT NULL,
    seq_from     INTEGER NOT NULL,
    seq_to       INTEGER NOT NULL,
    content_hash TEXT    NOT NULL,
    size_bytes   INTEGER NOT NULL,
    target_id    TEXT    NOT NULL,
    created_at   TEXT    NOT NULL,
    verified_at  TEXT
);

CREATE TABLE IF NOT EXISTS key_epoch (
    epoch              INTEGER NOT NULL PRIMARY KEY,
    created_at         TEXT    NOT NULL,
    public_fingerprint TEXT    NOT NULL,
    retired_at         TEXT
);
