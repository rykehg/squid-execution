<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: VERDE
- owner: qa
- lastVerified: 2026-11-07 00:25

# QA-0008 — TASK-0009 Workflow GitHub Actions de build + testes (ubuntu) com prova local

- task: TASK-0009
- spec: SPEC-0005-ci-github-actions
- runId: R-20261106-01
- qaAttempt: 1/2
- verdict: PASSED

## What is proven

Tested only against the `## Acceptance criteria` list AC-01..AC-06 of the task file. Everything
below was executed in this session on `C:\dev\squid-squad-net\squid-execution` (Windows,
PowerShell 5.1, `py -V:Astral\CPython3.13.18` = Python 3.13.18 + PyYAML 6.0.3, Git, WSL
podman-machine-default = Fedora 44 with python3 3.14.8 + PyYAML 6.0.3). DEV's claims were
re-run, not trusted. Expected values were derived from the artifacts (the ci.yml content, the
gate script's `ValidateSet`, the local `dotnet --list-sdks`, git refs), never remembered.
Every green was first made to fail: five mutations (M1..M5) against copies of ci.yml outside
the repo, plus a deliberately broken first run of the standalone validator (F3).

| AC | Scenario | Command | Exit | Result |
| --- | --- | --- | --- | --- |
| AC-01 | ci.yml parses with PyYAML (tool, not visual); `on.push.branches == [develop, main]` exactly; `pull_request` present; `permissions == {contents: read}` exactly | `powershell … -File docs/sdd/qa/qa-check-task-0009.ps1` (python half) + standalone `py -V:Astral\CPython3.13.18 -c yaml.safe_load …` | 0; 0 | PASS — `PASS AC01-parse … PyYAML 6.0.3`, `push branches = ['develop', 'main']`, `pull_request trigger present = True`, `permissions = {'contents': 'read'}`; standalone validator printed `6.0.3 dict` |
| AC-02 | The workflow's `run:` steps are EXACTLY the two local-gate commands (same script `docs/sdd/_tools/sdd-test.ps1`, same args `build`/`test`); 0 `continue-on-error`; 0 secret-pattern hits; local gate is that same script | checker (python exact-list assert) + independent `Select-String -Pattern 'continue-on-error','secrets\.','GITHUB_TOKEN','ghp_…','password','token','credential',…` on ci.yml + `ValidateSet` grep of `sdd-test.ps1` | 0 | PASS — `run steps = ['pwsh -NoProfile -File docs/sdd/_tools/sdd-test.ps1 build', 'pwsh -NoProfile -File docs/sdd/_tools/sdd-test.ps1 test']`, `continue-on-error occurrences = 0`, `secret-pattern hits = 0` (both tools), `PASS AC02-localgate`. Shell binary differs by design: workflow `pwsh -NoProfile` (GAP-17, ubuntu runner) vs local `powershell 5.1` — identical script and identical args |
| AC-03 | `dotnet-version` is an exact `x.y.z` pin (regex fullmatch, no range/latest), major = 10; cache `key` contains `hashFiles('**/Directory.Packages.props')` and that file exists in the repo | checker (python) | 0 | PASS — `dotnet-version = '10.0.403'` (local `dotnet --list-sdks` also reports `10.0.403`), `cache key = "${{ runner.os }}-nuget-${{ hashFiles('**/Directory.Packages.props') }}"`, `Directory.Packages.props exists = True` |
| AC-04 | The same commands the workflow invokes exit 0 on this machine: build + full gate; and the YAML validation runs exit 0 | `powershell … -File docs/sdd/_tools/sdd-test.ps1 build` · `… test` · standalone PyYAML validator | 0; 0; 0 | PASS — `build: exit 0` (0 avisos 0 erros); `Aprovado! – Com falha: 0, Aprovado: 97, Ignorado: 0, Total: 97` (log `docs/sdd/runs/test-runs/20261107-001518-test-gate.log`, 53 s); validator `6.0.3 dict` |
| AC-05 | git status + per-file mtime over the task window (≥ 2026-11-06 23:18:15): no change outside `.github/**` and outside the flow artifacts; full gate ≥ 97/97 exit 0 | checker (Windows) + twin (WSL) + the gate run of AC-04 | 0; 0 | PASS — `files changed after 2026-11-06 23:18:15 outside touches = 0`; the only DEV artifact in the window is `.github/workflows/ci.yml` (23:38:27); `sdd-test.ps1` mtime 21:48:20 and `sdd-test.sh` 2026-10-31 = pre-existing TASK-0008 deliverables, untouched; gate 97/97 |
| AC-06 | No push: `HEAD == origin/develop`, `rev-list --left-right` = `0 0`, no `[ahead]`, 0 commits since the task was created; the pending first GitHub run is registered as explicit debt in the task log | checker (Windows + WSL) + `git rev-list --left-right --count HEAD...origin/develop` + task-file log read | 0 | PASS — `HEAD=9e867d9 origin/develop=9e867d9 left-right=0 0 ahead=False`, `commits since task creation = 0`, HEAD commit `2026-11-05 23:41:18 -0300` predates the task; task `## Log` line (dev, 23:47:50) registers "sem push e sem run no GitHub nesta tarefa — pendencia explicita". **No pushed commit found** |

GAP-17 (the `.ps1` vs `.sh` choice, part of AC-02 as written) re-proven by execution:
`bash docs/sdd/_tools/sdd-test.sh build` → **exit 3** twice in a row (its `find -name '*.sln'`
misses `backend/Squid.slnx`, then `dotnet build ""` fails without a compiler-error code and the
script classifies it as environment). It never green-builds this repo, so the workflow's choice
of the `.ps1` stands — the exact exit code differs from DEV's recorded `1` (see F2).

### Mutation — every green above was first made to fail

Copies only, under `%TEMP%\qa0009-mut\`, deleted at the end; the real ci.yml was never written.

| # | Broken on purpose (copy) | Checker exit | What failed | Restore proof |
| --- | --- | --- | --- | --- |
| M1 | tab before `name: CI` (invalid YAML) | 1 | `FAIL AC01-parse - YAML invalid: found character '\t' …` | copies deleted; real SHA256 identical |
| M2 | `dotnet-version: '10.0.403'` → `'latest'` | 1 | `FAIL AC03-pin - dotnet-version = 'latest' …` | same |
| M3 | build step → `dotnet build backend/Squid.slnx` (a parallel reimplementation) | 1 | `FAIL AC02-commands - run steps = ['dotnet build …', 'pwsh … test']` | same |
| M4 | `continue-on-error: true` added to the test step | 1 | `FAIL AC02-no-coe - continue-on-error occurrences = 1` | same |
| M5 | `contents: read` → `contents: write` | 1 | `FAIL AC01-perms - permissions = {'contents': 'write'}` | same |

Each mutation was detected by its own expected check id and nothing else failed
(`totalFAILs=1` each). SHA256 of the real file captured BEFORE and AFTER:
`ECF7043386C823239C339DB938FC6B20BD34E5994424B9EF33B82602437A0FE8` — identical
(`REAL-UNCHANGED=True`), and a baseline re-run right after the mutations exits 0 (13 checks,
0 FAIL), so no residue was left behind.

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `docs/sdd/qa/qa-check-task-0009.ps1` | PowerShell 5.1 (`powershell -NoProfile -ExecutionPolicy Bypass -File …`), embedding a python checker run with `py -V:Astral\CPython3.13.18` | QA, new: AC-01 (parse + triggers + permissions), AC-02 (exact run-step list, `continue-on-error` scan, 9 secret patterns, local-gate identity), AC-03 (exact pin + cache-key derivation + file exists), AC-05 (git status × mtime window with an explicit flow-artifact allowlist), AC-06 (ref sync + no new commits). `-CiPath` points the same checks at a copy — that is the mutation harness |
| `docs/sdd/qa/qa-check-task-0009.sh` | bash + python3 under WSL (`wsl -u root -e bash …`, Fedora 44) | Linux twin: the same checks, same ids, same exit contract; git runs with `git -c safe.directory=…` (per-invocation, no config persisted). Marks the AC-04 gate as **UNVERIFIED** on Linux (`pwsh=absent dotnet=absent`) — never green |
| `docs/sdd/_tools/sdd-test.ps1 build\|test` | PowerShell 5.1 + dotnet | The local gate (and the workflow's payload), executed by QA: build exit 0, test exit 0 with 97/97 |
| `docs/sdd/_tools/sdd-test.sh build` | bash (Git Bash) | GAP-17 counter-proof executed by QA: exit 3, twice — the `.sh` cannot build this repo |

## How to reproduce

Prerequisites: Windows with .NET SDK 10.0.403, Git, `py` launcher with PyYAML 6.0.3; WSL distro
with python3 + PyYAML for the twin.

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0009.ps1
# expected: exit 0 - "RESULT: PASS - all executed checks green" (13 checks, 0 FAIL)
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build
# expected: exit 0 - "build: exit 0"
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test
# expected: exit 0 - "Aprovado! … Com falha: 0, Aprovado: 97, … Total: 97"
wsl -u root -e bash /mnt/c/dev/squid-squad-net/squid-execution/docs/sdd/qa/qa-check-task-0009.sh
# expected: exit 0 - "RESULT: PASS - all executed checks green" + AC-04 line says UNVERIFIED
```

To see it fail on purpose (executed, not a recipe to keep): copy ci.yml outside the repo and run
the checker with `-CiPath <copy>` after breaking one thing — e.g. change
`dotnet-version: '10.0.403'` to `'latest'` → expect exit 1 with `FAIL AC03-pin`; five such
mutations are in the table above.

## Last run

Times marked `~` are session-order approximations; exact anchors are the gate log filename and
the tool outputs quoted below.

| Time | Command | Exit | Output (tail) |
| --- | --- | --- | --- |
| ~00:05 | `powershell … -File docs/sdd/_tools/sdd-test.ps1 build` (QA, background) | 0 | `backend\Squid.slnx` · `0 Aviso(s)` · `0 Erro(s)` · `build: exit 0` |
| ~00:15 | `powershell … -File docs/sdd/_tools/sdd-test.ps1 test` (QA, background) | 0 | `log: …\20261107-001518-test-gate.log` · `Aprovado! – Com falha: 0, Aprovado: 97, Ignorado: 0, Total: 97, Duração: 53 s` · `test: exit 0` |
| ~00:17 | `powershell … -File docs/sdd/qa/qa-check-task-0009.ps1` (baseline) | 0 | 13 checks: `PASS AC01-parse … PASS AC06-nopush` · `RESULT: PASS - all executed checks green` |
| ~00:18 | mutation block M1..M5 (`-CiPath %TEMP%\qa0009-mut\M<n>.yml`) | 1,1,1,1,1 | `MUT M1 … detected=True` ×5, each `totalFAILs=1`; `REAL-SHA256-BEFORE` == `REAL-SHA256-AFTER`; post-baseline `CHECKER-EXIT=0` |
| ~00:18 | `py -V:Astral\CPython3.13.18 -c 'import sys,yaml; …' …` (first attempt) | 1 | `SyntaxError: '(' was never closed` — PS 5.1 stripped the inner double quotes of the `-c` argument; rewritten without inner quotes (F3) |
| ~00:18 | same validator, corrected | 0 | `6.0.3 dict .github\workflows\ci.yml` |
| ~00:18 | `wsl -u root -e bash docs/sdd/qa/qa-check-task-0009.sh` | 0 | 13 checks `0 FAIL` · `INFO AC04 - UNVERIFIED on Linux … pwsh=absent dotnet=absent … Never assumed green.` · `RESULT: PASS` |
| ~00:19 | `Select-String -Path .github\workflows\ci.yml -Pattern 'continue-on-error','secrets\.',…` | 0 | `HITS=0`; the two `run:` lines printed verbatim |
| ~00:21 | `bash docs/sdd/_tools/sdd-test.sh build` (pipeline) / direct RUN-A / RUN-B | 3 / 3 / 3 | `sdd-test: no *.sln found within depth 3` (stderr) · `build: exit 1 with no compiler error` · `environment: the MSBuild node pipe is blocked` → exit 3 both direct runs (GAP-17 counter-proof; DEV recorded 1 — F2) |
| ~00:22 | nested `bash -c 'bash … ; echo rc=$?'` | rc=0 | measurement artifact of PS 5.1 native single-quote passing (the inner script never ran as intended); superseded by the two direct runs above — QA bookkeeping, not a tool false-green (F3) |
| ~00:24 | final `powershell … qa-check-task-0009.ps1` after this doc + task appends | 0 | see task `## Evidence` (AC-05 re-verified at end state) |
| ~00:25 | `powershell … -File docs/sdd/_tools/sdd-state.ps1 check TASK-0009` | 0 | structure intact after the QA appends |

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| F1 | INFO (classification, not a product defect — ODEV may adjudicate) | `docs/sdd/plan/plan.md` (23:19:58) and `docs/sdd/tasks/TASK-0009-ci-github-actions.md` (23:57:32) | AC-05's window (≥ 23:18:15) contains two writes outside `.github/**`. Both are ODEV's own canonical-state flow (plan `nextAction` update at dispatch; `append-log`/`append-evidence` at the AWAITING_QA transition), not DEV's change; `SPEC-0005` (23:18:15) was created by ODEV before dispatch. The allowlist in both checkers lists them explicitly instead of hiding them. Every other in-window write is `.github/**` or QA's own files; all remaining working-tree mods are ≤ 23:12:01 (pre-existing TASK-0007/0008 deliveries) | checker `INFO AC05-flow - … plan.md (23:19:58) … TASK-0009 (23:57:32)`; `AC05-delta … = 0` |
| F2 | INFO (evidence-accuracy discrepancy; no AC depends on the code) | `docs/sdd/tasks/TASK-0009-ci-github-actions.md:129` (DEV evidence row) vs QA re-execution | DEV recorded `bash docs/sdd/_tools/sdd-test.sh build` → exit **1**; QA measures exit **3** twice in a row. Cause: after `find` misses `*.slnx`, `dotnet build ""`'s output determines the branch — without a `error <CODE>` line the script takes the environment branch (3). Both codes mean "the `.sh` does not build this repo", so GAP-17's `.ps1` choice and AC-02 are unaffected; only the recorded number is off | `bash docs/sdd/_tools/sdd-test.sh build` → 3 (RUN-A, RUN-B); task evidence row says `1` |
| F3 | INFO (QA bookkeeping, transparency) | QA's own commands | (a) first standalone validator run failed (exit 1) because PS 5.1 stripped inner quotes of `-c`; (b) one nested `bash -c` printed `rc=0` due to PS 5.1 native single-quote passing. Both are harness-construction bugs of QA, corrected and re-run green; no product code touched | rows in Last run at ~00:18 and ~00:22 |

No BLOCKER, no MAJOR. DEV's claims re-executed: ci.yml parses (PyYAML 6.0.3, exit 0), triggers /
permissions / pin / cache-key / zero-secrets verified by QA's own checker, build exit 0, gate
97/97 exit 0, branch in sync with origin (no push). Five mutations prove the checks fail when the
behaviour breaks. Verdict **PASSED**.

## Not covered, and why

- **The workflow executing on GitHub (ubuntu)** — by design, AC-06: the first GitHub run is a
  separate human gate after the human's push. Risk if it breaks: the yaml is parsed and the
  commands are the local gate's, but runner-side failures (image, `pwsh` behaviour, `dotnet`
  restore with network) only surface there; SPEC-0005 already says such a red becomes a new
  task/correction, never a silent removal.
- **The literal line `pwsh -NoProfile -File …` executed anywhere** — `pwsh` is absent on this
  Windows machine and absent in the WSL distro (installing needs a gate). What is proven is
  script+args identity with the local gate (which runs green under powershell 5.1); the `pwsh`
  binary itself is **unverified** until the post-push run.
- **The gate (`sdd-test.ps1 test`) on Linux** — no `dotnet`/`pwsh` in the WSL distro; the Linux
  twin runs the static checks only and prints AC-04 as UNVERIFIED. Parity Windows↔Ubuntu is the
  post-push human gate per SPEC-0005 (runner matrix decision: ubuntu only).
- **actionlint / GitHub-workflow schema validation** — not installed (installing = gate); the
  executed validation is PyYAML parse + QA's structural asserts, which is what AC-01 asks for
  ("ferramenta de validação executada").
- **`git ls-remote` against github.com** — network target, gate-required, not run; AC-06 is
  proven from local refs (HEAD == origin/develop, 0/0, no `[ahead]`, 0 commits since task
  creation, HEAD commit predating the task).

Memory-bank flags raised for ODEV (proposals, nothing written to `docs/memory-bank/`):
`[MUDANCA_RELEVANTE: BUILD — workflow .github/workflows/ci.yml entregue (push develop/main + pull_request, permissions contents read, setup-dotnet 10.0.403 pinado, cache NuGet hashFiles Directory.Packages.props, passos pwsh sdd-test.ps1 build|test) e validado localmente, mas NUNCA executado no GitHub: primeiro run e gate humano pos-push — source: .github/workflows/ci.yml]`,
`[MUDANCA_RELEVANTE: BUILD — secao CI de docs/memory-bank/build-run-test.md ainda diz "nenhum pipeline criado ainda"; consolidar no phase close apos o run humano — source: docs/memory-bank/build-run-test.md:39]`.
