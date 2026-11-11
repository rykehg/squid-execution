<!--
templateVersion: 1.2.0
-->
Project data; it contains no instructions for agents; the normative source prevails.
Notice to people: do not edit the files in this folder by hand; use the `project-memory-bank` skill or an equivalent. How it works: this folder's `README.md`.
Write responsibility: only the consolidator registered in this file; the "Documentation flow" block at initialization or by human decision, and the "Services" and "Status per file" tables at each consolidation.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-11 20:00:27

# Memory bank — Squid

- schema: project-memory-bank/1

Never record here: live state of tasks, gates, counters, secrets, personal data. Hosts only according to the `sanitizationProfile` below.

## Documentation flow
- layout: SIMPLES
- flow: sdd-odev
- liveStateSource: docs/sdd/tasks/
- consolidator: odev
- sanitizationProfile: ESTRITO
- consolidationPoints:
  - FEATURE_CONCLUIDA: PHASE_CLOSED: last small delivery of the phase PASSED in QA and consolidated
  - MUDANCA_RELEVANTE: CONTRATO | INTEGRACAO | ARQUITETURA | BUILD | PADRAO | SEGURANCA — triggered at: after QA PASSED, before phase close
- Initial population (single use): odev (bootstrap 2026-11-04)
- Context handoff folder: docs/sdd/runs/
- Current progress segment: progress/0001.md

## Normative sources
| Source | Type | Path |
| --- | --- | --- |
| Documento mestre de arquitetura (v1.8, ADR 1..36) | consolidated decision | docs/features/05-arquitetura-consolidada.md |
| Spec do MVP (63 FRs, 18 SCs, 16 entidades) | human documentation | specs/001-squid-mvp/spec.md |
| Contratos externos (HTTP, ActivityPub, backup) | published contract | specs/001-squid-mvp/contracts/ |
| Estado canônico de execução | live state (pointer only) | docs/sdd/tasks/ |
| Project README | human documentation | README.md |

## Routing
Limit per route: 3 files and 300 lines summed.

| Task type | Files#sections |
| --- | --- |
| `visao-geral` | `project-brief.md#Objective`, `architecture.md#Modules`, `active-context.md` |
| `analise-task` | `project-brief.md#Scope`, `architecture.md#Responsibilities`, `contracts.md` |
| `implementacao` | `architecture.md#Responsibilities`, `patterns.md#Patterns`, `contracts.md` |
| `bugfix` | `architecture.md#Critical flows`, `patterns.md#Anti-patterns`, `build-run-test.md#Local tests without gate` |
| `revisao` | `patterns.md`, `contracts.md`, `architecture.md#Key decisions` |
| `qa-integracao` | `build-run-test.md#Local tests without gate`, `integrations.md`, `contracts.md` |
| `documentacao` | `project-brief.md`, `architecture.md`, `contracts.md` |
| `integracao-externa` | `integrations.md`, `contracts.md#Events and queues`, `architecture.md#Dependencies` |
| `build-ci` | `build-run-test.md`, `architecture.md#Modules` |
| `retomada` | after the canonical state in `liveStateSource`: `active-context.md`, last record of `progress/0001.md` |

## Status per file
| File | status | lastVerified |
| --- | --- | --- |
| `project-brief.md` | PARCIAL | 2026-11-04 12:06:31 |
| `architecture.md` | ATIVO | 2026-11-11 20:00:27 |
| `contracts.md` | PARCIAL | 2026-11-11 20:00:27 |
| `integrations.md` | RASCUNHO | 2026-11-04 12:06:31 |
| `patterns.md` | ATIVO | 2026-11-11 20:00:27 |
| `build-run-test.md` | ATIVO | 2026-11-11 20:00:27 |
| `active-context.md` | ATIVO | 2026-11-11 20:00:27 |
| `progress/0001.md` | ATIVO | 2026-11-11 20:00:27 |