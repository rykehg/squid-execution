<!--
templateVersion: 1.0.0
-->
Human documentation; it contains no instructions for agents. Written once by ODEV at bootstrap; rewritten only on a human decision. How the flow runs: `~/.dsh/skills/odev/SKILL.md`.

# SDD — spec driven development

Everything the flow produces for **this repository** lives here. One folder per artifact type, flat inside each folder, 4-digit sequential ids.

```
docs/sdd/
  README.md                  this file
  plan/
    plan.md                  current plan: MVP, phases, small deliveries, authority switch
    history/NNNN-plan.md     frozen snapshot, written once at each phase close
  ideas/BACKLOG.md           parked ideas, proposed for future phases
  requirements/REQ-NNNN-<slug>.md
  specs/SPEC-NNNN-<slug>.md
  tasks/TASK-NNNN-<slug>.md  canonical state of a work item + append-only log
  qa/QA-NNNN-<slug>.md       tests written and how to reproduce them
  runs/<runId>-context-NN.md context handoffs
  _templates/                skeleton copied from the harness templates
```

Outside this folder: `docs/memory-bank/` — the living documentation for LLMs. It is a cache of slow-changing project knowledge, not a phase artifact, which is why it does not live under `sdd/`.

## Who writes what

| Path | Written by | Rule |
| --- | --- | --- |
| `plan/plan.md` | ODEV | current view only; history goes to `plan/history/` |
| `plan/history/NNNN-plan.md` | ODEV | frozen, never edited after creation |
| `requirements/`, `specs/` | ODEV | rewritten freely while the phase is open |
| `tasks/*.md` control block, acceptance criteria | ODEV | single writer of state |
| `tasks/*.md` `## Log` | DEV, QA | append only, one timestamped line per event |
| `qa/QA-NNNN-*.md` | QA | tests and reproduction instructions |
| `memory-bank/` | ODEV only (consolidator) | only at a consolidation point |
| `runs/` | any role | its own handoff file, written once |

## Reading order for an agent entering the flow

1. the task file (`tasks/TASK-NNNN-*.md`) — canonical state;
2. `specs/` and `requirements/` it points to;
3. `plan/plan.md` — phase and the current authority switch;
4. `docs/memory-bank/index.md` — routed reading only, at most 3 files and 300 lines.

Do not scan the whole tree. Ids are sequential: read the newest of what you need, not everything.

## Authority switch

While a delivery is open, `requirements/` and `specs/` are the source of truth. Once the delivery is green in QA and consolidated, **the code is the real source** and `docs/memory-bank/` is the living documentation: a divergence between memory bank and code means the memory bank is wrong.

## Naming

`REQ-0001`, `SPEC-0001`, `TASK-0001`, `QA-0001`, `GAP-01`, `D-01`, `AC-01`. Never reuse a number, never renumber. A cancelled item keeps its id and is marked `CANCELLED` in its own file.