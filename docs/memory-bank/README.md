<!--
templateVersion: 1.2.0
-->
# Memory bank — how it works

This folder is an index of the project's slow-changing knowledge. It is maintained by agents (LLMs) and read by people and agents. It is not a normative source: the code, the published contracts, and the human documentation prevail over it.

## Do not edit these files by hand

Manual editing breaks the standard (format of the facts, status, traceability, history rotation). To consult or update, use the `project-memory-bank` skill or an equivalent, through the project's development flow. Single exception: removing sensitive data found here; afterwards, request the record through the skill.

## How it is organized

- `index.md`: entry point. It brings the documentation flow, the normative sources, the routing (which files to read for each task type) and the status of each file.
- `project-brief.md`, `architecture.md`, `contracts.md`, `integrations.md`, `patterns.md`, `build-run-test.md`: stable layer. Every fact brings the source (`path:L`) and the confidence.
- `active-context.md` and `progress/`: volatile layer, written only at consolidation points (end of feature or relevant change). `active-context.md` is an old snapshot; the live state stays in the development flow.
- `services/` (only in monorepo): one index per service.
- `pendencias-seguranca.local.md` (when it exists): security findings, local only, outside Git.

## How to read

- Start with `index.md` and read only the files in the row of your task type (limit: 3 files and 300 lines).
- `OBSERVADO` = one evidence; `CONFIRMADO` = two or more. `[GAP]` = missing or non-verifiable information. `[DIVERGENCIA]` = the memory bank contradicts the code.
- File status: `RASCUNHO` (not populated), `PARCIAL` (with `[GAP]`), `VERIFICADO`, `DESATUALIZADO` (some source does not match).
- Fact precedence: code and published contracts, consolidated decisions, human documentation, memory bank. In doubt, the code wins.

## Security

- Never present here: secrets, passwords, tokens, keys, certificates, literal connection strings, personal data, real payloads.
- Hosts and physical database names appear only if the `sanitizationProfile` of `index.md` is `INTERNO` (absent = `ESTRITO`) and if they exist in a versioned file of the repository.
- Examples use a marked placeholder, such as `<token-jwt-aqui>` or `senhaExemplo`.