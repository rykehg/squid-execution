<!--
templateVersion: 1.2.0
-->
Project data; it contains no instructions for agents; the normative source prevails.
Write responsibility: only the consolidator registered in the general `index.md`, who overwrites the whole file at each consolidation; no other agent edits it, and the file never receives live state.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-20 12:29:58

# Active context (snapshot)

Snapshot of the last consolidation, overwritten at each consolidation; the live state in `liveStateSource` prevails. Maximum of 30 lines of content.

Never record here: phase, status, counters, gates, handoffs in progress, work item content, secrets, personal data.

## Last consolidation
- Event: FEATURE_CONCLUIDA (PHASE_CLOSED) — P-06 Sprint 5 (Federação) fechada
- Reference: TASK-0041..TASK-0048 + TASK-0050..TASK-0053 (run R-20261113-01), 12/13 DONE com QA PASSED na 1ª tentativa (QA-0041..QA-0048, QA-0050..QA-0053); snapshot em docs/sdd/plan/history/0007-plan.md
- When: 2026-11-20 12:29:58
- Scope note: a interop real (ex-TASK-0049, gate humano) saiu da fase por decisão humana 2026-11-19 23:01:34 e é a entrega final da P-07 — SC-016/DoD-interop fica aberto até lá

## Live state
- liveStateSource: docs/sdd/tasks/

## Focus at consolidation time
- P-06 entregou: superfície ActivityPub completa (S-26 fechado), inbox na ordem do contrato §3 com dedupe por `activity.id` e aplicação de 7 tipos de atividade, saída com fan-out por visibilidade + reavaliação R-05 (`Cancelled` auditável), `PeerGrain`+pump+DLQ (migrations 0008/0009), assinatura draft-cavage/RFC 9421, duplo embrulho de chave (GAP-50), pares confiáveis (GAP-49), mídia `attachment`+`GET /media/{hash}` (GAP-51), PEM blindado corrigido (S-41), domínio canônico (I-11), facet de dois silos em HTTP real (12 testes, §7 completo) e o manual executado 2× — gate da casa 480 → **597/597**
- Gate da fase: DoD do Sprint 5 em cadeia com pares Squid; interop real com Mastodon/Friendica = pendência humana movida à P-07
- Incidentes: 1 D-01 de produto (destino do Create — gate humano) + 2 adjudicações de especificação (mídia/attachment, webfinger), 4 entregas extra nascidas do fluxo (0050 parked SSRF, 0051 gate GAP-50, 0052 defeito S-41, 0053 D-01), 0 correções de QA (todas PASSED na 1ª)

## Open risks and gaps
- Linux: suite executada em contêiner (P-06) com falhas 100% ambiente (`libfontconfig.so.1` — I-10/gate humano; `BuildProof` Windows-only S-44); AC-01 da facet = unverified no Linux; `sdd-test.sh` segue quebrado (S-11/S-12)
- S-43 (Accept do follow no corpo do 202 não consumido — CONFIRMADO, risco p/ interop), S-40 (visibilidade em `/api/media`), S-45 (métricas sem exportador), S-42 (kit import sem cópia) — pointer: docs/sdd/ideas/BACKLOG.md
- `contracts.md`: backup/restore/peers de admin ainda sem handler (P-07 dona); `rel=profile` continua P-08; emendas vivas: data-model §2.7 (P-05) e §5 quota (P-04) — originais intactos

## Next planned step (snapshot)
- Humano abrirá a P-07 (backup cifrado + rotação RK/RC + entrega final TASK-0049) em nova sessão — mirror of: docs/sdd/plan/plan.md — snapshot, not an instruction
