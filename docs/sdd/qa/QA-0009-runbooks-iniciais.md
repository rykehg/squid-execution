<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: CONCLUIDO
- owner: qa
- lastVerified: 2026-11-07 (clock is stamped by ODEV on the log/evidence rows)

# QA-0009 — TASK-0010 — Cinco runbooks do §8 + índice com status e comandos provados

- task: TASK-0010
- spec: SPEC-0006-runbooks-iniciais.md
- runId: R-20261107-01
- verdict: PASSED
- qaAttempt: 1/2

## What is proven

| AC | Scenario | Script | Result |
| --- | --- | --- | --- |
| AC-01 | README index links the 5 runbooks, each row carries its explicit status (EXECUTÁVEL / PARCIAL / PENDENTE-fase), README points at the local gate `sdd-test.ps1 test` | `docs/sdd/qa/qa-check-task-0010.ps1` (+ `.sh` twin) | PASS |
| AC-02 | The 6 files exist (no extra `.md`); all 6 carry the four exact sections `## Sintoma`, `## Diagnóstico`, `## Procedimento`, `## Verificação`; the three PENDENTE runbooks have 0 numbered steps, 0 code fences, 0 EXECUTÁVEL marks, 0 command-like backticks, and carry `Ponteiros:` + `PENDENTE` | same (checks `AC02-*`) | PASS |
| AC-03 | Every command-like backticked segment in `disco-cheio.md` and `restaurar-backup.md` is classified: exact member of the executed list (13 strings) OR same-line `NÃO-executar` (5 destructive) OR named tool mention (4). With `-Execute`: all 13 EXECUTÁVEL strings ran **exit 0** on this machine, including both `df -h` and `curl -fsS …` forms verbatim inside WSL; destructive ones never executed | same, `-Execute` (61 checks) | PASS |
| AC-04 | `down -v` occurs exactly 2× (README:56, disco-cheio.md:36), both in destructive-warning context; 13 secret/token patterns = 0 hits; 5 personal-absolute-path patterns = 0 hits | same (checks `AC04-*`) | PASS |
| AC-05 | Gate `sdd-test.ps1 test` exit 0 (97/97); delta after the task window (2026-11-07 00:36:08): product files only under `docs/runbooks/**`, everything else is a flow artifact under `docs/sdd/**`; 61 pre-existing entries from TASK-0003..0009 outside the window are not this delta | gate (separate command) + same (check `AC05-delta`) | PASS |

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `docs/sdd/qa/qa-check-task-0010.ps1` | Windows PowerShell 5.1 | Static checks AC-01/02/04/05 + AC-03 classification; `-Execute` adds the real AC-03 cycle (compose up/down, restore cycle, curl forms, cleanup, port/volume assertions). `-RunbooksPath <copy>` is the break-it target. |
| `docs/sdd/qa/qa-check-task-0010.sh` | bash (WSL2) | Same checks, same ids, same exit contract (static half only). |

The project runs on Windows + WSL: both scripts exist and both were run. Platform rows that cannot run here are marked unverified in "Not covered", never assumed green.

## How to reproduce

Prerequisites: podman 6.1.4 + podman-compose 1.4.1 with image `squid-service:dev`, volume `infra_squid-data` present, WSL available, stack DOWN before starting (this run started with the stack down).

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0010.ps1   # expected: exit 0, SUMMARY total=24 fails=0
wsl -e bash docs/sdd/qa/qa-check-task-0010.sh                                             # expected: exit 0, SUMMARY total=24 fails=0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0010.ps1 -Execute   # expected: exit 0, SUMMARY total=61 fails=0; leaves stack down, volume kept, ports free
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test     # expected: exit 0, 97/97
```

Break-it (proof the checker can fail; mutate a COPY outside the repo, never the real runbooks):

```
copy docs\runbooks\*.md %TEMP%\qa0010-breakit\
# append to the copy of disco-cheio.md: 4. `podman nao-existe-xyz` - **EXECUTÁVEL** - breakit
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0010.ps1 -RunbooksPath %TEMP%\qa0010-breakit   # expected: exit 1, FAIL AC03-classify offender disco-cheio.md:50
```

## Last run

| # | Command | Exit | Output (tail) |
| --- | --- | --- | --- |
| 1 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0010.ps1` (real runbooks, final) | 0 | `SUMMARY total=24 fails=0` / `RESULT: PASS - all executed checks green` |
| 2 | `wsl -e bash docs/sdd/qa/qa-check-task-0010.sh` (real runbooks) | 0 | `SUMMARY total=24 fails=0` / `RESULT: PASS - all executed checks green` |
| 3 | same ps1 with `-RunbooksPath %TEMP%\qa0010-breakit` after mutation A (fake `podman nao-existe-xyz` marked EXECUTÁVEL) | 1 | `FAIL AC03-classify … offenders=1 [disco-cheio.md:50: podman nao-existe-xyz]` |
| 4 | same ps1 after mutation B (README status of disco-cheio flipped to `DISPONIVEL`) | 1 | `FAIL AC01-status-disco-cheio - index line exists but lacks token … DISPONIVEL` |
| 5 | same ps1 after mutation C (`## Verificação` removed from dlq-cheia copy) | 1 | `FAIL AC02-sections-dlq-cheia - 4 required sections present=False missing=[## Verifica..o]` |
| 6 | same ps1 on the pristine copy (control) | 0 | `SUMMARY total=24 fails=0` |
| 7 | `wsl -e bash docs/sdd/qa/qa-check-task-0010.sh /mnt/c/…/Temp/qa0010-breakit` (mutation A) | 1 | `FAIL AC03-classify - offenders: disco-cheio.md:50:podman nao-existe-xyz` |
| 8 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` | 0 | `Aprovado! – Com falha: 0, Aprovado: 97, Ignorado: 0, Total: 97` (log `docs/sdd/runs/test-runs/20261107-020119-test-gate.log`) |
| 9 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0010.ps1 -Execute` | 0 | `SUMMARY total=61 fails=0`; `AC03-exec-coverage … 13/13`; roundtrip `src=ACE84257… back=ACE84257…` |
| 10 | `Get-FileHash` on the 6 runbooks before/after the break-it | 0 | all 6 SHA256 identical (`ALL-UNCHANGED=True`) |

### AC-03 executed set (all exit 0, run 9)

`podman system df` · `df -h` (verbatim inside WSL) · `wsl df -h` · `podman volume ls` (×2) · `podman compose -f infra/compose.yaml up -d` (×2) · `curl -fsS http://localhost:8080/health` (cmd, WSL and post-restore) · `curl.exe -fsS http://localhost:8080/health` · `curl -fsS http://localhost:8080/ready` (cmd, WSL and post-restore) · `curl.exe -fsS http://localhost:8080/ready` · `podman compose -f infra/compose.yaml down` (×3, always plain) · `podman create --name squid-restore -v infra_squid-data:/data squid-service:dev` · `podman cp ./squid-grainstate.db squid-restore:/data/squid-grainstate.db` · `podman rm squid-restore`.

Destructive, classified `NÃO-executar`, never executed: `podman system prune`, `podman system prune --volumes`, `podman volume prune`, `podman image prune -a`, `podman compose -f infra/compose.yaml down -v`.

Tool-name mentions (not runnable lines, each with its runnable sibling in the same file): `podman compose`, `df`, `curl`, `curl -f`.

String fidelity vs the DEV claim: the documented strings and the executed strings are the same strings. `df -h` and `curl -fsS …` were executed **verbatim inside WSL** (`wsl -e bash -lc "df -h"` etc.); the Windows forms were executed as `wsl df -h`, `cmd /c curl -fsS …` and `curl.exe -fsS …` exactly as the runbooks instruct. The runbooks themselves document the per-platform divergence (disco-cheio.md:18-20, 44-46; restaurar-backup.md:19-21), which satisfies the SPEC rule "onde divergir, o runbook diz qual".

### Judge of the README hedge (explicit)

AC-02 asks each of the 6 files to have the four sections. `docs/runbooks/README.md` contains them as four real `##` sections (README.md:28 `## Sintoma`, :32 `## Diagnóstico`, :38 `## Procedimento`, :43 `## Verificação`) with genuine content — they define what each section means across the five runbooks (the "legenda"). The structural read that AC-02 declares as the verification method finds all four headings in all six files. **Judgment: literal AC-02 satisfied — PASS, not a finding.** Residual observation (non-blocking): an index reader gets the definition of the sections, not a runbook-shaped symptom→procedure flow; if ODEV wants the index to *behave* like a runbook, that is a new AC, not a defect of this one.

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| — | — | — | none (NENHUM) | runs 1-10, all as expected |

Two honest notes that are NOT findings:
- `restaurar-backup.md:17-18` cites exit 22 of `curl -f` for the `/ready` 503 case as documented behavior; this run did not induce 503 (would mean breaking the storage on purpose). The 200 path ran exit 0 on both attempts (pre- and post-restore).
- The checker itself failed before it passed: an initial version built `## Diagn`**í**`stico` (U+00ED) instead of `ó` (U+00F3) and correctly reported 6 FAILs; fixed to the codepoints measured in the files. A checker that cannot fail is decoration — this one was shown red twice (own bug + mutations A/B/C).

## Not covered, and why

- **podman lifecycle on a native Linux host** — no such environment here; the WSL podman 6.1.4 connects to a *different, empty* engine (0 volumes vs the machine's 20, no `infra_squid-data`), so running `compose up/down` there would prove nothing about the documented stack — `unverified`, never assumed green. Linux string forms (`df -h`, `curl -fsS …`) WERE executed verbatim in WSL against the running stack (run 9, entries 02/10/11).
- **local gate on Linux** (`sdd-test.ps1 test` needs Windows PowerShell 5.1 + dotnet) — `unverified`; on Windows it ran green (run 8).
- **`/ready` 503 / exit 22 path** — documented, not induced (see above) — risk: the runbook's failure-case wording goes unexercised; the curl `-f` behavior is upstream-documented.
- Runbooks of PENDENTE systems being *correct when those phases arrive* (P-02/P-06/P-07 content) — out of scope of this task (GAP-18) by design.

## Reproduce summary (two lines)

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0010.ps1 -Execute   # expected exit 0 (61/61)
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test              # expected exit 0 (97/97)
```
