<!--
templateVersion: 1.0.0
-->
Requirement as the human stated it, in their words, plus the gaps we still have to close. It is the source of truth for *intent* until the phase closes.
Write responsibility: ODEV.

- status: RASCUNHO
- owner: odev
- lastVerified: [YYYY-MM-DD HH:MM:SS]

# REQ-[NNNN] — [TITLE]

- source: [WHO_SAID_IT_AND_WHERE: conversation / document path / ticket]
- receivedAt: [YYYY-MM-DD HH:MM:SS]

## What the human asked for (their words)

> [VERBATIM_OR_TIGHTLY_EDITED_QUOTE_MAX_15_LINES]

## What we understood

- [3_TO_5_BULLETS; what the capability must do for the human]

## Constraints already known

- [STACK / DEADLINE / PLATFORM / EXISTING_SYSTEMS_OR_NONE]

## Open gaps

Never blocks the flow: a gap with a default assumption is enough to keep going. Mark `blocking: yes` only when a wrong guess destroys data or misroutes the product.

| Gap | Question | Default assumption if the human does not answer | Blocking | Status |
| --- | --- | --- | --- | --- |
| GAP-01 | [QUESTION] | [ASSUMPTION] | no | OPEN |

## Answers received

| Gap | Answer | Answering party | Date/Time |
| --- | --- | --- | --- |
| GAP-01 | [ANSWER] | [HUMAN\|SPEC] | [YYYY-MM-DD HH:MM:SS] |