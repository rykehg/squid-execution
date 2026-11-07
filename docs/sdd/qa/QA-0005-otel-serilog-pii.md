<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: VERDE
- owner: qa
- lastVerified: 2026-11-06 15:31

# QA-0005 — TASK-0006 OTel com exporter OTLP + Serilog com sanitização de PII

- task: TASK-0006
- spec: SPEC-0002-silo-saude-dashboard-observabilidade
- runId: R-20261106-01
- qaAttempt: 1/2
- verdict: PASSED

## What is proven

Tested only against the `## Acceptance criteria` list AC-01..AC-05 of the task file. Everything below
was executed in this session on `C:\dev\squid-squad-net\squid-execution` (Windows, PowerShell 5.1,
SDK 10.0.403). DEV's claims were re-run, not trusted. No stagingRoot: everything was written and run
directly in the repo. Every expectation below was proved breakable first — the mutation battery
(six mutations of product code, all restored) is what makes the green runs mean something.

| AC | Scenario | Command | Exit | Result |
| --- | --- | --- | --- | --- |
| AC-01 | local OTLP receiver (loopback Kestrel, HTTP/2 cleartext, counts export requests) + `OTEL_EXPORTER_OTLP_ENDPOINT` pointing at it: `GET /health` 200 → `ForceFlush` → the receiver count moves **before/after** and the exported batch carries `GET /health` | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.ServiceHost"` | 0 (1 under mutation M2) | PASS — `ObservabilityTests.A_health_request_reaches_the_otlp_receiver_as_an_exported_span` green in `Aprovado 14 Com falha 0 Total 14` (log `20261106-152537-test-filter.log`); mutation M2 (instrumentation detached) exits 1 with `no OTLP export arrived at the local receiver` at `ObservabilityTests.cs:38` (log `20261106-150452-test-filter.log`) — the span really travels to the receiver, only a broken pipeline fails |
| AC-02 | without `OTEL_EXPORTER_OTLP_ENDPOINT`: (a) `OtlpEndpoint.Resolve` returns `http://localhost:4317` for null/blank and passes an explicit URL through; (b) **end to end**: the HOST exports a `/health` span to a receiver bound on port **4317**, proving the wiring uses that value and not only the pure function | same filter | 0 (1 under mutation M4) | PASS — both cases green in `Aprovado 14 Total 14`; mutation M4 (host wiring bypasses `OtlpEndpoint.Resolve`, default forced to 4318) exits 1 with `no OTLP export arrived on http://localhost:4317 with OTEL_EXPORTER_OTLP_ENDPOINT unset` at `Qa0005OtlpDefaultEndpointTests.cs:49` while **all 12 DEV tests stayed green** (log `20261106-151403-test-filter.log`) — finding F1 explains why QA added that case |
| AC-03 | receiver down = a port that was open a moment ago and is now closed: `/health` 200 **before and after** the export attempt, the export error appears as `Level: Warning` naming **our** endpoint, no silent `catch {}` in the export path (static grep: the only `catch` in `WarningOtlpExporter` logs the exception), no exception reaches the client | same filter | 0 (1 under mutation M3) | PASS — `ObservabilityTests.An_unreachable_collector_keeps_the_host_healthy_and_warns_in_the_log` green; mutation M3 (both `LogWarning` calls removed = the silent catch the AC forbids) exits 1 with `Assert.Contains() Failure: Sub-string not found` at `ObservabilityTests.cs:81` (log `20261106-150938-test-filter.log`) — the warning is the only thing standing between a silent swallow and a green test |
| AC-04 | a log event containing email, phone and key reaches the console output structured (key-value JSON, `Level`, `Properties`, `RenderedMessage`) and masked — proven on **both** layers: property-name masking through the host (`[MASKED]` in `Properties.Email/Phone/Key`) and literal PII written straight into the message template (masked to `***@***`, JSON still parses, message still renders); unit cases cover the enricher, the formatter and JSON/number/timestamp integrity | same filter | 0 (1 under mutation M1, 1 under M1b, 1 under M5) | PASS — `ObservabilityTests.Structured_logs_reach_the_output_with_pii_masked`, `Qa0005HostLiteralPiiTests.Literal_pii_in_a_template_message_never_reaches_the_console_output` and the 3 `PiiSanitizerTests` green; mutation M1 (`Mask` no-op) exits 1 with 2 failures at `PiiSanitizerTests.cs:27` and `:63` (log `20261106-145535-test-filter.log`), M1b (`IsSensitive` always false) exits 1 with AC-04 failing at `ObservabilityTests.cs:126/129` (log `20261106-150019-test-filter.log`), M5 (formatter detached from the host console sink) exits 1 with `Assert.DoesNotContain() Failure: Sub-string found` at `Qa0005HostLiteralPiiTests.cs:41` while the DEV e2e stayed green (log `20261106-152923-test-filter.log`) — each layer breaks the suite where it should |
| AC-05 | full gate green with the new tests counted; no `Version=` in any `.csproj` (ADR-25); new versions only in `Directory.Packages.props` | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (re-run by QA twice) + `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0006.ps1` | 0 and 0 | PASS — QA's own gate re-run of DEV's claim: `Aprovado 40 Com falha 0 Total 40` (log `20261106-144558-test-gate.log`), baseline `33` in the header of `20261106-124547-test-gate.log`, so DEV's 7 new tests ARE counted; final gate after all restores `Aprovado 42 Com falha 0 Total 42` (log `20261106-153124-test-gate.log` = 33 + 7 DEV + 2 QA); ADR-25 `csproj=10 hits=0` |

### Mutation battery — every green above was first made to fail

| # | Broken on purpose (product code, restored after) | Suite exit | What failed | Restore proof |
| --- | --- | --- | --- | --- |
| M1 | `PiiMaskingFormatter.Mask` returns the line unchanged (formatter layer dead) | 1 | `PiiSanitizerTests.cs:27` and `:63` — `Assert.DoesNotContain() Failure` (log `20261106-145535-test-filter.log`) | SHA256 `CD5EB253E14BDEDDD429BC40D5E5198EE978748FF6F8FD07D058D6C61DA25C87` byte-exact |
| M1b | `PiiSanitizingEnricher.IsSensitive` always false (enricher blind) | 1 | AC-04 e2e at `ObservabilityTests.cs:126/129` + destructured unit at `PiiSanitizerTests.cs:48` — `Assert.Equal() Failure: Strings differ` (log `20261106-150019-test-filter.log`) | text identity 60/60 lines + 0 mutation markers (see F2: pre-mutation hash was not captured for this file) |
| M2 | `AddAspNetCoreInstrumentation()` removed (no request span is ever created) | 1 | 3 failures: AC-01 `ObservabilityTests.cs:38`, AC-03 `ObservabilityTests.cs:81`, QA AC-02 (log `20261106-150452-test-filter.log`) | SHA256 `59BF88E961EFC915B5F0FB7322B61A7CCCFB85560F09B4FD1DBD031794F6C6C1` byte-exact |
| M3 | both `logger.LogWarning` calls removed from `WarningOtlpExporter` (the silent catch) | 1 | AC-03 only — `Assert.Contains() Failure` at `ObservabilityTests.cs:81` (log `20261106-150938-test-filter.log`) | SHA256 `0F696E04FAA7B2E3D8DB56EF14569F8ECC504F8A30B2E71AAB6883D3B25CA728` byte-exact |
| M4 | host wiring ignores `OtlpEndpoint.Resolve`, default forced to `http://localhost:4318` | 1 | QA AC-02 only (`Qa0005OtlpDefaultEndpointTests.cs:49`); the 12 DEV tests stayed green (log `20261106-151403-test-filter.log`) | SHA256 `59BF88E9…` byte-exact (same file as M2) |
| M5 | `PiiMaskingFormatter` detached from the host's `WriteTo.Console` | 1 | QA literal-PII case only (`Qa0005HostLiteralPiiTests.cs:41`); DEV's AC-04 e2e stayed green (log `20261106-152923-test-filter.log`) | SHA256 `59BF88E9…` byte-exact; final scan `MARCADORES DE MUTACAO: NENHUM` |

### Package scope vs. the human gate of 2026-11-06 (Henrique) — verified, not trusted

| Package (gate says) | Pinned in `backend/Directory.Packages.props` | Referenced without `Version=` in | Result |
| --- | --- | --- | --- |
| OpenTelemetry.Extensions.Hosting 1.20.1 | line 30, `1.20.1` | `Squid.Service.csproj` | OK |
| exporter OTLP = OpenTelemetry.Exporter.OpenTelemetryProtocol 1.20.1 | line 31, `1.20.1` | `Squid.Service.csproj` | OK |
| instrumentação ASP.NET Core = OpenTelemetry.Instrumentation.AspNetCore 1.20.1 | line 32, `1.20.1` | `Squid.Service.csproj` | OK |
| Serilog 4.4.0 | line 33, `4.4.0` | `Squid.Service.csproj` | OK |
| Serilog.AspNetCore 10.0.0 | line 34, `10.0.0` | `Squid.Service.csproj` | OK |

All five direct `PackageReference`s of the observability stack carry no `Version=` (ADR-25: 0 hits in
the 10 csproj). Everything else in `project.assets.json` for Serilog/OTel (`Serilog.Sinks.Console
6.1.1`, `Serilog.Extensions.Hosting 10.0.0`, `OpenTelemetry 1.20.1` core, …) is transitive through
those five — no package outside the gate scope was installed. `Squid.Tests` references none of them
directly (it reaches them through the `Squid.Service` project reference).

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `backend/tests/Squid.Tests/ServiceHost/ObservabilityTests.cs` | xunit via `sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.ServiceHost` | DEV's AC-01..AC-04: receiver span count, endpoint default (pure), closed-port warning, host PII masking |
| `backend/tests/Squid.Tests/ServiceHost/PiiSanitizerTests.cs` | xunit (same runner) | DEV's 3 unit cases: literal PII in the line, destructured sensitive names, JSON/numbers/timestamps intact |
| `backend/tests/Squid.Tests/ServiceHost/Qa0005OtlpDefaultEndpointTests.cs` | xunit (same runner) — **QA, new** | AC-02 end to end: receiver bound to port 4317 with the env var unset, so the host must export there |
| `backend/tests/Squid.Tests/ServiceHost/Qa0005HostLiteralPiiTests.cs` | xunit (same runner) — **QA, new** | AC-04 second half: literal PII in the message template through the real host pipeline (guards the formatter wiring) |
| `docs/sdd/qa/qa-check-task-0006.ps1` | PowerShell 5.1 (`powershell -NoProfile -ExecutionPolicy Bypass -File …`) | static AC-05: ADR-25 scan, gate-version pins, newest gate log exit 0 with total above the 33 baseline |
| `docs/sdd/qa/qa-check-task-0006.sh` | bash under WSL (`wsl -e bash …`) | Linux twin of the same static checks; explicitly reports the suite as not executed on Linux (no .NET SDK) |

## How to reproduce

Prerequisites: Windows, .NET SDK 10.0.403, port 4317 free (SPEC-0002 keeps a real collector out of
dev), no leftover `squid-*` hosts in `%TEMP%`.

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test
# expected: exit 0 - "Aprovado! - Com falha: 0, Aprovado: 42, Ignorado: 0, Total: 42"
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.ServiceHost"
# expected: exit 0 - "Aprovado! - Com falha: 0, Aprovado: 14, Ignorado: 0, Total: 14"
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0006.ps1
# expected: exit 0 - "RESULT: PASS - all checks green"
wsl -e bash /mnt/c/dev/squid-squad-net/squid-execution/docs/sdd/qa/qa-check-task-0006.sh
# expected: exit 0 - "RESULT: PASS - all checks green"
```

To see them fail on purpose (the proof they are not decoration): remove one `logger.LogWarning` from
`WarningOtlpExporter.cs` (AC-03), or detach `PiiMaskingFormatter` in `SquidServiceHost.cs:43`
(AC-04 literal), or point the host default at `4318` (AC-02 wiring), rerun the filter command and
expect exit 1 with the assertion quoted in the battery above; restore afterwards — the SHA256 of
`SquidServiceHost.cs`/`PiiMaskingFormatter.cs`/`WarningOtlpExporter.cs` before and after this run was
`59BF88E9…`/`CD5EB253…`/`0F696E04…` (byte-exact, mutation markers 0).

## Last run

| Date/Time | Command | Exit | Output (tail) |
| --- | --- | --- | --- |
| 2026-11-06 14:45:58 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` | 0 | `Aprovado! - Com falha: 0, Aprovado: 40, Ignorado: 0, Total: 40, Duracao: 40 s` (log `docs/sdd/runs/test-runs/20261106-144558-test-gate.log`) — QA re-run of DEV's claim, 33+7 |
| 2026-11-06 14:53:42 | filter run, baseline with QA's first test | 0 | `Aprovado 14 … Total 14` was reached only after both QA tests existed; this run: `Aprovado 13 Com falha 0 Total 13` (log `20261106-145342-test-filter.log`) |
| 2026-11-06 14:55:35 | filter run under mutation M1 (`Mask` no-op) | 1 | `2 … PiiSanitizerTests [FAIL] Assert.DoesNotContain() Failure` at `PiiSanitizerTests.cs:27` and `:63` (log `20261106-145535-test-filter.log`) |
| 2026-11-06 15:00:19 | filter run under mutation M1b (enricher blind) | 1 | `2 … ObservabilityTests.Structured_logs_reach_the_output_with_pii_masked [FAIL] Assert.Equal() Failure: Strings differ` at `ObservabilityTests.cs:126` (log `20261106-150019-test-filter.log`) |
| 2026-11-06 15:04:52 | filter run under mutation M2 (no instrumentation) | 1 | `3 … no OTLP export arrived at the local receiver` at `ObservabilityTests.cs:38` + AC-03 + QA AC-02 (log `20261106-150452-test-filter.log`) |
| 2026-11-06 15:09:38 | filter run under mutation M3 (silent export failure) | 1 | `1 … Assert.Contains() Failure: Sub-string not found` at `ObservabilityTests.cs:81` (log `20261106-150938-test-filter.log`) |
| 2026-11-06 15:14:03 | filter run under mutation M4 (default wiring → 4318) | 1 | `1 … no OTLP export arrived on http://localhost:4317 with OTEL_EXPORTER_OTLP_ENDPOINT unset` at `Qa0005OtlpDefaultEndpointTests.cs:49`, 12 DEV green (log `20261106-151403-test-filter.log`) |
| 2026-11-06 15:25:37 | filter run, baseline with both QA tests | 0 | `Aprovado! - Com falha: 0, Aprovado: 14, Ignorado: 0, Total: 14` (log `20261106-152537-test-filter.log`) |
| 2026-11-06 15:29:23 | filter run under mutation M5 (formatter detached from host) | 1 | `1 … Assert.DoesNotContain() Failure: Sub-string found` at `Qa0005HostLiteralPiiTests.cs:41`, DEV's AC-04 e2e green (log `20261106-152923-test-filter.log`) |
| 2026-11-06 15:31:24 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (final, after all restores) | 0 | `Aprovado! - Com falha: 0, Aprovado: 42, Ignorado: 0, Total: 42, Duracao: 34 s` (log `20261106-153124-test-gate.log`) |
| 2026-11-06 15:32 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0006.ps1` | 0 | `RESULT: PASS - all checks green` (10 PASS lines: `csproj=10 hits=0`, 5 gate pins, `newest=42 baseline=33`) |
| 2026-11-06 15:33 | `wsl -e bash /mnt/c/dev/squid-squad-net/squid-execution/docs/sdd/qa/qa-check-task-0006.sh` | 0 | `RESULT: PASS - all checks green` (9 PASS lines, same static checks) |

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| F1 | MINOR (coverage, closed in this session) | `backend/tests/Squid.Tests/ServiceHost/ObservabilityTests.cs:50` (AC-02) and `:95` (AC-04) | DEV's AC-02 case asserts only the pure `OtlpEndpoint.Resolve`, and DEV's AC-04 e2e only exercises the enricher layer — so two host-wiring breaks went unguarded: with the default wired to `4318` all 12 DEV tests stayed green (M4), and with `PiiMaskingFormatter` detached from `WriteTo.Console` all DEV tests stayed green (M5). AC-02/AC-04 as written were satisfiable, but the host behaviour behind them was not proven by DEV's suite alone | `20261106-151403-test-filter.log` (12 green, 1 QA fail) and `20261106-152923-test-filter.log` (DEV e2e green, QA fail). Closed by `Qa0005OtlpDefaultEndpointTests.cs` and `Qa0005HostLiteralPiiTests.cs`, both green in the final gate |
| F2 | INFO (QA bookkeeping, no product impact) | `backend/src/Squid.Service/PiiSanitizingEnricher.cs` | The M1b restore is verified by full text identity (60/60 lines equal to the original read) plus a zero-marker scan, **not** by a pre-mutation SHA256 — QA took that file's hash only after the mutation had begun. The other three mutated files have byte-exact SHA256 anchors. Risk of an unnoticed byte difference in this one file is nil in practice (text identity + final gate 42/42), but the anchor is weaker and stated as such | restore scan: `RESTORES OK` / `MARCADORES DE MUTACAO: NENHUM` |
| F3 | INFO (attribution, no impact) | `docs/sdd/runs/test-runs/20261106-144929-test-gate.log` | A gate run inside this attempt that QA's surviving transcript did not start (`when: 2026-11-06 14:49:29`, exit 0, 40/40): it belongs to the **first QA dispatch of this attempt, which died before writing anything** — ODEV recorded exactly that at `TASK-0006:110` ("subagente morreu sem closing message, zero escritas no repositorio"). Result identical to QA's own gate (`20261106-144558`), no impact on any AC; noted so the run history is not misattributed to DEV or to another task | header of the log file itself + `docs/sdd/tasks/TASK-0006-otel-serilog.md:110` |

No BLOCKER, no MAJOR → verdict **PASSED**.

## Not covered, and why

- **Linux / WSL execution of the suite (AC-01..AC-05 on Linux)** — WSL has no .NET SDK
  (re-confirmed this run: `command -v dotnet` → `dotnet-absent`), so only the static twin ran there
  (exit 0) — **unverified** on Linux, never assumed equal to the Windows run that passed. Risk if it
  breaks: loopback/`localhost` dual-stack resolution, `Console.Out` redirection and Kestrel h2c
  behave differently on Linux — none of them proven there.
- **A real OTLP collector / remote log sink** — explicitly out of scope in SPEC-0002 ("collector
  OTLP real no dev"; GAP-10 keeps the console sink in dev). Proven against a local receiver and a
  closed port only — real-collector interop (TLS, auth, grpc status codes) is unproven.
- **OTel metrics/logs exporters, Prometheus, tracing of non-HTTP paths** — not in AC-01..AC-05 —
  not tested.
- **Export latency / batch-timing budgets** — no AC states one; the tests wait bounded (30 s) and
  flush explicitly, they do not measure latency.

Memory-bank flags raised for ODEV (proposals, nothing written to `docs/memory-bank/`):
`[DIVERGENCIA: control block de TASK-0006 registra memoryBankFlags [GAP: memory bank absent], mas docs/memory-bank/ existe e esta populado (index.md status PARCIAL, bootstrap 2026-11-04) — source: docs/sdd/tasks/TASK-0006-otel-serilog.md:40]`,
`[MUDANCA_RELEVANTE: BUILD — pacotes de observabilidade OpenTelemetry.* 1.20.1 + Serilog 4.4.0 + Serilog.AspNetCore 10.0.0 no gate — source: backend/Directory.Packages.props:30]`,
`[MUDANCA_RELEVANTE: SEGURANCA — sanitizacao de PII em log estruturado em duas camadas (PiiSanitizingEnricher + PiiMaskingFormatter) — source: backend/src/Squid.Service/SquidServiceHost.cs:42]`,
`[PADRAO_NOVO: WarningOtlpExporter (wrapper BaseExporter<Activity>) converte falha de export OTLP em Warning com endpoint, sem propagar excecao para request nem para shutdown — source: backend/src/Squid.Service/WarningOtlpExporter.cs:15]`.
