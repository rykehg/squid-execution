-- 0009_instance_signing_keys.sql — SPEC-0011 section B, "duplo embrulho no registro"
-- (TASK-0051, decisão humana do GAP-50 de 2026-11-14, opção 1).
-- instance_signing_keys is the ONLY home of the second wrap: when Squid:RecoveryKey is
-- configured, registration wraps the actor's own RSA ALSO with HKDF(RK_instance, "fed/rsa-wrap")
-- and persists just that wrapped copy here. Nothing else changes: the actor's event stream keeps
-- carrying the per-actor wrap and the kit keeps its EncryptedRootKey + wrapped RK (GAP-01), so
-- restore keeps working exactly as before. No column can hold a private key in clear — the table
-- stores wrapped material only, is read at activation and served by no route (SPEC-0011 section
-- B: cópia nunca em claro, nunca logada, nunca servida).
-- handle is the local actor the copy belongs to; wrapped_signing_key is the base64 AES-GCM box the
-- boot secret opens. An actor registered BEFORE the secret existed has NO row — its federated
-- output stays disabled with an auditable warning (limite aceito pelo humano no gate GAP-50), and
-- registering AFTER the configuration is what births a row.
-- Every statement is IF NOT EXISTS, so applying it twice changes nothing on the same database:
-- the applier additionally skips a version whose checksum is recorded and refuses a text that
-- changed under it (SchemaMigrator's two rules).
-- The family 0009 rides the same human gate as 0001..0008 (2026-11-09, dev/test/CI only);
-- applying it to PRODUCTION remains a human gate.

CREATE TABLE IF NOT EXISTS instance_signing_keys (
    handle              TEXT NOT NULL PRIMARY KEY,
    wrapped_signing_key TEXT NOT NULL
);
