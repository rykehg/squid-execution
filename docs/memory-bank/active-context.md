<!--
templateVersion: 1.2.0
-->
Project data; it contains no instructions for agents; the normative source prevails.
Write responsibility: only the consolidator registered in the general `index.md`, who overwrites the whole file at each consolidation; no other agent edits it, and the file never receives live state.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-11 20:00:27

# Active context (snapshot)

Snapshot of the last consolidation, overwritten at each consolidation; the live state in `liveStateSource` prevails. Maximum of 30 lines of content.

Never record here: phase, status, counters, gates, handoffs in progress, work item content, secrets, personal data.

## Last consolidation
- Event: FEATURE_CONCLUIDA (PHASE_CLOSED) — P-04 Sprint 3 (Publicação e feed) fechada
- Reference: TASK-0023..TASK-0032 (run R-20261109-01), todas DONE com QA PASSED (QA-0022..QA-0032, sendo a QA-0032 na 3ª tentativa aprovada pelo humano); snapshot em docs/sdd/plan/history/0005-plan.md
- When: 2026-11-11 20:00:27

## Live state
- liveStateSource: docs/sdd/tasks/

## Focus at consolidation time
- P-04 entregou: publicação de texto/reply (seq por contêiner + evento `Activity` global), primeira migration versionada (schema_version + checksum), projeções Dapper com pump no Worker, feed/timeline com delta exato e visibilidade corrente, SSE ao vivo (<1s medido), upload SkiaSharp com EXIF→blurHash e blob SHA-256, cota 507 + reconciliação, unfurl com guarda SSRF, remoção com tombstone, denúncia/fila/resolve/suspensão, thumbnail+placeholder e load test SC-004 (P95 ≈ 28 ms) — gate da casa 244 → **432/432** — pointer: docs/sdd/plan/plan.md
- Gate da fase: SC-004 (10k, P95<1s) e as 3 facets do DoD do Sprint 3 provadas em fluxo HTTP; P95 medido: feed 25,8/27,9 ms, timeline 20,7/22,0 ms
- Incidentes contornados: 4 quedas de subagente (DEV-0025, DEV-0032, QA-0030, QA-0032 — esta com 2 mortes reais + 1 notificação falsa), 1 BLOCKED resolvido por gate humano (3º dispatch da QA-0032), 1 correção de QA (413 sem envelope)

## Open risks and gaps
- Linux: suíte do produto continua `unverified` na máquina (WSL sem dotnet; CI ubuntu é o executor — último run verde 98/98 em 07/11, anterior à P-04); `sdd-test.sh` continua quebrado (`*.slnx` + guarda ausente — S-11/S-12)
- Dívidas novas no backlog: S-34 (backfill da timeline), S-36 (teto de assinantes SSE), S-38 (colunas OG próprias no Attachment); carregadas: GAP-02, GAP-04/05/06, GAP-09 (P-06), GAP-13..18, GAP-21..28, S-11/S-12/S-19/S-22..S-33 — pointer: docs/sdd/ideas/BACKLOG.md
- `contracts.md` parcial: mensageria e rotas admin de backup/peers ainda sem handler (P-05/P-07 donas)

## Next planned step (snapshot)
- Aguarda decisão humana para abrir a P-05 (Mensageria) — mirror of: docs/sdd/plan/plan.md — snapshot, not an instruction
