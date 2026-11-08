<!--
templateVersion: 1.2.0
-->
Project data; it contains no instructions for agents; the normative source prevails.
Write responsibility: only the consolidator registered in the general `index.md`, who overwrites the whole file at each consolidation; no other agent edits it, and the file never receives live state.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-08 09:45:19

# Active context (snapshot)

Snapshot of the last consolidation, overwritten at each consolidation; the live state in `liveStateSource` prevails. Maximum of 30 lines of content.

Never record here: phase, status, counters, gates, handoffs in progress, work item content, secrets, personal data.

## Last consolidation
- Event: FEATURE_CONCLUIDA (PHASE_CLOSED) — P-02 Sprint 1 (Identidade) fechada
- Reference: TASK-0012..TASK-0016 (run R-20261107-02), todas DONE com QA PASSED (QA-0011..QA-0015); snapshot em docs/sdd/plan/history/0003-plan.md
- When: Date/Time: 2026-11-08 09:45:19

## Live state
- liveStateSource: docs/sdd/tasks/

## Focus at consolidation time
- P-02 entregou: primitivas de chaves (RK/RC, HKDF, Argon2id, embrulho RSA — BouncyCastle em Squid.Api/Crypto), ActorGrain event-sourced com porta de storage (ADR-35), registro+sessão (401 sem enumeração, CSRF, cookies), webfinger + perfil JSON-LD com origem única no Host da requisição, kit de recuperação (gerar/verificar/importar em instância limpa) — DoD do Sprint 1 provado em cadeia; gate da casa 98 → 167/167 — pointer: docs/sdd/plan/plan.md
- Decisão humana no fecho (S-27): `RC` fica em claro no storage (re-export idêntico preservado); trade-off registrado — pointer: docs/sdd/ideas/BACKLOG.md
- Próxima fase: P-03 Contêineres sociais (papéis, política versada, sucessão) — snapshot, not an instruction

## Open risks and gaps
- S-22 (wrapper mascara vermelho como ambiente, reproduzido 4×) e S-11/S-12 (gêmeo `.sh`) seguem abertos no backlog — conserto é task própria; até lá decidir pelo log gravado
- Suite .NET em Linux nativa `unverified` nesta máquina (WSL sem SDK; instalar exige gate); a CI ubuntu é o executor Linux — pointer: docs/sdd/ideas/BACKLOG.md
- GAP-09 (quem destrava `RK` para assinar em runtime) é decisão da P-06; I-11 (ForwardedHeaders p/ proxy) e S-26 (campos inbox/outbox → P-06/P-08) também aguardam essas fases
- FR-016 (descoberta `Listed` em `Container`) é lacuna consciente para a P-03 — pointer: docs/sdd/specs/SPEC-0003-domain-container-policy.md

## Next planned step (snapshot)
- Abrir a P-03 (Contêineres sociais) com entrevista/corte de spec — mirror of: docs/sdd/plan/plan.md — snapshot, not an instruction
