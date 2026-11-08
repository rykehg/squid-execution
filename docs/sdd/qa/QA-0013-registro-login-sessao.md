<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: CONCLUIDO
- owner: qa
- lastVerified: 2026-11-07 20:49:14

# QA-0013 — Registro + login/sessão (cookie, CSRF, logout)

- task: TASK-0014
- spec: SPEC-0007
- runId: R-20261107-02
- qaAttempt: 1/2
- verdict: PASSED

Independent verification of the 6 ACs of TASK-0014 against the acceptance list, plus the facet
QA-0012 deferred to this task (the clear RK never appears in an HTTP response — now a real
surface) and the contract facts ODEV adjudicated (pre-auth exemption of `/register` + `/login`,
byte-identical 401 with `traceId` omitted only there and a dummy hash always verified, the two
cookie shapes, the kit once in the `http-api.md` §3 format, one KV row per handle in `auth` /
`account/<handle>` with no new table). QA wrote its own 6 scenarios
(`Qa0013AuthAcceptanceTests`, real HTTP against the `SquidServiceHost` the composition root
builds) instead of re-reading DEV's assertions, re-executed DEV's 22 tests as the existing
suite, and proved the tests can fail with two product mutations inside the task's own paths —
both restored byte-exact (S-16) with an mtime bump (S-23). Every classification below comes
from the `exit:` header and the summary line **inside** the log under
`docs/sdd/runs/test-runs/`, never from the wrapper console (S-22, reproduced live in this
attempt).

## What is proven

| AC | Scenario (executed) | Script | Result |
| --- | --- | --- | --- |
| AC-01 | `Ac01_register_is_201_with_both_contract_cookies_and_exactly_one_kit`: 201; `squid_session` carries `httponly`, `secure`, `samesite=lax`, and an `expires` parsed and asserted inside 29.5–30.5 d; `squid_csrf` **without** `httponly`, `secure`, `samesite=lax`; `"kit"` occurs exactly once in the body; kit fields are exactly `recoveryCredential, mnemonic, encryptedRootKey, createdAt` (contract §3), mnemonic = 24 words, RC matches `^RC-([0-9A-F]{4}-)+[0-9A-F]{4}$`, `encryptedRootKey` decodes as base64, `createdAt` parses as ISO-8601; password absent from the body; actor row + credential row exist. Static half: exempt paths = exactly register+login (`AuthSessionMiddleware.cs:16`), cookie options (`AuthSessions.cs:86`), 30-day `Lifetime` + slide + re-issue (`AuthSessionMiddleware.cs:49`), kit shape at `AuthEndpoints.cs:106` — 201 | filtered xunit + `qa-check-task-0014.ps1/sh` AC01-* | PASS |
| AC-02 | `Ac02_refused_registrations_answer_409_422_and_persist_nothing`: duplicate → 409 naming the handle, `em uso`, code `handle_taken`; malformed `Ab!` → 422 quoting the rule `[a-z0-9_]{3,30}` verbatim; 9-char password → 422 citing `10`; store counts grow by exactly the one valid registration (`actors` +1, `auth` +1) and the refused handle has no row in either store. Static half: both 422 returns and the 409 return all precede `await store.CreateAsync` (`AuthEndpoints.cs:43`, `:50`, `:83` vs `:101`) | filtered xunit + checker AC02-* | PASS |
| AC-03 | `Ac03_the_two_login_401_bodies_are_byte_identical_and_trace_less`: login → 200 + both cookies; wrong-password 401 vs unknown-handle 401 compared as **raw bytes** (`SequenceEqual` + equal SHA-256, equal content-type); neither body carries `traceId`, the handle or the password; no account row is created for the unknown handle; `AuthEndpoints.cs:130` reads `account?.PasswordHash ?? PasswordHasher.DummyHash`. Static half: both 401 branches use `withTraceId: false` (`:134`, `:146`), the traceless envelope exists (`AuthErrors.cs:19`) — **mutation M2 turns it red** | filtered xunit + checker AC03-* + M2 | PASS |
| AC-04 | `Ac04_csrf_refuses_a_mutation_and_register_and_login_stay_exempt`: logout without header → 403 `csrf_failed`; divergent header → 403; `logout-all` without header → 403; pre-auth proof — login with no header → 401 (not 403) and register with no header → 201; correct header → 200 (the refusals revoked nothing). Static half: gate at `AuthSessionMiddleware.cs:33`, fixed-time compare (`AuthSessions.cs:63`) — **mutation M1 turns it red** | filtered xunit + checker AC04-* + M1 | PASS |
| AC-05 | `Ac05_logout_and_logout_all_revoke_and_the_row_carries_only_the_phc_hash`: logout-all from one jar → both other jars 401 (with and without token); single logout → replay of the very same cookie 401 (with and without token); persisted row carries exactly one `$argon2id$v=19$m=4096,t=3,p=1$…` field, **no** plaintext/base64/hex form of the password in the raw row *or* in the decoded JSON, `PasswordHasher.Verify` true for this password and false for another (product's own read path), actor row clean. Log grep: run logs × 4 password literals + the RK forms of every run → 0 hits; no log statement in the touched paths names password/senha. Storage shape: the stores contain no inline SQL and no DDL, `auth`/`account/<handle>` + `actors`/`actor/<handle>`, schema reused from TASK-0003 | filtered xunit + checker AC05-* | PASS |
| AC-06 | Gate `sdd-test.ps1 test` → **log** `exit: 0`, `Aprovado 147, Com falha 0` (141 baseline + 6 QA) in `20261107-203804-test-gate.log`; `Version="` attributes across the 10 csproj = 0 (3 literal `Version=` occurrences, all in comments); `git status --porcelain -uall` = 51 entries vs the 48 captured at QA start, **0** outside the touches and the QA-0013 prefixes, on both platforms | full gate + checker AC06-* (ps1 + sh) | PASS |
| Extra facet (FR-004, B-03, deferred by QA-0012) | `Extra_the_clear_root_key_and_the_password_never_appear_in_any_auth_response`: the kit is opened with the password (32-byte RK asserted) and **7 response bodies** — register 201, login 200, both 401s, logout 403, register 409, register 422 — are scanned for the password and for the RK in base64 + hex-lower + hex-upper, **both raw and JSON-decoded** (System.Text.Json escapes `+` inside base64, so a raw-only scan can miss it) → 0 | filtered xunit | PASS |
| Adjudications | CSRF exempt set = register+login only; 401 byte-equal with `traceId` omitted only there + dummy hash always verified; cookies `HttpOnly+Secure+SameSite=Lax` (session) and not-`HttpOnly` (csrf); kit once in §3 shape; persistence = one row per handle, store `auth`, key `account/<handle>`, no migration; `GET /api/auth/me` absent (TASK-0016); S-21 `SqliteActorEventStore` registered at the composition root and `Squid.Api.csproj` still free of `Squid.Persistence` (ADR-35) — all verified against the code, both platforms | checker AC01/AC03/AC05/ADJ/S21 + source read | PASS |

Baseline before QA wrote anything: full gate `20261107-194741-test-gate.log` → `exit: 0`,
`Aprovado 141, Com falha 0`.

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `backend/tests/Squid.Tests/Auth/Qa0013AuthAcceptanceTests.cs` | xunit (project's own runner) | The 6 scenarios above, over real HTTP against `SquidServiceHost`; QA's own assertions, expected values taken from the contract/spec |
| `docs/sdd/qa/qa-check-task-0014.ps1` | Windows PowerShell 5.1 / pwsh | 16 static + log checks (contract shapes, write order, PII source scan, run-log grep for passwords and RK marker forms, `Version="`, touches delta, adjudications). Exit 0 = `SUMMARY total=16 fails=0`. UTF-8 **with BOM** (finding 1) |
| `docs/sdd/qa/qa-check-task-0014.sh` | bash / WSL2 | Linux twin: same 16 check ids, same exit contract, same baseline; resolves the Windows `%TEMP%` for the RK marker grep |
| `docs/sdd/_tools/sdd-test.ps1 test [-Fast] [-TestFilter …]` | pwsh + dotnet | The project's runner: filtered scenario runs, both mutation runs, the final gate |

## How to reproduce

Prerequisites: Windows with .NET SDK 10, repo at `C:\dev\squid-squad-net\squid-execution`
(no network needed after restore); the `.sh` twin needs WSL (static checks only — see Not covered).

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~Squid.Tests.Auth.Qa0013   # expected: Aprovado 6, Com falha 0, log exit: 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0014.ps1                                                                # expected: SUMMARY total=16 fails=0, exit 0
wsl -e bash docs/sdd/qa/qa-check-task-0014.sh                                                                                                         # expected: SUMMARY total=16 fails=0, exit 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test                                                                 # expected: Aprovado 147, Com falha 0, log exit: 0
```

Order matters for the RK half of the checker: it greps the run logs for the forms the suite
leaves in `%TEMP%\qa0013-rk-*.txt`, so run a suite command first; the `PROBE rk-markers = 0`
line labels an invocation in which that half was vacuous.

Mutation proof (S-16) — restore recipe if you repeat it: SHA256 the file, copy it to `%TEMP%`,
apply the described edit, run the filtered suite (red) and the checker (red), copy the backup
back, verify SHA256 PRE == POST, **bump the file's mtime** (a restore rewinds it behind the dll
built from the mutated source — S-23), re-run the suite (green).

## Last run

| Date/Time | Command | Exit | Output (max 10 lines) |
| --- | --- | --- | --- |
| 2026-11-07 19:47 | `sdd-test.ps1 test` (baseline before QA wrote) | 0 | log `exit: 0`; `Aprovado! – Com falha: 0, Aprovado: 141, Total: 141` |
| 2026-11-07 19:56 | `sdd-test.ps1 test -Fast -TestFilter …Qa0013` (1st) | 1 | `CS1061 RootKey.Length`, `CS1503`, `ToHexStringUpper` — QA's own test file did not compile; fixed, re-run green |
| 2026-11-07 19:58 | `sdd-test.ps1 test -Fast -TestFilter …Qa0013` | 0 | log `exit: 0`; `Aprovado 6, Com falha 0` (AC-01..AC-05 + facet) |
| 2026-11-07 20:00 | `powershell … qa-check-task-0014.ps1` (1st) | 1 | `FAIL AC02-validation-before-persist … =-1` — BOM-less script read as cp1252 by PS 5.1 (finding 1); fixed with UTF-8 BOM |
| 2026-11-07 20:06 | `powershell … qa-check-task-0014.ps1` (after BOM) | 0 | `SUMMARY total=16 fails=0` / `RESULT: PASS` |
| 2026-11-07 20:11 | `sdd-test.ps1 test -Fast -TestFilter …Squid.Tests.Auth` (**mutation M1a**) | 1 | log `exit: 1`; `Com falha: 5, Aprovado: 23` — first M1 variant made the gate fire unconditionally (inverted); re-applied as M1b |
| 2026-11-07 20:14 | same (**mutation M1b, gate removed**) | 1 | log `exit: 1`; `Com falha: 2, Aprovado: 26` — DEV `A_mutation_is_403_without_the_csrf_token…` + QA `Ac04_…` (line 224) |
| 2026-11-07 20:14 | `powershell … qa-check-task-0014.ps1` (under M1b) | 1 | `FAIL AC04-csrf-gate` / `SUMMARY total=16 fails=1` |
| 2026-11-07 20:15 | restore M1 + `Get-FileHash` | 0 | `PRE = POST = F2F22F2269D011ED6DB70978382A0EFF515424492C6A8002F4966324A37EE948`, `BYTE_EXACT=True`, mtime bumped |
| 2026-11-07 20:15 | `sdd-test.ps1 test -Fast -TestFilter …Squid.Tests.Auth` (after M1 restore) | 0 | log `exit: 0`; `Aprovado 28, Com falha 0` |
| 2026-11-07 20:17 | same (**mutation M2**) | 1 | log `exit: 1`; `Com falha: 2` — DEV `Login_answers_one_401_for_a_wrong_password_and_for_an_unknown_handle` + QA `Ac03_…`; probes `sha256=2D498481…` vs `B1D78DFA…`, both `len=119`; **wrapper printed `exit 3` (sandbox signature) while the log says `exit: 1` — S-22 reproduced again** |
| 2026-11-07 20:17 | `powershell … qa-check-task-0014.ps1` (under M2) | 1 | `FAIL AC03-single-401-shape … tracelessShape=False` |
| 2026-11-07 20:17 | restore M2 + `Get-FileHash` | 0 | `PRE = POST = 81BE69E8975F84D51A1E89B5558531BC9745DD7EF16544F8779ADC29B56FA1C7`, `BYTE_EXACT=True`, mtime bumped |
| 2026-11-07 20:19 | `sdd-test.ps1 test` (gate) | 1 | log `exit: 1`; `Com falha: 1, Aprovado: 146, Total: 147` — QA `Ac05` line 302 `the stored PHC does not verify this password` (finding 2: QA's raw-JSON extraction) |
| 2026-11-07 20:31 | `dotnet fsi --exec %TEMP%/qa0013-phc.fsx` (root cause) | 0 | `raw-regex extraction unusable … 124 / 200`; `json-decoded extraction unusable … 0 / 200` — product unaffected (`login` answered 200 in the same run) |
| 2026-11-07 20:36 | `sdd-test.ps1 test -Fast -TestFilter …Squid.Tests.Auth` (after test fix) | 0 | log `exit: 0`; `Aprovado 28, Com falha 0` |
| 2026-11-07 20:38 | `sdd-test.ps1 test` (**gate rerun, AC-06**) | 0 | log `exit: 0`; `Aprovado! – Com falha: 0, Aprovado: 147, Ignorado: 0, Total: 147, Duração: 38 s` |
| 2026-11-07 20:39 | `powershell … qa-check-task-0014.ps1` (final) | 0 | `SUMMARY total=16 fails=0`; `logs-scanned = 123 ; rk-markers = 4 ; rk-forms = 12`, password hits 0, RK hits 0 |
| 2026-11-07 20:45 | `wsl -e bash docs/sdd/qa/qa-check-task-0014.sh` (final) | 0 | `SUMMARY total=16 fails=0`; `logs-scanned = 125 ; rk-markers = 1 ; rk-forms = 3` (real grep, then consumed) |
| 2026-11-07 20:48 | `sdd-test.ps1 test -Fast -TestFilter …Squid.Tests.Auth` (last green) | 0 | log `exit: 0`; `Aprovado 28, Com falha 0` |
| 2026-11-07 20:48 | `powershell … sdd-state.ps1 check <task>` | 0 | `check: OK - log entries 10, evidence rows 30` |

Rule S-22 respected: every classification above comes from the log's `exit:` header and its
summary line. In the M2 run the console and the log **disagreed on purpose** — the wrapper's
signature matcher saw the bare substring `handle` (inside a failing test name) while its summary
regex missed the pt-BR line `Com falha!`, so it printed `test: exit 1 with the sandbox
signature` and exited **3 (environment)** for a real red; the log
`20261107-201705-test-fast.log` says `exit: 1` with `Com falha: 2`, which is the truth used
here.

## Mutation proof (S-16)

| # | Mutation (product path, inside the task's touches) | Can-fail evidence | Restore proof |
| --- | --- | --- | --- |
| M1a | `backend/src/Squid.Api/Auth/AuthSessionMiddleware.cs` — CSRF clause rewritten so the gate fires for **every** non-exempt mutation (inverted; first attempt) | xunit red: 5 failures, log `20261107-201134-test-fast.log` `exit: 1`, `Com falha: 5` — DEV logout + AC-04 tests and QA `Ac05`/`Ac04` | superseded by M1b inside the same mutation cycle |
| M1b | same file — the whole CSRF gate block removed (mutations run with no token at all) | xunit red: QA `Ac04_…` :224 `Expected: OK / Actual: …` + DEV `A_mutation_is_403_without_the_csrf_token_and_runs_with_it`, log `20261107-201416-test-fast.log` `exit: 1`, `Com falha: 2`, `Aprovado: 26`; checker red: `FAIL AC04-csrf-gate`, exit 1 | SHA256 PRE = POST = `F2F22F2269D011ED6DB70978382A0EFF515424492C6A8002F4966324A37EE948`, `BYTE_EXACT=True`; mtime bumped to 20:15:10 (after the mutated dll); filtered suite green 28/28; final gate green |
| M2 | `backend/src/Squid.Api/Auth/AuthErrors.cs` — the traceless branch returns a fresh `traceId = Guid.NewGuid()` per call, so the two 401s differ | xunit red: QA `Ac03_…` (SHA-256 `2D498481…` ≠ `B1D78DFA…`) + DEV `Login_answers_one_401_for_a_wrong_password_and_for_an_unknown_handle`, log `20261107-201705-test-fast.log` `exit: 1`, `Com falha: 2`; checker red: `FAIL AC03-single-401-shape`, exit 1 | SHA256 PRE = POST = `81BE69E8975F84D51A1E89B5558531BC9745DD7EF16544F8779ADC29B56FA1C7`, `BYTE_EXACT=True`; mtime bumped to 20:17:57; filtered suite green 28/28; final gate green |

After both restores: checker 16/16 (ps1) and 16/16 (sh), full gate `exit: 0` with 147/147 —
the restored bytes are proven by an actual green run, not only by hash. Mutations were applied
only inside `backend/src/Squid.Api/Auth/**`; no file of `Squid.Api/Crypto/**`,
`Squid.Grains/Actors/**`, `Squid.Api/Recovery/**` or of an earlier QA doc was written, modified
or reverted at any moment.

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| 1 | MINOR (QA's own, fixed) | `docs/sdd/qa/qa-check-task-0014.ps1` (1st run) | A UTF-8 **without BOM** script is read as cp1252 by Windows PowerShell 5.1, so every accented matching literal (`handle inválido`, `senha inválida`) matched nothing and a content-green check failed: `FAIL AC02-validation-before-persist … =-1`. Fixed by writing the file with a UTF-8 BOM. Rule for this repo: any `.ps1` that *matches* accented text must ship a BOM — the existing tools only use accents in display strings (`sdd-test.ps1:85`, `:173`), where the same decoding only prints mojibake | 1st checker run exit 1, 2nd exit 0 (both in Last run) |
| 2 | MINOR (QA's own, fixed) | `backend/tests/Squid.Tests/Auth/Qa0013AuthAcceptanceTests.cs:302` (line at the time of the red) | The gate went red on QA's own AC-05 scenario: the PHC was extracted from the **raw** row JSON with a regex, but `System.Text.Json` (default encoder, used at `SqliteGrainStorage.cs:94`) escapes `+` inside base64 as `+`, so `PasswordHasher.Verify` received `+` where a salt/hash had `+` and returned false. Measured: raw extraction unusable **124/200**, decoded **0/200**; the product is unaffected — `login` answered 200 in that very run because the product deserializes. The test now reads the row through `JsonSerializer.Deserialize<AuthAccount>` and scans raw **and** decoded text | `dotnet fsi` probe 124/200 vs 0/200; gate log `20261107-201948-test-gate.log` `exit: 1`; green after the fix: `20261107-203658`, `20261107-203804` |
| 3 | MINOR (coverage note for DEV, not fixed by QA) | `backend/tests/Squid.Tests/Auth/RegisterLoginSessionTests.cs:247` and `:253` | DEV's PII test searches response bodies and persisted rows for the **base64** form of the clear RK as raw text. In JSON, base64 containing `+` is written escaped, so roughly half of all random keys would escape that raw scan (a leak would be invisible to it). QA-0013's facet covers the decoded view, so AC-05 stays covered; DEV's assertion is weaker than it looks. Reported, not changed (QA does not edit DEV's tests) | base64 `+` rate 155/300 for 32 random bytes (measured in this session); QA's facet scans raw + decoded |
| 4 | MINOR (harness, pre-existing — S-22, reproduced again) | `docs/sdd/_tools/sdd-test.ps1:80` and `:81` | On a real red whose output contains the bare substring `handle` and whose pt-BR summary starts with `Com falha!`, the wrapper's signature matcher fires and its summary regex misses, so it reports **exit 3 (environment)** and "NOT a failing test" for a real failure. Proven in this attempt: wrapper exit 3 vs log `exit: 1` / `Com falha: 2` | `20261107-201705-test-fast.log` header + summary vs wrapper console |

Out-of-scope interference: none observed. No red originated in `Squid.Api/Crypto/**`,
`Squid.Grains/Actors/**`, `Squid.Api/Recovery/**` or another task's suite during any of this
attempt's runs; the final gate (147/147) includes the peer TASK-0012/0013 suites green, and
QA-0013 never wrote outside its own artifacts plus the two mutated files above (both restored).

## Not covered, and why

- **Linux build/test (`dotnet test` under Linux)** — the only WSL distro
  (`podman-machine-default`) has no .NET SDK and installing one needs a gate approval that was
  not granted → `unverified`, never assumed equal to the Windows result. The static half *did*
  run on Linux (`.sh` twin, 16/16 on WSL2), including the log/RK grep and the touches delta.
  CI (`.github/workflows/ci.yml`) runs the same gate with pwsh on ubuntu — risk of a
  Linux-only divergence falls to that pipeline, not to this attempt.
- **Span/OTLP PII** — these runs have no collector, so no span was read back. Covered by the
  source scan (no log or activity statement in the touched paths names password/senha; the
  Serilog masking pipeline is TASK-0005's) and by the run-log grep — risk: an Activity tag
  carrying PII would need `sdd-test.ps1 compose`, not executed here.
- **Cookie slide as a delta between two requests** — proved by code (Slide + WriteCookies on the
  authenticated request, both platforms), by DEV's unit `A_session_lives_thirty_days_and_the_slide_renews_only_the_one_in_use`, and dynamically by the 30-day `expires` on the response; a second request's fresh `Set-Cookie` was not compared → risk: low (single code path).
- **Expired session after 30 days → 401** — only the `Expires` window and the `ExpiresAt > now`
  branch were checked; no wall-clock wait → risk: low.
- **Concurrent duplicate registration** (two clients racing the same handle) — the grain-index
  409 path is code-checked (`AuthEndpoints.cs:79`) but was executed only sequentially → risk:
  the concurrent loser's row is unproven at runtime (DEV recorded the same limit as a
  `shortcut:` at `AuthEndpoints.cs:98`).
- **Performance `< 500 ms`** (SPEC-0007 non-functional) — not an AC of this task; not measured
  formally (the executed register/login runs finished well inside it, anecdotal only).
