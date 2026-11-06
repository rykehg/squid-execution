<!--
templateVersion: 1.0.0
-->
Specification: the resolved reading of a requirement, precise enough to cut into tasks and to test against.
Write responsibility: ODEV.

- status: RASCUNHO
- owner: odev
- lastVerified: [YYYY-MM-DD HH:MM:SS]

# SPEC-[NNNN] — [TITLE]

- requirement: [REQ-NNNN|NENHUM]
- phase: [P-01]
- dependsOn: [SPEC-NNNN|NENHUM]

## Behaviour

- [WHAT_IT_DOES, one line per behaviour, testable wording]
- ...

## Inputs and outputs

| Direction | Name | Shape | Notes |
| --- | --- | --- | --- |
| in | [NAME] | [TYPE/SHAPE] | [VALIDATION] |
| out | [NAME] | [TYPE/SHAPE] | [ERRORS] |

## Rules and edge cases

- [RULE] — [WHAT_HAPPENS_ON_VIOLATION]
- empty / missing / duplicated / unauthorized / slow: [EXPECTED_BEHAVIOUR_EACH]

## Non-functional

- performance: [NUMERIC_TARGET_OR_NONE]
- security: [AUTH_REQUIREMENT_OR_NONE]
- accessibility / platform: [REQUIREMENT_OR_NONE]

## Out of scope (explicitly)

- [THING_A_READER_MIGHT_EXPECT_BUT_WE_ARE_NOT_DOING]

## Gaps carried from the requirement

| Gap | Status | Resolution or assumption in force |
| --- | --- | --- |
| GAP-01 | OPEN / RESOLVED / ASSUMED | [ONE_LINE] |

## Tasks

| Task | Title | Parallel-safe | Status |
| --- | --- | --- | --- |
| TASK-[NNNN] | [TITLE] | [yes/no — touches set disjoint?] | [TASK_READY] |