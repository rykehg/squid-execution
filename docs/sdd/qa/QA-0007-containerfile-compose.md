<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: VERDE
- owner: qa
- lastVerified: 2026-11-06 22:52

# QA-0007 — TASK-0008 Containerfile multi-stage + infra/compose.yaml + DoD da fase

- task: TASK-0008
- spec: SPEC-0004-containerfile-compose
- runId: R-20261106-01
- qaAttempt: 1/2
- verdict: PASSED

## What is proven

Tested only against the `## Acceptance criteria` list AC-01..AC-06 of the task file. Everything
below was executed in this session on `C:\dev\squid-squad-net\squid-execution` (Windows,
PowerShell 5.1, podman 6.1.4 + podman-compose 1.4.1 via pip, podman machine WSL running).
DEV's claims were re-run, not trusted. No stagingRoot: QA wrote and ran directly in the repo.
Expected values were derived from the artifacts (the Containerfile lines, the three base images
on this machine, the Jaeger API response), never remembered. Every green was first made to fail:
one mutation (M1) plus the three exit-3 branches of the gate.

| AC | Scenario | Command | Exit | Result |
| --- | --- | --- | --- | --- |
| AC-01 | Final stage literally `runtime` (last `FROM .../runtime:10.0 AS runtime`, 3 FROMs, `dotnet publish` only before the last FROM), `USER 10001:10001` after it; built image has `Config.User=10001:10001`, EMPTY `dotnet --list-sdks`, both runtimes present, env free of `DOTNET_SDK_VERSION` and `ASPNET_VERSION` (base derived from the 3 base images probed on this machine: only sdk carries DOTNET_SDK_VERSION, only aspnet/sdk carry ASPNET_VERSION, runtime carries neither) | `powershell ... -File docs/sdd/qa/qa-check-task-0008.ps1` + `wsl -u root -e bash docs/sdd/qa/qa-check-task-0008.sh` + `sdd-test.ps1 compose` | 0, 0, 0 | PASS — 33 PASS on Windows, 31 PASS on Linux (same image probes executed inside the WSL store); gate prints `imagem final usuario 10001:10001, sem SDK - AC-01/AC-05` |
| AC-02 | `podman compose up -d` from `infra/` brings service AND collector; `GET /health` on the exposed port 200, `/ready` 200, `/dashboard` 200 | QA's own cycle (`podman compose up -d` + 3 GETs) + `sdd-test.ps1 compose` (2 runs) | 0 | PASS — `infra_squid_1` and `infra_collector_1` Up, `/health /ready /dashboard -> 200` on `http://127.0.0.1:8080` |
| AC-03 | Named volume: file created during use survives `compose down` + `compose up` with NO `-v`; QA's own marker, never DEV's | QA's own cycle (`echo QA0007-R-20261106-01 > /data/qa0007.marker`, `down`, `up`, `cat`, `stat`) + static scan for `down -v` invocations | 0 | PASS — marker read back `QA0007-R-20261106-01` after down/up, inode `34930 -> 34930`, volume `infra_squid-data` exists after the plain `down`; 0 `down -v` code lines in `sdd-test.ps1` (the file's prose about `down -v` excluded) |
| AC-04 | A request QA generated appears as a span queryable in the collector, attributed tightly | QA's own cycle: 3x `GET /health?qa=QA0007B-R-20261106-01` then Jaeger `/api/services` + `/api/traces?service=...&start&end` bounded to my window ±2s | 0 | PASS — 4 `GET /health` spans inside the window (traces `d1afcf5469b5ef4a2ab2d2e2b87196a2`, `8c4a4478323c1d0a259d591670759dff`, `bdbb075a3f5a728d139c7fd8211a6a36`, `1a5da58c79504c90e107e00578b0e347`), `user_agent.original = ...WindowsPowerShell/5.1...` = QA's client, service `missing-service-name` |
| AC-05 | Image runs without privileges: uid/gid 10001, container `privileged=false`, compose has no `privileged:` and no secret-shaped line | `qa-check-task-0008.ps1` (secrets regex, comment lines excluded, same shape as the gate's) + `podman run --entrypoint id` + `podman inspect` on the running compose container | 0 | PASS — `uid=10001(10001) gid=10001(10001)` on both platforms, `privileged=false`, `compose privileged: matches=0`, secret hits=0. **The literal "(rootless)" facet measures false on this host — see F1** |
| AC-06 | Full gate `sdd-test.ps1 test` exits 0 with the compose in place, and the compose gate classifies podman-missing / provider-broken as environment (exit 3) | `sdd-test.ps1 test` + 3 child-process runs of `sdd-test.ps1 compose` with a modified PATH **in the child only** | 0; 3, 3, 3 | PASS — `Aprovado: 97, Total: 97` (log `20261106-224120-test-gate.log`); podman hidden -> exit 3; podman-compose hidden (podman present, provider lookup exit 125) -> exit 3; fake `docker-compose.bat`/`podman-compose.bat` (exit 1) shadowing the real one -> exit 3. Real PATH never modified (parent still resolves `Python\bin` after the runs), fake-provider temp dir removed |

### AC-01 base-image derivation (probed, not remembered)

| Image on this machine | `DOTNET_SDK_VERSION` | `ASPNET_VERSION` | `Config.User` |
| --- | --- | --- | --- |
| `mcr.microsoft.com/dotnet/sdk:10.0` | yes | yes | empty |
| `mcr.microsoft.com/dotnet/aspnet:10.0` | no | yes | empty |
| `mcr.microsoft.com/dotnet/runtime:10.0` | no | no | empty |
| `squid-service:dev` (built) | **no** | **no** | **10001:10001** |

The built image's env matches the runtime base and only ours (`ASPNETCORE_URLS=http://+:8080`,
`HOME=/app`). `dotnet --list-sdks` empty + `--list-runtimes` showing both shared frameworks is
the executed half of AC-01; the last `FROM` line is the static half.

### Mutation — every green above was first made to fail

| # | Broken on purpose | Gate exit | What failed | Restore proof |
| --- | --- | --- | --- | --- |
| M1 | `infra/Containerfile` final stage base swapped `runtime:10.0 -> sdk:10.0` (real file; backup taken OUTSIDE the repo first) | 1 | `compose: a imagem final contem SDK - falha real (exit 1)` — the AC-01/AC-05 image check fires before `up`, so the check is not decoration | SHA256 `50746498FE98C59CDA9328C982CFC3C5FA850AAC0805749E3007EFBC0C59D1E9` captured BEFORE the mutation, restored byte-exact (identical after `Copy-Item` from the backup), rebuild exit 0, image back to `list-sdks` empty; backup then deleted |

After the restore: `infra/compose.yaml` still `C8BD2B8E9262DFD2A899A48BD684AEC814941684522EAA9435A30440F36440DE`
(pre-run value), a final full `sdd-test.ps1 compose` exits 0, containers `none`, ports
8080/16686 free, volume `infra_squid-data` kept — never `down -v` at any point.

### Exit-3 classification — how it was isolated (AC-06)

Each case runs `sdd-test.ps1 compose` in a **child** `powershell` that sets `$env:PATH` for itself
only; the parent session PATH was verified untouched afterwards (`Python\bin` still resolves):

1. `PATH=C:\Windows\System32;C:\Windows;...\WindowsPowerShell\v1.0` -> `podman=ABSENT` -> exit 3
   (`podman ausente - ambiente (exit 3), nao e defeito de produto`).
2. Full PATH minus `...\Python\bin` (the dir holding the `podman-compose.bat` shim) ->
   `podman=present podman-compose=ABSENT` -> `podman compose version` exit 125 -> gate exit 3.
3. TEMP dir first in PATH with failing `docker-compose.bat` + `podman-compose.bat` (both
   `exit /b 1`) -> provider resolves to the fake -> `podman compose version` exit 1 -> gate exit 3;
   temp dir removed in the same run.

### touchedOutsidePlan — checked, nothing new

`git status --porcelain` + per-file mtimes: in TASK-0008's DEV window (20:59–22:08) only
`infra/Containerfile` (21:16), `infra/compose.yaml` (21:55), `docs/sdd/_tools/sdd-test.ps1`
(21:48) and `.dockerignore` (21:16, already accepted by ODEV) changed. Every other modified or
untracked file has mtime ≤ 18:46 and belongs to earlier tasks/ODEV. **No new touchedOutsidePlan
finding.**

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `docs/sdd/qa/qa-check-task-0008.ps1` | PowerShell 5.1 (`powershell -NoProfile -ExecutionPolicy Bypass -File …`) | QA, new: AC-01 static (last FROM/USER/publish), AC-02/03/04/05 compose static, AC-06 gate guards + gate logs (QA's newest + DEV's two claims, all 97/97 exit 0), image probes (user, list-sdks, runtimes, base-env derivation, uid/gid), no-`down -v` scan |
| `docs/sdd/qa/qa-check-task-0008.sh` | bash under WSL (`wsl -u root -e bash …`) | Linux twin: same static checks + the SAME image probes executed on the Linux side against the shared rootful store. `-u root` is required: the distro's default user `user` has its own EMPTY rootless store (measured). Marks compose orchestration + exit-3 as UNVERIFIED on Linux — not green |
| `docs/sdd/_tools/sdd-test.ps1 compose` | PowerShell 5.1 | DEV's DoD gate, executed by QA twice green (start and after M1 restore) and once red under M1 |
| `docs/sdd/_tools/sdd-test.ps1 test` | PowerShell 5.1 + dotnet | The full suite gate, executed by QA: 97/97 exit 0 |

## How to reproduce

Prerequisites: Windows with podman 6.1.4 + the D-01-gated podman-compose 1.4.1 on PATH, stack down.

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 compose
# expected: exit 0 - "compose: exit 0 - DoD da fase verificado (AC-01 a AC-05)"
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test
# expected: exit 0 - "Aprovado! - Com falha: 0, Aprovado: 97, Ignorado: 0, Total: 97"
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0008.ps1
# expected: exit 0 - "RESULT: PASS - all checks green" (33 checks)
wsl -u root -e bash /mnt/c/dev/squid-squad-net/squid-execution/docs/sdd/qa/qa-check-task-0008.sh
# expected: exit 0 - "RESULT: PASS - all executed checks green" (31 PASS + 2 UNVERIFIED on Linux)
```

To see it fail on purpose (the executed proof, not a recipe to keep): swap the last `FROM` of
`infra/Containerfile` to `mcr.microsoft.com/dotnet/sdk:10.0 AS runtime`, run the compose gate —
expect exit 1 with `a imagem final contem SDK`; restore the file (SHA256 before:
`50746498…59D1E9`) and rebuild. To see the exit-3 branch: run the compose gate in a child
powershell whose `$env:PATH` lacks the podman dir — expect exit 3, never 1.

## Last run

Times marked `~` are session-order approximations: compose runs write no log of their own. Exact
anchors are the test-gate log filenames, the Jaeger span windows and the final `Get-Date`.

| Time | Command | Exit | Output (tail) |
| --- | --- | --- | --- |
| ~22:16 (before 22:19) | `powershell … -File docs/sdd/_tools/sdd-test.ps1 compose` (QA run 1) | 0 | `imagem final usuario 10001:10001, sem SDK` · `/health /ready /dashboard 200` · `span consultavel no coletor` · `inode 34930` both sides · `compose: exit 0 - DoD da fase verificado` |
| ~22:29 | `powershell … -File docs/sdd/qa/qa-check-task-0008.ps1` (first run) | 1 | 2 false positives of QA's OWN checks (the `down -v` prose line, GNU `id` refusing `-u -g`) — fixed in the script, no product issue |
| ~22:30 | `powershell … -File docs/sdd/qa/qa-check-task-0008.ps1` (corrected) | 0 | `RESULT: PASS - all checks green` (33 checks) |
| ~22:33 | `wsl -e bash docs/sdd/qa/qa-check-task-0008.sh` (first run) | 1 | awk counted `dotnet publish` from the FIRST FROM; default WSL user's rootless store has no images (UNVERIFIED) — fixed (`awk` resets on FROM; run as root) |
| ~22:35 | `wsl -u root -e bash docs/sdd/qa/qa-check-task-0008.sh` | 0 | `RESULT: PASS - all executed checks green` (31 PASS, 2 UNVERIFIED noted) |
| 22:37:05 span window | own cycle: `compose up -d` + GETs + marker `QA0007-R-20261106-01` + `down` + `up` + `cat`/`stat` | 0 | `GET /health /ready /dashboard -> 200` · `privileged=false` · `marker-after=QA0007-R-20261106-01` · `AC03-persist=True (inode 34930 -> 34930)` · `containers-left=none` · `RESULT=True` |
| 22:39:13 span window | own AC-04 probe: 3x `GET /health?qa=QA0007B-R-20261106-01` + Jaeger query bounded ±2s | 0 | 4 `GET /health` spans in-window with PowerShell user_agent; tags show `url.query = ?qa=Redacted`; `AC04-tight=True` |
| 22:41:20 | `powershell … -File docs/sdd/_tools/sdd-test.ps1 test` | 0 | `Aprovado! – Com falha: 0, Aprovado: 97, Ignorado: 0, Total: 97, Duração: 40 s` (log `docs/sdd/runs/test-runs/20261106-224120-test-gate.log`) |
| ~22:45 | child PATH without podman -> `sdd-test.ps1 compose` | 3 | `podman ausente - ambiente (exit 3)` |
| ~22:46 | child PATH without `Python\bin` -> `sdd-test.ps1 compose` | 3 | `podman compose indisponivel (exit 125)` + `provider de compose ausente - ambiente (exit 3)` |
| ~22:47 | child PATH with fake providers -> `sdd-test.ps1 compose` | 3 | `provider-resolved-to=…\qa0008-broken-provider\podman-compose.bat` + `provider de compose ausente - ambiente (exit 3)`; temp removed |
| ~22:49 | M1: mutated Containerfile -> `sdd-test.ps1 compose` -> restore | 1 | `compose: a imagem final contem SDK - falha real (exit 1)` · `byte-exact=True` · `rebuild-exit=0` · `sdks-empty=True` |
| ~22:51 | `powershell … -File docs/sdd/_tools/sdd-test.ps1 compose` (final, restored file) | 0 | same green tail as run 1, plus `qa0007.marker` and `task0008.marker` both present in `/data` before the down |
| 22:52:41 | final state: `podman ps -a` + `netstat` + `podman volume ls` + `Get-FileHash` | 0 | `containers=none` · `ports 8080=0 16686=0` · `volume=infra_squid-data` · hashes identical to pre-run |

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| F1 | INFO (environment vs a literal AC reading — ODEV may adjudicate) | host podman machine, outside `touches` | AC-05's parenthetical `(rootless, não-root)`: `não-root` and `sem privilégios` are proven (uid 10001, `privileged=false`), but `podman info` reports `Host.Security.Rootless=false` and `podman machine inspect` reports `Rootful=true` — a STRICT "podman runs rootless" reading measures false on this machine. SPEC-0004's own gloss is `rootless (podman não exige daemon nem sudo)`, which holds (every command runs as the normal user, no sudo, no host daemon). The machine config predates the task and nothing in `touches` can change it | `podman info --format '{{json .Host.Security}}'` -> `"rootless":false`; `podman machine inspect podman-machine-default` -> `rootful=true` |
| F2 | INFO (explains a gate fallback; no AC broken) | OTLP export pipeline (SPEC-0002) — `grep -ri Redacted backend/src` = 0 hits, so this is upstream OTel behaviour, not repo code | Exported spans carry `url.query = ?qa=Redacted`: query VALUES are redacted before Jaeger, so a unique marker in the query string can NEVER appear in the trace JSON. The gate's `smoke=TASK0008` string match can only succeed via its `GET /health` fallback (it does — `span consultavel no coletor - missing-service-name (operacao GET /health)`). QA's AC-04 proof therefore used a ±2s time window + `user_agent` attribution instead | Jaeger tags dump: `url.query = ?qa=Redacted`, `url.path = /health`, `user_agent.original = …WindowsPowerShell/5.1…` |
| F3 | INFO (QA bookkeeping, transparency) | `docs/sdd/qa/qa-check-task-0008.ps1` / `.sh` first runs | QA's own checks failed first: (a) the `down -v` scan matched the gate's success MESSAGE text (`apos down/up sem -v`), (b) GNU `id` refuses `-u -g` together, (c) the sh awk counted from the first FROM, (d) the WSL default user has an empty rootless store. All four are test-harness bugs, corrected and re-run green; no product code touched | first runs exit 1 (both scripts) -> corrected runs exit 0 / exit 0 |

No BLOCKER, no MAJOR. DEV's claims were re-executed: compose gate 0 (twice + final), test gate
97/97 exit 0 (DEV's two logs `20261106-210143` / `20261106-215042` verified by the script as
exit 0 + 97, plus QA's own `20261106-224120`), both exit-3 branches reproduced, image/marker/span
proofs redone with QA's own markers. Verdict **PASSED**.

## Not covered, and why

- **Compose orchestration and exit-3 classification on native Linux** — the WSL machine has no
  `podman-compose` provider (D-01's pip install lives on the Windows host PATH) and the gate is
  PowerShell. The Linux twin runs the static checks and the image probes only; the dynamic AC-02/
  03/04/06 flows are **unverified** there. Risk if they break: the Containerfile/compose files are
  platform-neutral YAML/Dockerfile and the image itself executed on Linux (inside the containers),
  but a Linux-host `podman compose up` was never run.
- **`dotnet test` on Linux** — standing gap from earlier tasks (no .NET SDK in the WSL distro;
  installing one needs a gate). The Windows gate is green; the suite on Linux stays unverified.
- **Strict rootless runtime** — see F1: if ODEV/human meant "podman itself runs rootless", this
  host measures `rootful=true` and only a machine re-init (environment change, outside this
  task's touches, likely its own gate) could satisfy it.

Memory-bank flags raised for ODEV (proposals, nothing written to `docs/memory-bank/`):
`[MUDANCA_RELEVANTE: BUILD — sdd-test.ps1 ganhou o modo compose (ValidateSet probe/build/test/health/compose) que classifica podman ausente e provider quebrado/ausente como ambiente exit 3; provider desta maquina e podman-compose 1.4.1 via pip — source: docs/sdd/_tools/sdd-test.ps1:256]`,
`[MUDANCA_RELEVANTE: SEGURANCA — url.query exportado ao Jaeger redigido (?qa=Redacted,0 hits de "Redacted" no backend: comportamento upstream do OTel); marcador em query string nunca casa no trace, span localizar por janela de tempo + user_agent — source: docs/sdd/qa/QA-0007-containerfile-compose.md]`,
`[MUDANCA_RELEVANTE: BUILD — podman machine default e rootful (podman info Rootless=false); rootless estrito nao demonstravel nesta maquina, glosso da SPEC-0004 "sem daemon nem sudo" e o que vale — source: docs/sdd/qa/QA-0007-containerfile-compose.md]`.
