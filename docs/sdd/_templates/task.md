<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-[NNNN] — [TITLE]

```yaml
runId: [RUN_ID]
taskId: TASK-[NNNN]
status: TASK_READY
agent: odev
phase: [P-01]
sequence: 1
createdAt: [YYYY-MM-DD HH:MM:SS]
updatedAt: [YYYY-MM-DD HH:MM:SS]
parent: [SPEC-NNNN]
authority: SPEC
parallel: false
touches: []
blocks: []
blockedBy: []
inputArtifacts: []
outputArtifacts: []
interviewRounds: 0/6
feasibilityRounds: 0/2
correctionAttempts: 0/2
qaAttempts: 0/2
contextHandoffs: 0/4
progressDelta: 0
humanGate:
  approvedBy: NENHUM
  approvedDate: NENHUM
  scope: NENHUM
  expiresAt: NENHUM
blocker: NENHUM
nextAction: [ONE_LINE_WHAT_RUNS_NEXT]
evidence: []
memoryBankFlags: []
```

## Intent

[1_TO_3_LINES: what this task delivers and why, from the spec.]

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: [OBSERVABLE_BEHAVIOUR]
- AC-02: [OBSERVABLE_BEHAVIOUR]

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- [PATH_OR_MODULE]

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| [GAP-01] | no | [ASSUMPTION] |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | [QUESTION] | [SPEC-NNNN / REQ-NNNN / HUMAN] | [ANSWER_OR_PENDING] |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`

- [YYYY-MM-DD HH:MM:SS] | odev | task created from [SPEC-NNNN]

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| [YYYY-MM-DD HH:MM:SS] | [dev\|qa] | [COMMAND] | [0] | [PATH:L] |