<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: RASCUNHO
- owner: qa
- lastVerified: [YYYY-MM-DD HH:MM:SS]

# QA-[NNNN] — [TASK_TITLE]

- task: [TASK-NNNN]
- spec: [SPEC-NNNN]
- runId: [RUN_ID]
- verdict: PASSED | FAILED | PARTIAL

## What is proven

| AC | Scenario | Script | Result |
| --- | --- | --- | --- |
| AC-01 | [WHAT_IS_EXERCISED] | [PATH] | PASS / FAIL / NOT-COVERED |

## Scripts

Real files, committed with the tests. Prefer the project's existing runner; when there is none, one small script is enough.

| Script | Language / runner | What it does |
| --- | --- | --- |
| [PATH] | [node\|python\|pwsh\|sh\|existing runner] | [ONE_LINE] |

## How to reproduce

Prerequisites: [WHAT_MUST_BE_INSTALLED_OR_RUNNING]

```
[COMMAND_1]      # expected: [OUTPUT_OR_EXIT_CODE]
[COMMAND_2]      # expected: [OUTPUT_OR_EXIT_CODE]
```

## Last run

| Date/Time | Command | Exit | Output (max 10 lines) |
| --- | --- | --- | --- |
| [YYYY-MM-DD HH:MM:SS] | [COMMAND] | [0/1] | ```[TAIL_OF_OUTPUT]``` |

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| 1 | BLOCKER / MAJOR / MINOR | [PATH:L] | [ONE_LINE] | [COMMAND_OR_OUTPUT_REF] |

## Not covered, and why

Explicit gaps in coverage. "Not covered" written here is honest; a silent gap is a defect.

- [WHAT_IS_NOT_TESTED] — [REASON] — [RISK_IF_IT_BREAKS]