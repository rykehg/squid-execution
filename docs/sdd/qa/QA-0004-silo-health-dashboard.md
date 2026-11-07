<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: VERDE
- owner: qa
- lastVerified: 2026-11-06 12:20:02

# QA-0004 — TASK-0005 Silo Orleans + /health + /ready + Orleans Dashboard

- task: TASK-0005
- spec: SPEC-0002-silo-saude-dashboard-observabilidade
- runId: R-20261106-01
- qaAttempt: 1/2
- verdict: PASSED

## What is proven

Tested only against the `## Acceptance criteria` list AC-01..AC-06 of the task file. Everything below
was executed in this session on `C:\dev\squid-squad-net\squid-execution` (Windows, PowerShell 5.1,
SDK 10.0.403). DEV's claims were re-run, not trusted. No stagingRoot: everything was written and run
directly in the repo.

| AC | Scenario | Command | Exit | Result |
| --- | --- | --- | --- | --- |
| AC-01 | plain `dotnet run --project backend/src/Squid.Service` (launchSettings ports 59311/59312, both free beforehand) brings up silo + API in ONE listener process; `GET /health` 200 while the process lives; liveness does no database I/O (with the storage file renamed away, `/health` stays 200 and the file is NOT recreated) | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0005.ps1` | 0 | PASS — `PASS \| AC-01 dotnet run comes up and serves GET /health 200`, `sdd-test health` 200/200/200, `PASS \| AC-01 a single listener process serves API + silo + dashboard \| listener_pid=31580 name=Squid.Service`, `PASS \| AC-01 /health stays 200 and does NOT touch the database \| health=200 file_recreated=False` (log `20261106-121043-qa0005-host.log`) |
| AC-02 | negative case on the REAL process: `/ready` 200 → storage file moved away → 503 → restored → 200, `/health` untouched throughout; plus the xunit rename case; plus mutation A (below) proving the 503 assertion is real | same as AC-01, plus `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.ServiceHost"` | 0 and 0 (1 under mutation) | PASS — host run: `status=503` while away, `status=200` after restore, `file_recreated=False`; suite: `Aprovado 5 Com falha 0 Total 5` (log `20261106-113628-test-filter.log`); mutation A exit 1 with `Expected: OK / Actual: ServiceUnavailable` at `ServiceHostTests.cs:line 36` (log `20261106-113341-test-filter.log`) — the product really answers 503, only a wrong expectation fails |
| AC-03 | a grain of test persists state through the storage the silo registers as `sqlite`, relê, and a write with a stale ETag is rejected by the version rule — proven by execution, not by reading | same filter as AC-02 | 0 (1 under mutation) | PASS — `Grain_persists_state_and_a_stale_version_write_conflicts` green in `Aprovado 5 Total 5`; mutation B (expected exception type swapped to `InvalidOperationException`) exits 1 with `Actual: typeof(Orleans.Storage.InconsistentStateException) — ETag mismatch for 'probe' / 'probe/ac03': expected 'MQ==', stored 'Mg=='` thrown at `SqliteGrainStorage.cs:line 123` (log `20261106-113521-test-filter.log`) — the ETag rule fires in the executed scenario |
| AC-04 | dashboard answers on the local route with NO redirect (both `/dashboard` and `/dashboard/` return 200 directly) and its counters report the active silo, all on the same single process; no authentication anywhere in the path | `qa-check-task-0005.ps1` (HttpClientHandler with `AllowAutoRedirect=false`) + `sdd-test.ps1 health` (independent: `Invoke-WebRequest -MaximumRedirection 0`) + the xunit dashboard case | 0 | PASS — `bare=200 slash=200`, `PASS \| AC-04 DashboardCounters reports the silo Active \| status=Active silo=127.0.0.1:11111`, `sdd-test health` prints `/dashboard 200` with zero redirects allowed, suite case `Dashboard_answers_locally_and_reports_the_active_silo` green |
| AC-05 | while the first instance holds the ports, a SECOND plain `dotnet run --project backend/src/Squid.Service` fails fast with a clear error and exit ≠ 0, never hanging | same as AC-01 (host B section, bounded: 120 s stopwatch + 130 s watchdog) | 0 (the script) | PASS — `exit=-532462766 seconds=15 hung=False clear_error=True`, message `System.IO.IOException: Failed to bind to address https://127.0.0.1:59311: address already in use` with the stack ending at `Program.cs:line 7`. Exit -532462766 = 0xE0434352 (unhandled exception), i.e. ≠ 0 as the AC demands. See Finding F1: DEV's evidence says `1`; QA measured 0xE0434352 twice and could not reproduce `1` |
| AC-06 | full gate green and the new tests counted; no `Version=` in any `.csproj` (ADR-25); new versions only in `Directory.Packages.props` | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (re-run by QA) + static checks inside both scripts | 0 and 0 | PASS — gate `Aprovado 33 Com falha 0 Total 33` (log `20261106-112847-test-gate.log`); the pre-existing baseline is 28 (header of `20261106-095401-test-gate.log`, `exit: 0 … Total: 28`), so the 5 new ServiceHost tests ARE counted; ADR-25 `csproj=10 hits=0`; pins as `PackageVersion` only (log `20261106-121043-qa0005-host.log` and WSL twin `20261106-122002-qa0005-host-wsl.log`) |

### GAP-08 closed by probe — verified independently (do not trust, re-checked)

| Claim (DEV) | QA verification | Result |
| --- | --- | --- |
| `Microsoft.Orleans.Dashboard` 10.2.2 exists and matches the Orleans 10.2 line | nuget.org flatcontainer lists `10.2.2`; its nuspec (authors **Microsoft**, group `net10.0`) depends on `Microsoft.Orleans.Runtime 10.2.2` — https://api.nuget.org/v3-flatcontainer/microsoft.orleans.dashboard/10.2.2/microsoft.orleans.dashboard.nuspec | CONFIRMED |
| OrleansContrib 8.x alternative correctly rejected | package id is `OrleansDashboard`; latest 8.2.0 nuspec (authors **OrleansContrib**) is `net8.0`-only and depends on `Microsoft.Orleans.* 8.2.0` — the Orleans 8 line, wrong for this repo — https://api.nuget.org/v3-flatcontainer/orleansdashboard/8.2.0/orleansdashboard.nuspec | CONFIRMED |
| pinned package actually restores | `project.assets.json` has `Microsoft.Orleans.Dashboard/10.2.2` and no `OrleansContrib` entry; local caches `backend/.packages/microsoft.orleans.dashboard/10.2.2` and `.dotnet-home/nuget/microsoft.orleans.dashboard/10.2.2` both exist; same two greps green under WSL | CONFIRMED |

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `docs/sdd/qa/qa-check-task-0005.ps1` | PowerShell 5.1 (`powershell -NoProfile -ExecutionPolicy Bypass -File …`) | static AC-06/GAP-08 checks, then the real hosts: A = plain `dotnet run` (AC-01/02/04 incl. the rename negative), B = second `dotnet run` on the busy ports (AC-05); every wait bounded, full cleanup (ports, processes, temp files) |
| `docs/sdd/qa/qa-check-task-0005.sh` | bash under WSL (`wsl -e bash …`) | Linux twin of the static checks only (AC-06 + GAP-08); explicitly reports that host scenarios are unverified on Linux because this box has no .NET SDK |
| `backend/tests/Squid.Tests/ServiceHost/ServiceHostTests.cs` | xunit via `sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.ServiceHost` | AC-01..AC-05 in-process: health, ready rename 200/503/200, grain write + stale-ETag conflict, dashboard counters Active, busy-port fail-fast |

## How to reproduce

Prerequisites: Windows, .NET SDK 10.0.403, ports 59311/59312 free, no leftover `squid-svc-*` in `%TEMP%`.

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test
# expected: exit 0 - "Aprovado! - Com falha: 0, Aprovado: 33, Ignorado: 0, Total: 33"
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0005.ps1
# expected: exit 0 - "RESULT: PASS - all checks green" (starts and stops its own hosts)
wsl -e bash /mnt/c/dev/squid-squad-net/squid-execution/docs/sdd/qa/qa-check-task-0005.sh
# expected: exit 0 - "RESULT: PASS - all checks green" (static checks only)
```

To see the tests fail on purpose (the proof they are not decoration): break the expectation in
`ServiceHostTests.cs:36` (AC-02) or `:69` (AC-03), rerun the filter command above, expect exit 1 with
the assertion quoted in Findings F2/F3, then restore the file — SHA256 before and after this run was
`4B529CBDBCA96E5A7AF949A5D9D4BB4132CAF4ADC5806E1E93DFF765C0434C57` (byte-exact restore, mutation
markers = 0).

## Last run

| Date/Time | Command | Exit | Output (tail) |
| --- | --- | --- | --- |
| 2026-11-06 11:28:47 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` | 0 | `Aprovado! - Com falha: 0, Aprovado: 33, Ignorado: 0, Total: 33, Duracao: 36 s` (log `docs/sdd/runs/test-runs/20261106-112847-test-gate.log`) |
| 2026-11-06 11:33:41 | filter run with AC-02 expectation deliberately broken (mutation A) | 1 | `Ready_is_200_with_storage_and_503_when_the_file_is_renamed [FAIL] Assert.Equal() Failure: Values differ / Expected: OK / Actual: ServiceUnavailable` at `ServiceHostTests.cs:line 36` (log `20261106-113341-test-filter.log`) |
| 2026-11-06 11:35:21 | filter run with AC-03 expected exception type swapped (mutation B) | 1 | `Grain_persists_state_and_a_stale_version_write_conflicts [FAIL] Actual: typeof(Orleans.Storage.InconsistentStateException) - ETag mismatch for 'probe' / 'probe/ac03': expected 'MQ==', stored 'Mg=='` at `SqliteGrainStorage.cs:line 123` (log `20261106-113521-test-filter.log`) |
| 2026-11-06 11:36:28 | filter run after byte-exact restore | 0 | `Aprovado! - Com falha: 0, Aprovado: 5, Ignorado: 0, Total: 5` (log `20261106-113628-test-filter.log`) |
| 2026-11-06 12:10:43 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0005.ps1` | 0 | `RESULT: PASS - all checks green`, incl. `AC-05 … exit=-532462766 seconds=15 hung=False clear_error=True` and `cleanup: port released … count=0` (log `20261106-121043-qa0005-host.log`) |
| 2026-11-06 12:20:02 | `wsl -e bash …/qa-check-task-0005.sh` | 0 | `RESULT: PASS - all checks green` + `note: dotnet absent on this platform - host scenarios AC-01..AC-05 are NOT executed here` (log `20261106-122002-qa0005-host-wsl.log`) |

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| F1 | MAJOR (evidence accuracy, not product) | `docs/sdd/tasks/TASK-0005-silo-health-dashboard.md:134` | DEV's Evidence row records the busy-port run as `exit 1`. QA reproduced AC-05 twice and measured `exit -532462766` (0xE0434352, the unhandled-exception code) both times; `1` could not be reproduced. AC-05 only demands exit ≠ 0 and holds, but the recorded value is wrong — ODEV should adjudicate/correct the row (a correction is a new row, never an edit) | `20261106-120142-qa0005-host.log`, `20261106-121043-qa0005-host.log` |
| F2 | MINOR (environment) | `docs/sdd/qa/qa-check-task-0005.ps1` capture note (AC-05 block) | In this sandbox `Start-Process -RedirectStandardOutput/-RedirectStandardError` returns an **empty** `Process.ExitCode` (isolated probe: plain process → `7`, redirected → empty), which silently voided the first AC-05 check. Fixed by capturing host B through `cmd /c "… > file 2>&1"` + `$LASTEXITCODE`. Environment quirk, never a product defect | probe 2026-11-06; script header comment |
| F3 | MINOR (QA tooling, fixed in session) | `docs/sdd/qa/qa-check-task-0005.ps1` HTTP helpers (first log `20261106-114547-qa0005-host.log` line 1) | The first host run assigned `AllowAutoRedirect` on `HttpClient` (the property lives on `HttpClientHandler`), so the "no redirect" client silently followed redirects and that specific check could not fail. Fixed with a real `HttpClientHandler{AllowAutoRedirect=false}`; the log that backs AC-04 is `20261106-121043-qa0005-host.log`, independently confirmed by `sdd-test.ps1 health` which uses `Invoke-WebRequest -MaximumRedirection 0` | `20261106-114547-qa0005-host.log` (property error), `20261106-121043-qa0005-host.log` (green) |

## Not covered, and why

- **Linux / WSL / container host scenarios (AC-01..AC-05 on Linux)** — this WSL box has no .NET SDK
  (documented in `qa-check-task-0003.sh`; re-confirmed here: the twin prints `dotnet absent`), docker
  is absent and the `Containerfile` is explicitly out of scope for SPEC-0002 ("entrega seguinte da
  P-01") — **unverified** on Linux, never assumed equal to the Windows run that passed. Only the
  static half of AC-06 ran on Linux (exit 0).
- **The 130 s watchdog that kills a hung host B** was not fire-tested (B never hung; it exited in 15 s)
  — the recorded run relies on the 120 s stopwatch + harness timeout as bounds.
- **`/health` in < 100 ms** (SPEC-0002 non-functional) — not an acceptance criterion of TASK-0005 —
  not measured.
- **OTel/Serilog behaviours** of SPEC-0002 — TASK-0006's scope, outside AC-01..AC-06 — not tested here.

Memory-bank flags raised for ODEV (proposals, nothing written to `docs/memory-bank/`):
`[MUDANCA_RELEVANTE: BUILD — pacotes Microsoft.Orleans.Dashboard 10.2.2 + Microsoft.Orleans.Server 10.2.2 no gate — source: backend/Directory.Packages.props:23]`,
`[PADRAO_NOVO: composition root testavel SquidServiceHost.Build, storage nomeado "sqlite" com AddGrainStorage + GetKeyedService nas tentativas de teste — source: backend/src/Squid.Service/SquidServiceHost.cs:21]`.
