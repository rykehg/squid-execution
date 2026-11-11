-- 0001_init_projections.sql — SPEC-0009 section B, the first versioned script of the project.
-- Creates in ONE version: the read projections of data-model section 4 (timeline_item and
-- home_timeline, each indexed on seq), the single-row global counter of Activity.Seq (GAP-23)
-- and the quota ledger of section F (GAP-20). schema_version — version, name, SHA-256 of this
-- text, instant applied — is the applier's own ledger (SchemaMigrator creates it and records
-- every run there), which is why the scripts below only carry the tables they own.
-- Every statement is IF NOT EXISTS / ON CONFLICT DO NOTHING, so applying it twice on the same
-- database changes nothing: the applier additionally skips any version whose checksum is already
-- recorded, and refuses a text that changed under a recorded version.
-- Liberated for dev/test/CI by the human gate of 2026-11-09 (GAP-19); applying it to PRODUCTION
-- remains a human gate.

-- GAP-23: one row (id = 1), one number. It only ever moves through the atomic single-row
-- UPDATE of the append path — never read-modify-write in the application.
CREATE TABLE IF NOT EXISTS activity_counter (
    id       INTEGER PRIMARY KEY CHECK (id = 1),
    last_seq INTEGER NOT NULL DEFAULT 0
);

INSERT INTO activity_counter (id, last_seq) VALUES (1, 0)
ON CONFLICT (id) DO NOTHING;

-- data-model section 4: feed projection, key (container_id, seq), used by GET feed?since=.
-- seq is the GLOBAL Activity.Seq (GAP-23) so a delta is comparable across the whole log;
-- object_seq keeps the per-container Object.Seq of data-model section 2.5 for the summary.
-- The counters are the Counts tuple (likes, replies, shares): derived, never a source of truth.
CREATE TABLE IF NOT EXISTS timeline_item (
    container_id   TEXT    NOT NULL,
    seq            INTEGER NOT NULL,
    object_seq     INTEGER NOT NULL,
    post_id        TEXT    NOT NULL,
    author         TEXT    NOT NULL,
    content        TEXT    NOT NULL,
    depth          INTEGER NOT NULL DEFAULT 0,
    reply_to_id    TEXT,
    like_count     INTEGER NOT NULL DEFAULT 0,
    reply_count    INTEGER NOT NULL DEFAULT 0,
    share_count    INTEGER NOT NULL DEFAULT 0,
    visibility     TEXT    NOT NULL,
    policy_version INTEGER NOT NULL DEFAULT 0,
    created_at     TEXT    NOT NULL,
    PRIMARY KEY (container_id, seq)
);

CREATE INDEX IF NOT EXISTS ix_timeline_item_seq ON timeline_item (seq);

-- data-model section 4: personal timeline, key (actor_id, seq), used by GET /api/timeline?since=.
-- It carries the pointer (container_id, post_id); the summary itself lives once, in timeline_item,
-- so one post is stored once no matter how many members it reaches.
CREATE TABLE IF NOT EXISTS home_timeline (
    actor_id     TEXT    NOT NULL,
    seq          INTEGER NOT NULL,
    container_id TEXT    NOT NULL,
    post_id      TEXT    NOT NULL,
    PRIMARY KEY (actor_id, seq)
);

CREATE INDEX IF NOT EXISTS ix_home_timeline_seq ON home_timeline (seq);

-- SPEC-0009 section F (GAP-20): instance-scoped ledger in bytes, default limit 10 GiB.
-- The upload gate increments it (TASK-0028); the daily blob sweep is the source of truth.
CREATE TABLE IF NOT EXISTS quota_ledger (
    id          INTEGER PRIMARY KEY CHECK (id = 1),
    used_bytes  INTEGER NOT NULL DEFAULT 0,
    limit_bytes INTEGER NOT NULL,
    updated_at  TEXT    NOT NULL
);

INSERT INTO quota_ledger (id, used_bytes, limit_bytes, updated_at)
VALUES (1, 0, 10737418240, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
ON CONFLICT (id) DO NOTHING;
