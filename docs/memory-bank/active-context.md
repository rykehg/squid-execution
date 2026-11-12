<!--
templateVersion: 1.2.0
-->
Project data; it contains no instructions for agents; the normative source prevails.
Write responsibility: only the consolidator registered in the general `index.md`, who overwrites the whole file at each consolidation; no other agent edits it, and the file never receives live state.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-12 12:27:17

# Active context (snapshot)

Snapshot of the last consolidation, overwritten at each consolidation; the live state in `liveStateSource` prevails. Maximum of 30 lines of content.

Never record here: phase, status, counters, gates, handoffs in progress, work item content, secrets, personal data.

## Last consolidation
- Event: FEATURE_CONCLUIDA (PHASE_CLOSED) — P-05 Sprint 4 (Mensageria) fechada
- Reference: TASK-0033..TASK-0040 (run R-20261111-01), todas DONE com QA PASSED na 1ª tentativa (QA-0033..QA-0040); snapshot em docs/sdd/plan/history/0006-plan.md
- When: 2026-11-12 12:27:17

## Live state
- liveStateSource: docs/sdd/tasks/

## Focus at consolidation time
- P-05 entregou: thread com ordem total (CAS na coluna `version`, migrations 0002..0007), envio/leitura com delta exato e 404 byte-idêntico, participantes com admin eleito e sucessão do criador, aviso não-E2EE com gate 428 dentro da transação, marcador `lastReadSeq` monotônico, resposta `replyToId` com 422, reação idempotente com whitelist por requisição, anexo reusando `/api/media` (cota não duplicada) e facet de fecho do DoD — gate da casa 432 → **480/480**
- Gate da fase: DoD do Sprint 4 provado em fluxo HTTP (dois conversam, terceiro 404 idêntico, 6 envios concorrentes com seqs únicas, resposta/reação idempotentes, facet ponta a ponta com anexo)
- Incidentes: NENHUM (0 correções, 0 bloqueios, 0 D-01 abertos; 3 erros do ODEV corrigidos no fluxo — touches `schema/**` em 0034/0036 e typo de data em 0040)

## Open risks and gaps
- Linux: suíte do produto continua `unverified` na máquina (WSL sem dotnet, sondado a vivo em cada QA; CI ubuntu é o executor); `sdd-test.sh` continua quebrado (`*.slnx` — S-11/S-12)
- GAP-36 (ao vivo de mensagens não decidido — custo SSE/webhook/push respondido, polling cobre o DoD); GAP-37 (UI do aviso é P-08); carregados: GAP-02, GAP-04/05/06, GAP-09 (P-06), GAP-13..18, GAP-21..28, GAP-29..40 (29..35 resolvidos por humano, 36..40 assumidos), S-11/S-12/S-19/S-22..S-38 — pointer: docs/sdd/ideas/BACKLOG.md
- `contracts.md`: backup/restore/peers ainda sem handler (P-07 dona); emendas vivas: data-model §2.7 (admin de thread) e contrato §6 (rotas novas) — originais intactos

## Next planned step (snapshot)
- Aguarda decisão humana para abrir a P-06 (Federação) — mirror of: docs/sdd/plan/plan.md — snapshot, not an instruction
