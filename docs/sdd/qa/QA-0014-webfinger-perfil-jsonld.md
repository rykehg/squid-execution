<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: CONCLUIDO
- owner: qa
- lastVerified: 2026-11-07 22:41:00

# QA-0014 — Webfinger + perfil JSON-LD com round-trip

- task: TASK-0015
- spec: SPEC-0007 (B-09, B-09a, B-10, B-11 — D-01 adjudicada)
- runId: R-20261107-02
- qaAttempt: 1/2
- verdict: PASSED

Independent verification of the 5 ACs of TASK-0015 against the acceptance list, plus the
B-09a settlement the dispatch asked to verify against the code (SPEC-0007 B-09a: the instance
domain comes from the REQUEST — origin = scheme + Host, no config key; `X-Forwarded-*` never read
(parked I-11); `subject`, `link.href` and the JSON-LD `id` all derive from that single origin).
QA wrote its own 5 scenarios (`Qa0014WebfingerAcceptanceTests`, real HTTP against the
`SquidServiceHost` the composition root builds) instead of re-reading DEV's assertions, proved the
tests can fail with two product mutations inside the task's own paths — both restored byte-exact
(SHA256 PRE = POST) with an mtime bump (S-23) — and recorded every classification from the
`exit:` header + summary **inside** the log under `docs/sdd/runs/test-runs/`, never from the
wrapper console (S-22; reproduced live again this attempt — the wrapper printed exit 3 for a real
red whose log says exit 1).

## What is proven

| AC | Scenario (executed) | Script | Result |
| --- | --- | --- | --- |
| AC-01 | `Ac01_webfinger_is_200_with_subject_and_a_self_link_to_the_profile`: register → 201; `GET /.well-known/webfinger?resource=acct:<handle>@<authority>` → 200, content type `application/jrd+json` (RFC 7033 §3.1), `subject` exactly `acct:…@authority`, exactly one `rel=self` link with `type=application/activity+json` and `href=<origin>/users/<handle>`, and `new Uri(href).Authority == Authority` (subject and href on the one asking origin — B-09a). Static half: origin built from `context.Request.Scheme` + `context.Request.Host.Value` (`DiscoveryEndpoints.cs:113`), href and profile both take `Origin(context)`, subject takes the Host-guarded `domain` (`DiscoveryEndpoints.cs:84`, `:99`, `:102`) | filtered xunit + `qa-check-task-0015.ps1/sh` AC01-* | PASS |
| AC-02 | `Ac02_webfinger_is_404_for_unknown_malformed_and_foreign_resources`: **11 refusals, all 404** — unknown (1), malformed (6: no `acct:`, no `@`, empty name, `ACCT:` case, double `@`, trailing junk), missing `resource` param (1), foreign (3: `example.org`, same host **without** port, same host **on another port** — DEV covers `127.0.0.1` sans port too). Actor exists in the foreign cases, so the 404 is the domain rule, not the lookup; every 404 body carries neither the handle nor a partial JRD (contract §7 item 9). Static half: the `string.Equals(domain, Request.Host.Value)` guard returns `NotFound` (`DiscoveryEndpoints.cs:84-87`) — **mutation M1 turns it red** | filtered xunit + checker AC02-* + M1 | PASS |
| AC-03 | `Ac03_profile_serves_without_session_with_2048_rsa_ed25519_and_no_private_material`: a fresh client with `UseCookies=false` (no cookie can ride along) → 200, `application/activity+json`, **no `Set-Cookie`**; a second request poisoned with `squid_session=not-a-real-session` → still 200 (no auth dependency leaked in). Document: `id`, `preferredUsername`, `type=Person`, fixed `@context` (`activitystreams` + `<origin>/ns/squid#`). RSA: `#main-key`, `owner == id`, PEM body == the persisted SPKI verbatim, `RSA.ImportSubjectPublicKeyInfo` → **KeySize 2048**. Ed25519: `#ed25519`, multibase decoded by **QA's own base58** (the product's `Base58` is internal — trusting it would let a broken decoder agree with itself) → `0xed 0x01` + 32 bytes == the persisted public key. Absence: 7 secrets (wrapped RSA key from the persisted stream, RK base64/hex-lower/hex-upper from the opened kit, password, `PRIVATE`, `WrappedSigningKey`) × 3 views — raw body, raw with `\u002B` unescaped, every JSON-decoded string (S-25) → 0 hits. Static half: 0 `X-Forwarded`/`ForwardedHeaders` in `backend/src` (parked I-11), Discovery carries 0 auth references, the middleware gates only mutations and holds no 401 | filtered xunit + checker AC03-* | PASS |
| AC-04 | `Ac04_round_trip_canonical_jsonld_canonical_keeps_every_field`: canonical → JSON-LD → canonical record **equality** (drop name, a key or the origin and it breaks), `id → JSON-LD → id` (B-11 literal), re-serializing what came back reproduces the **exact document bytes**, the nameless-actor path round-trips to `null` with `"name"` omitted from the wire, and the **server's own document over HTTP** materializes and re-serializes byte-identically (`served == Serialize(StateOf(Materialize(served)))`). Static half: composition root wired (`SquidServiceHost.cs:114`), `Serialize`/`Materialize` public, `ActorProfile` carries the five canonical members — **mutation M2 turns it red** | filtered xunit + checker AC04-* + M2 | PASS |
| AC-05 | Gate `sdd-test.ps1 test` → **log** `20261107-223928-test-gate.log` `exit: 0`, `Aprovado 156, Com falha 0` (151 baseline + 5 QA) — S-22, read inside the log. `git status --porcelain -uall` at QA end vs the 58-entry baseline captured at QA start: missing 0, out-of-scope 0, extras = only the 4 QA artifacts (test file, checker ps1/sh, this doc), on **both** platforms; no private-key PEM marker in the newest gate/filter logs | full gate + checker AC05-* / LOG-* (ps1 + sh) | PASS |
| Extra facet — B-09a settlement of D-01 (dispatch requirement) | `Extra_origin_comes_from_the_request_host_and_ignores_forwarded_headers`: (a) `X-Forwarded-Proto: https` + `X-Forwarded-Host: forwarded.example` on both endpoints → `href`/`id` **unchanged** on the asking origin (I-11 really parked — verified in code too: 0 reads of forwarded headers anywhere in `backend/src`); (b) forged `Host: qa0014-forged.example` with the matching resource → 200, `subject` and `href` follow the forged origin — the domain is the request's Host, not a config value (static half: the only `Configuration[` in the whole source tree is `Storage:Path`, `SquidServiceHost.cs:70`; 0 config/env reads in `Squid.Api/Discovery/**` and `Squid.Protocols/**`; 0 `appsettings*.json`); (c) the same real authority asked under the forged Host → 404 (single source); (d) `/users/<handle>` under the forged Host → `id` on the forged origin — subject, href and id share one origin | filtered xunit + checker AC01/AC03-* | PASS |

Baseline before QA wrote anything: `20261107-214525-test-gate.log` → `exit: 0`,
`Aprovado 151, Com falha 0` (read from the log, S-22).

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `backend/tests/Squid.Tests/Discovery/Qa0014WebfingerAcceptanceTests.cs` | xunit (project's own runner) | The 5 scenarios above, over real HTTP against `SquidServiceHost`; QA's own assertions and its own base58 decoder |
| `docs/sdd/qa/qa-check-task-0015.ps1` | Windows PowerShell 5.1 | 12 static + log + touches checks (single-origin code lines, no domain config, foreign-404 rule, no forwarded reads, no auth on Discovery, S-25 two views, round-trip wiring, gate/filter logs classified inside the file, 58-entry touches baseline, private-key markers in logs). Exit 0 = `SUMMARY total=12 fails=0`. Pure ASCII, so the PS-5.1 BOM trap of QA-0013 cannot apply |
| `docs/sdd/qa/qa-check-task-0015.sh` | bash / WSL2 | Linux twin: same 12 check ids, same baseline, same exit contract |
| `docs/sdd/_tools/sdd-test.ps1 test [-TestFilter …]` | pwsh + dotnet | The project's runner: the Discovery filter runs, both mutation runs, the final gate |

## How to reproduce

Prerequisites: Windows with .NET SDK 10, repo at `C:\dev\squid-squad-net\squid-execution`
(no network needed after restore); the `.sh` twin needs WSL (static/log/touches checks only —
see Not covered).

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Discovery   # expected: Aprovado 9, Com falha 0, log exit: 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0015.ps1                                                     # expected: SUMMARY total=12 fails=0, exit 0
wsl -e bash docs/sdd/qa/qa-check-task-0015.sh                                                                                              # expected: SUMMARY total=12 fails=0, exit 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test                                                      # expected: Aprovado 156, Com falha 0, log exit: 0
```

Mutation proof (S-16) — restore recipe if you repeat it: SHA256 the file, copy it to `%TEMP%`,
apply the described edit, run the filtered suite (red) and, for M1, the checker (red), copy the
backup back, verify SHA256 PRE == POST, **bump the file's mtime** (a restore rewinds it behind the
dll built from the mutated source — S-23), re-run the suite (green). The `%TEMP%\qa0015-*.bak`
files of this attempt were removed after both byte-exact verifications; re-creating them is the
first step above.

## Last run

| Date/Time | Command | Exit | Output (max 10 lines) |
| --- | --- | --- | --- |
| 2026-11-07 21:53 | read of `20261107-214525-test-gate.log` (baseline, S-22) | 0 | `exit: 0` / `Aprovado: 151, Com falha: 0` |
| 2026-11-07 21:53 | `git status --porcelain -uall` (baseline captured at QA start) | 0 | 58 entries — embedded in both checkers |
| 2026-11-07 22:17 | `sdd-test.ps1 test -TestFilter …Squid.Tests.Discovery` (1st) | 1 | log `20261107-221756` `exit: 1`, `Com falha: 1, Aprovado: 8` — QA's own AC-03 defect: line 138 read the persisted row as `ActorState`, but a fresh actor has `Snapshot = null` (threshold 8) and lives in the event log; fixed by the product's typed `SqliteActorEventStore.ReadAsync` |
| 2026-11-07 22:22 | same (2nd) | 0 | log `20261107-222243` `exit: 0`, `Com falha: 0, Aprovado: 9` (DEV 4 + QA 5) |
| 2026-11-07 22:26 | `powershell … qa-check-task-0015.ps1` (1st, 2nd) | 1 | own script defects only: reserved `$host`/`$profile` variables, then porcelain status prefix not stripped — fixed |
| 2026-11-07 22:29 | `powershell … qa-check-task-0015.ps1` (3rd) | 0 | `SUMMARY total=12 fails=0` / `RESULT: PASS` |
| 2026-11-07 22:31 | `wsl -e bash docs/sdd/qa/qa-check-task-0015.sh` (1st) | 1 | own script defect: positional arg used for min-approved — fixed |
| 2026-11-07 22:32 | same (2nd) | 0 | `SUMMARY total=12 fails=0` / `RESULT: PASS` |
| 2026-11-07 22:34 | `sdd-test.ps1 test -TestFilter …Discovery` (**mutation M1**) | 1 | log `20261107-223450` `exit: 1`, `Com falha: 3, Aprovado: 6` — DEV `Webfinger_answers_404_…` + QA `Ac02_…` + QA `Extra_…`; **wrapper console printed exit 3 (sandbox signature), log says exit 1 — S-22 reproduced live** |
| 2026-11-07 22:35 | `powershell … qa-check-task-0015.ps1` (under M1) | 1 | `FAIL AC01-single-origin-code`, `FAIL AC02-foreign-404-rule`, `FAIL AC05-filter-log-green` / `SUMMARY total=12 fails=3` |
| 2026-11-07 22:35 | restore M1 + `Get-FileHash` | 0 | `PRE = POST = 34D4CC507FD70477235EF38F3027DE6DF0BB0190F1F1C18FBB15D9F6D849E1A9`, `BYTE_EXACT=True`, mtime bumped to 2026-11-08 01:35:35 UTC, no mutation marker left |
| 2026-11-07 22:36 | same filter (after M1 restore) | 0 | log `20261107-223608` `exit: 0`, `Com falha: 0, Aprovado: 9` |
| 2026-11-07 22:37 | `sdd-test.ps1 test -TestFilter …Discovery` (**mutation M2**) | 1 | log `20261107-223724` `exit: 1`, `Com falha: 2, Aprovado: 7` — DEV round-trip + QA `Ac04_…`, both showing `Name = Alice da Silva` (expected) vs `Name =` (actual) |
| 2026-11-07 22:37 | restore M2 + `Get-FileHash` | 0 | `PRE = POST = C1CB00BAA7CA7170A6B657C37A8CC900A26452F58BE0BC6A75E1DE550F553342`, `BYTE_EXACT=True`, mtime bumped to 2026-11-08 01:37:39 UTC |
| 2026-11-07 22:38 | same filter (after M2 restore) | 0 | log `20261107-223816` `exit: 0`, `Com falha: 0, Aprovado: 9` |
| 2026-11-07 22:39 | `sdd-test.ps1 test` (**gate, AC-05**) | 0 | log `20261107-223928` `exit: 0`, `Aprovado: 156, Com falha: 0, Total: 156, Duração: 47 s` |
| 2026-11-07 22:40 | `powershell … qa-check-task-0015.ps1` (final) | 0 | `SUMMARY total=12 fails=0`; gate check reads `20261107-223928` `exit=0 Aprovado=156`, touches `baseline=58 missing=0 out-of-scope=0` |
| 2026-11-07 22:41 | `wsl -e bash docs/sdd/qa/qa-check-task-0015.sh` (final) | 0 | `SUMMARY total=12 fails=0` (same 12 ids on Linux) |

Rule S-22 respected: every classification above comes from the log's `exit:` header and its
summary line. In the M1 run the console and the log disagreed **on purpose** — the wrapper's
signature matcher saw the bare substring `handle` (inside my test's `existingHandle` parameter in
the stack trace) while its pt-BR summary regex does not know `Com falha!`, so it printed
`test: exit 1 with the sandbox signature` and exited **3 (environment)** for a real red; the log
`20261107-223450-test-filter.log` says `exit: 1` with `Com falha: 3`, which is the truth used here.

## Mutation proof (S-16)

| # | Mutation (product path, inside the task's touches) | Can-fail evidence | Restore proof |
| --- | --- | --- | --- |
| M1 | `backend/src/Squid.Api/Discovery/DiscoveryEndpoints.cs` — the foreign-domain guard replaced by `if (false) { }`, so any `acct:<existing>@<other-domain>` passes the B-09a check | xunit red: log `20261107-223450-test-filter.log` `exit: 1`, `Com falha: 3` — DEV `Webfinger_answers_404_for_unknown_malformed_and_foreign_resources` + QA `Ac02_…` + QA `Extra_…` (the forged-Host 404 case); checker red: `FAIL AC01-single-origin-code`, `FAIL AC02-foreign-404-rule`, `SUMMARY total=12 fails=3`, exit 1 | SHA256 PRE = POST = `34D4CC507FD70477235EF38F3027DE6DF0BB0190F1F1C18FBB15D9F6D849E1A9`, `BYTE_EXACT=True`; mtime bumped to 01:35:35 UTC (after the mutated dll); filtered suite green 9/9 (`20261107-223608`); final gate green |
| M2 | `backend/src/Squid.Protocols/ActivityPub/ProfileJsonLd.cs` — `ToDocument` writes `null` instead of `profile.Name`, so the display name is dropped from the JSON-LD (AC-04 "nada se perde") | xunit red: log `20261107-223724-test-filter.log` `exit: 1`, `Com falha: 2` — DEV `The_round_trip_canonical_to_jsonld_and_back_loses_nothing` and QA `Ac04_…`, both with `Name = Alice da Silva` vs `Name =` in the assertion diff | SHA256 PRE = POST = `C1CB00BAA7CA7170A6B657C37A8CC900A26452F58BE0BC6A75E1DE550F553342`, `BYTE_EXACT=True`; mtime bumped to 01:37:39 UTC; filtered suite green 9/9 (`20261107-223816`); final gate green |

After both restores: checker 12/12 (ps1) and 12/12 (sh), full gate `exit: 0` with 156/156 —
the restored bytes are proven by an actual green run, not only by hash. Mutations were applied
only inside `backend/src/Squid.Api/Discovery/DiscoveryEndpoints.cs` and
`backend/src/Squid.Protocols/ActivityPub/ProfileJsonLd.cs`; no file of `Auth/`, `Crypto/`,
`Grains/Actors/`, `Recovery/`, another QA doc or the task control block was written, modified or
reverted at any moment.

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| 1 | MINOR (harness, pre-existing — S-22, reproduced again) | `docs/sdd/_tools/sdd-test.ps1:80` and `:81` | On a real red whose output contains the bare substring `handle` (my test's `existingHandle` parameter in the failing stack trace) and whose pt-BR summary starts with `Com falha!`, the wrapper's signature matcher fires and its summary regex misses, so it reports **exit 3 (environment)** and "NOT a failing test" for a real failure. Proven again in this attempt: wrapper exit 3 vs log `exit: 1` / `Com falha: 3` | `20261107-223450-test-filter.log` header + summary vs wrapper console |
| 2 | MINOR (contract end-state vs this task's scope — not a failed AC; raised as a memory-bank proposal) | `specs/001-squid-mvp/contracts/activitypub.md:12` and `:14` vs `DiscoveryEndpoints.cs:96-106`, `ProfileJsonLd.cs:73-84` | The published contract lists `rel=profile` alongside `rel=self` in webfinger, and `inbox`, `outbox`, `followers`, `following` in the actor document (§1.1 "formato minimo"). The implementation serves only `rel=self` and no collection fields — and no `inbox`/`outbox` route exists anywhere yet (`grep inbox backend/src` → 0). SPEC-0007 B-09/B-10 (this task's authority) require exactly what is served, and the ACs do not name those fields → coverage observation for ODEV, not a red | grep `inbox\|outbox` over `backend/src` = 0; contract lines vs `ProfileJsonLd.cs:73` |
| 3 | MINOR (QA's own, fixed before the verdict) | `backend/tests/Squid.Tests/Discovery/Qa0014WebfingerAcceptanceTests.cs:138` (1st run) and the checkers' first runs | QA's own AC-03 scenario read the persisted row as `ActorState`, but a fresh actor has `Snapshot = null` (threshold 8) and its state lives in the `ActorCreated` event → NRE. Fixed by reading through the product's typed path (`SqliteActorEventStore.ReadAsync`). Own script defects likewise fixed (reserved `$host`/`$profile` variables; porcelain status prefix; a positional arg) — none of them touched product code | `20261107-221756-test-filter.log` `exit: 1` vs green `20261107-222243`; checker runs in Last run |

Out-of-scope interference: none observed. No red originated in `Auth/`, `Crypto/`,
`Grains/Actors/`, `Recovery/` or another task's suite during any of this attempt's runs; the final
gate (156/156) includes the peer TASK-0012/0013/0014 suites green, and QA-0014 wrote only its own
artifacts plus the two mutated files above (both restored byte-exact).

## Not covered, and why

- **Linux build/test (`dotnet test` under Linux)** — the only WSL distro has no .NET SDK and
  installing one needs a gate approval that was not granted → `unverified`, never assumed equal to
  the Windows result. The static/log/touches half *did* run on Linux (`qa-check-task-0015.sh`,
  12/12 on WSL2). CI (`.github/workflows/ci.yml`) runs the same gate with pwsh on ubuntu — risk of
  a Linux-only divergence falls to that pipeline.
- **`X-Forwarded-*` honoured behind a real proxy (Caddy)** — parked as I-11 by design; this
  attempt proved the opposite today: the headers are *ignored* (code grep + live requests). The
  future proxy phase will change that behaviour and will also have to pin the accepted `Host`
  values — B-09a as specified answers on whatever Host arrives (proven by the forged-Host 200 in
  the extra facet), which is correct per the spec now but is the door I-11 has to close.
- **`rel=profile` link and nodeinfo (contract §1)** — not required by B-09/AC-01; see finding 2.
- **Federation end-to-end (signature verification over `#main-key`, inbox delivery)** — the routes
  do not exist yet (contract end-state); out of this task's ACs.
- **Performance (< 500 ms)** — SPEC-0007 non-functional, not an AC of this task; not measured
  formally (executed runs finished in seconds, anecdotal only).
