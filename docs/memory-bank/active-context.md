<!--
templateVersion: 1.2.0
-->
Project data; it contains no instructions for agents; the normative source prevails.
Write responsibility: only the consolidator registered in the general `index.md`, who overwrites the whole file at each consolidation; no other agent edits it, and the file never receives live state.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-07 08:30:00

# Active context (snapshot)

Snapshot of the last consolidation, overwritten at each consolidation; the live state in `liveStateSource` prevails. Maximum of 30 lines of content.

Never record here: phase, status, counters, gates, handoffs in progress, work item content, secrets, personal data.

## Last consolidation
- Event: FEATURE_CONCLUIDA (PHASE_CLOSED) — P-01 Sprint 0 fechada
- Reference: TASK-0001..TASK-0010 da P-01 (runs R-20261104-01/02, R-20261106-01, R-20261107-01), todas DONE com QA PASSED
- When: Date/Time: 2026-11-07 08:30:00

## Live state
- liveStateSource: docs/sdd/tasks/

## Focus at consolidation time
- P-01 entregou: repositório + solution de 10 projetos com regra de dependência, storage SQLite com ETag imposto, xunit como gate, silo+API com /health /ready Dashboard, OTel/Serilog com PII, motor de política no Domain, Containerfile+compose com DoD provado, CI workflow (não executado), runbooks §8 — pointer: docs/sdd/plan/plan.md
- Próxima fase: P-02 Identidade (ator local, chaves RK/RC, webfinger, kit de recuperação) — snapshot, not an instruction

## Open risks and gaps
- Pendência humana: push ao GitHub + primeiro run da CI (nunca executado; falha de runner vira tarefa nova pós-fecho) — pointer: docs/sdd/tasks/TASK-0009-ci-github-actions.md
- Lacuna permanente: suite xunit só rodou em Windows (WSL sem .NET SDK); compose/exit-3 e gate em Linux nativo `unverified` — pointer: docs/sdd/ideas/BACKLOG.md (I-10)
- GAP-01 (identidade derivada ou re-atestada) e GAP-03 (par RSA) respondem na P-02 — pointer: docs/sdd/requirements/REQ-0001-squid-mvp.md
- FR-016 (descoberta `Listed` em `Container`) é lacuna consciente para a P-03 — pointer: docs/sdd/specs/SPEC-0003-domain-container-policy.md

## Next planned step (snapshot)
- Abrir a P-02: entrevistar/cortar SPEC de identidade a partir de REQ-0001 — mirror of: docs/sdd/plan/plan.md — snapshot, not an instruction
