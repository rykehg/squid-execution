-- 0008_federation.sql — SPEC-0011 sections F/G (TASK-0045), data-model §2.9 and §3.4.
-- peer_delivery is the outbound queue THIS task writes: one row per (activityId,
-- recipientActorId) — the pair R-05 fixes — carrying the raw JSON-LD that goes out (R-13: the
-- payload is frozen at enqueue, signed only at send), the policy version in force at enqueue
-- (what the re-evaluation compares against), and the Pending|Sent|Failed|Cancelled machine of
-- data-model §3.4 with its audit reason. activity_type/object_id are this task's columns beyond
-- §2.9: the Delete fan-out (R-08) has to find every recipient that received a Create of that
-- object without parsing payloads, so the state machine stays queryable.
-- peer is the row home of §2.9's Peer (Status/BackoffUntil/FailureCount/LastSuccessAt/
-- RateLimitPerMinute) — created here because the family is immutable by checksum: a later task
-- would have to add 0009 for a table nobody could slip in afterwards. The motor that fills it
-- (backoff, circuit, DLQ, per-pair rate) is TASK-0046.
-- follow is the relation table the phase reserves for the (handle, direction, actor) edge.
-- TASK-0045 does NOT write it: the outbound path adopts IFollowStore — the document store
-- TASK-0044 delivered over the grain storage — so callers never change, and no code touches
-- this table yet; it exists so the consolidation of the family has one home to move to.
-- Every statement is IF NOT EXISTS, so applying it twice changes nothing on the same database:
-- the applier additionally skips a version whose checksum is recorded and refuses a text that
-- changed under it (SchemaMigrator's two rules).
-- The family 0008 rides the same human gate as 0001..0007 (2026-11-09, dev/test/CI only);
-- applying it to PRODUCTION remains a human gate.

-- data-model §2.9: the pair that identifies one delivery, plus which activity of which object
-- it carries. The payload is the JSON-LD exactly as it will be POSTed (leitura tolerante do
-- contrato §6 is the receiving side's problem, not this column's).
CREATE TABLE IF NOT EXISTS peer_delivery (
    activity_id               TEXT    NOT NULL,
    recipient_actor_id        TEXT    NOT NULL,
    activity_type             TEXT    NOT NULL,
    object_id                 TEXT    NOT NULL,
    payload                   TEXT    NOT NULL,
    policy_version_at_enqueue INTEGER NOT NULL,
    attempts                  INTEGER NOT NULL DEFAULT 0,
    next_attempt_at           TEXT    NOT NULL,
    state                     TEXT    NOT NULL,
    reason                    TEXT,
    PRIMARY KEY (activity_id, recipient_actor_id)
);

-- The Delete fan-out reads by object (every Create recipient of a removed post); the motor
-- later reads the queue by state. shortcut: no index on state yet — the queue is per-instance
-- and small until TASK-0046 gives it a drain loop worth indexing for.
CREATE INDEX IF NOT EXISTS ix_peer_delivery_object ON peer_delivery (object_id, activity_type);

-- data-model §2.9: one row per remote domain — Status Healthy|Degraded|Down|Blocked, the
-- backoff window, the failure counter and the per-pair quota (contract §3 step 5).
CREATE TABLE IF NOT EXISTS peer (
    domain                 TEXT    NOT NULL PRIMARY KEY,
    status                 TEXT    NOT NULL,
    backoff_until          TEXT,
    failure_count          INTEGER NOT NULL DEFAULT 0,
    last_success_at        TEXT,
    rate_limit_per_minute  INTEGER NOT NULL DEFAULT 60
);

-- The follow relation's table home (SPEC-0011 section E/F): inbound followers and outbound
-- following share the key (handle, direction, actor_uri); established marks the Accept already
-- seen. Reserved — IFollowStore's documents are the port this task adopts, so this table stays
-- empty until the consolidation that decides whether the documents move here.
CREATE TABLE IF NOT EXISTS follow (
    handle      TEXT    NOT NULL,
    direction   TEXT    NOT NULL,
    actor_uri   TEXT    NOT NULL,
    established INTEGER NOT NULL DEFAULT 0,
    created_at  TEXT    NOT NULL,
    PRIMARY KEY (handle, direction, actor_uri)
);
