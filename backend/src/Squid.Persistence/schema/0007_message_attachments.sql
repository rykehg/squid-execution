-- 0007_message_attachments.sql — SPEC-0010 section G (TASK-0039), GAP-35 emenda (b) + data-model §2.6.
-- A mensagem só REFERENCIA as mídias já subidas em POST /api/media (upload, magic bytes, EXIF, Alt
-- e cota 507 continuam intocados da P-04): a coluna guarda a lista `Attachment` de §2.6 montada na
-- publicação da mensagem, no mesmo formato em que o post embarca a sua (PostObject.Attachments é a
-- lista dentro do próprio documento — aqui a lista dentro da própria linha, espelho direto).
-- NULL em toda linha escrita antes desta versão e em mensagem sem anexo — "sem anexo" é o que NULL
-- já significava, nenhum backfill. O inverso do GAP-25 ("quem referencia este hash") é lido com
-- json_each na coluna, não por índice separado: uma linha por mensagem, como a família mensageira
-- já traz (0003/0005).
-- Sem IF NOT EXISTS de propósito: SQLite não tem "ALTER TABLE ... ADD COLUMN IF NOT EXISTS". A
-- idempotência são as duas regras do SchemaMigrator — uma versão registrada é pulada, então este
-- comando roda UMA vez por banco, e um texto editado sob uma versão registrada é recusado (é por
-- isso que 0002..0006 ficam byte-idênticos e intocados).
-- Esta é uma versão NOVA, não uma edição de 0002..0006: a família sai de seis scripts para sete
-- em vez de reescrever um checksum registrado.
-- The family 0007 rides the same human gate as 0001..0006 (2026-11-09, dev/test/CI only); applying
-- it to PRODUCTION remains a human gate.

ALTER TABLE message ADD COLUMN attachments TEXT;
