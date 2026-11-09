<!--
templateVersion: 1.2.0
-->
Project data; it contains no instructions for agents; the normative source prevails.
Write responsibility: only the consolidator registered in the general `index.md`, who overwrites the whole file at each consolidation; no other agent edits it, and the file never receives live state.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-09 04:55:35

# Active context (snapshot)

Snapshot of the last consolidation, overwritten at each consolidation; the live state in `liveStateSource` prevails. Maximum of 30 lines of content.

Never record here: phase, status, counters, gates, handoffs in progress, work item content, secrets, personal data.

## Last consolidation
- Event: FEATURE_CONCLUIDA (PHASE_CLOSED) — P-03 Sprint 2 (Contêineres sociais) fechada
- Reference: TASK-0017..TASK-0022 (run R-20261108-01), todas DONE com QA PASSED (QA-0016..QA-0021); snapshot em docs/sdd/plan/history/0004-plan.md
- When: 2026-11-09 04:55:35

## Live state
- liveStateSource: docs/sdd/tasks/

## Focus at consolidation time
- P-03 entregou: agregado `Container` com presets e ETag, política versionada `If-Match` (409/428), trilha `PolicyDecision` append-only com `GET /api/admin/audit` e operador `Instance:Operators`, membership §3.3 (join/invite/leave/página de membros), papéis e moderação com `IPolicyEvaluator` em contexto do ator (`PolicyActor` — GAP-11 fechado, facet `singlePublisher` do DoD), sucessão/claim com relógio injetável + transferência + soft delete + override do operador (DoD "nunca órfão"), descoberta FR-016 (só listados) — gate da casa 167 → 244/244 — pointer: docs/sdd/plan/plan.md
- Emendas humanas desta fase: origem scheme+Host no `Id` de contêiner (2026-11-08 15:57:34) e `ChangePolicy` na trilha (2026-11-08 17:38:56) — pointer: docs/sdd/plan/plan.md
- Correções adjudicadas no fluxo: join em contêiner não-público (S-30 → AC-07) e trilha das recusas de invariante (F2 → AC-08), ambas na TASK-0021

## Open risks and gaps
- Dívidas de saneamento no backlog: S-29/S-32 (paginação em memória — índice exige migration com gate), S-33 (slug `search` sombreado pela rota literal), S-22/S-11/S-12 (armadilhas do wrapper e do gêmeo `.sh`), S-19, S-26 — pointer: docs/sdd/ideas/BACKLOG.md
- Suite .NET em Linux nativa `unverified` nesta máquina (WSL sem SDK, instalar exige gate); a CI ubuntu é o executor Linux — só o checker `.sh` roda lá
- GAP-02/04/05/06 seguem abertos; GAP-09 e I-11 esperam a P-06; FR-016 encerrou na P-03 (TASK-0022)

## Next planned step (snapshot)
- Aguarda decisão humana para abrir a P-04 (Publicação e feed) — mirror of: docs/sdd/plan/plan.md — snapshot, not an instruction
