<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: CONCLUIDO
- owner: qa
- lastVerified: 2026-11-08 01:09:02

# QA-0015 — Kit de recuperação: gerar, verificar e importar

- task: TASK-0016
- spec: SPEC-0007 (B-12..B-15, FR-005..FR-007)
- runId: R-20261107-02
- qaAttempt: 1/2 (redispatch: the previous QA subagent died mid-run — no verdict, no doc; its artifacts were read, verified against the ACs and re-executed from scratch by this run, whose verdicts depend on no log it left)
- verdict: PASSED

Independent verification of the 5 ACs of TASK-0016 plus the P-02 DoD facet, by executing QA's own
4 acceptance scenarios (`Qa0015RecoveryAcceptanceTests`, real HTTP against the `SquidServiceHost`
composition root) inside the project's own runner, the two static/log/touches checkers on both
platforms, and four can-fail product mutations — one per interesting AC — each restored byte-exact
(SHA256 PRE = POST = `1E14DA6AA26F6F9F647DB889822B5DA1789F153561348C1C0D6B23179EB21E9E`)
with an mtime bump (S-16/S-23). Every classification comes from the `exit:` header and summary
**inside** the logs under `docs/sdd/runs/test-runs/`, never from the wrapper console (S-22 —
reproduced live again: the M3 run's console printed exit 3 for a real red whose log says exit 1).

## What is proven

| AC | Scenario (executed) | Script | Result |
| --- | --- | --- | --- |
| AC-01 | `Ac01_the_kit_is_201_with_exactly_five_fields_24_words_once_and_no_read_route_recovers_it` (`Qa0015RecoveryAcceptanceTests.cs:35`): anonymous POST → 401, session without CSRF → 403 (authenticated); POST → **201** with exactly the sorted set `createdAt, encryptedRootKey, mnemonic, recoveryCredential, wrappedSigningKey` (contract §3's four + `wrappedSigningKey`, data-model §2.12/AC-03) and nothing else; `mnemonic` = **24** entries == the AC's literal == `Mnemonic.WordCount`, and probe against the artifact: QA parses `recoveryCredential` back to bytes itself and asserts the words == `Mnemonic.FromRecoveryCode(those bytes)` (the response's words are this kit's words, not a remembered label); `"mnemonic"` occurs exactly once in the body; `RK` opened here with the password (`RootKeyCipher.Decrypt`) appears **nowhere** in the kit response in base64/hex-lower/hex-upper + the password, in all three views — raw, `\uXXXX`-decoded, JSON string leaves (S-25); no other route recovers it: GET on the three recovery paths → 405 (POST-only), and `GET /api/auth/me`, `/health`, `/users/{h}`, webfinger, `/api/actors/{h}` carry none of: RK forms, password, RC bytes, `RC-…` credential, the 24 words joined three ways; the register response (which carries its own kit) never sees the clear RK either. **Mutation M1 turns it red at :106** (`Expected: MethodNotAllowed / Actual: Created`) | filtered xunit + checker `ROUTES-STATIC` + M1 | PASS |
| AC-02 | `Ac02_me_switches_between_unverified_both_stale_and_silent_in_every_state` (`:135`): anonymous `GET /api/auth/me` → 401 (contract §2, actor only); state 1 fresh unverified → exactly `["kit_unverified"]`; state 2 kit aged 13 months (row backdated directly — the ageing no route offers) → exactly `["kit_unverified","kit_stale"]` in report order; verify without CSRF → 403, with CSRF → 200 carrying `handle` + parseable `verifiedAt`; state 3 verified+aged → exactly `["kit_stale"]` (verify silenced only the unverified half); state 4 verified+fresh → **silent** (so the stale half really is the age, >12 months per `RecoveryWarnings.KitAgeMonths = 12`). **Mutation M2 turns it red at :177** (`Expected: ["kit_stale"] / Actual: ["kit_unverified","kit_stale"]`) | filtered xunit + checker + M2 | PASS |
| AC-03 | `Ac03_a_downloaded_kit_restores_the_identity_and_webfinger_resolves_the_same_keys` (`:194`): origin instance (register → export kit → webfinger chain resolve → stop); premise of *instância limpa* proved **before** import: fresh host = fresh temp DB (`AuthHostFixture.cs:30`) answering 404 for `/users/{h}` and webfinger; import with **no session** (pre-login path, the AC-04 rule) carrying the kit in flat fields + `handle` + `password` → **201** with the same handle; then the DoD chain in one breath: webfinger on the restored instance resolves the same handle (subject `acct:{h}@authority`, exactly one `rel=self` of type `activity+json`, href on this instance's authority with path `/users/{h}`, `preferredUsername == handle`) and the profile publishes **string-identical** RSA PEM and Ed25519 multibase to the origin's — read by QA's own JSON walk and decoded by QA's own base58 (asserts `z` prefix, 34 bytes, `0xed 0x01` multicodec; the product's decoder never grades itself); the kit rehydrated the RK: `RootKeyCipher.Decrypt` + `KeyDerivation.DeriveEd25519PublicKey` equals the published key, and both restored private keys **sign/verify**: RSA (unwrap `wrappedSigningKey` with the RK → `SignData` → verified by the PEM the restored instance publishes) and Ed25519 (HKDF seed from the RK → verified against the published multibase); the 201 body carries no clear RK/password. **Mutation M3 (drop `wrappedSigningKey` from the kit) turns the round-trip red** — `KeyNotFoundException` in the export half (`:350` via `:200`) plus Ac01's 5→4 field diff — proving the field is what carries the RSA to the clean instance | filtered xunit + checker + M3 | PASS |
| AC-04 | `Ac04_a_wrong_password_is_one_typed_401_and_keeps_every_row_byte_identical` (`:257`): SHA-256 over **every row of every store** (`StoreName\|GrainId\|State\|Version` over all of `GrainState` — the only table, `SqliteGrainStorage.cs:21`) taken after export; the wrong-password attempt runs **without a session** on purpose (an authenticated request would slide the session expiry by B-05 and rewrite the compared rows — the rule ODEV recorded for QA); answer = one typed refusal: 401, `error.code == "recovery_failed"` exactly once in the body, non-empty message; hash before == hash after, byte for byte; the refusal carried out no RK material (clear or password) in any view; the account still logs in with its own password afterwards. **Mutation M4 (the wrong-key path now writes a row before refusing) turns it red at :296 — the hash assertion itself** (`Expected 1eeda497… / Actual 39e6678e…`): the 401/code assertions passed and the byte-identity half is the one that caught the write | filtered xunit + M4 | PASS |
| AC-05 | Full gate `sdd-test.ps1 test` → log `20261108-010157-test-gate.log` `exit: 0`, `Aprovado 167, Com falha 0` (163 suite + 4 QA), read inside the log (S-22); no writes outside `touches`: `git status --porcelain -uall` at QA end = 73 entries = the checkers' 69-entry baseline captured at QA start (all still present, `missing=0`) + only the 4 QA artifacts (`Qa0015RecoveryAcceptanceTests.cs`, `qa-check-task-0016.ps1/.sh`, this doc) — asserted by `TOUCHES-NO-OUTSIDE` on both platforms; mutation residue scan `grep QA-MUTATION backend` → **0 hits**; the mutated file's final SHA256 == its PRE hash (checker `RESTORE-SHA256`) | full gate + both checkers | PASS |
| DoD facet (P-02, dispatch requirement) | The webfinger chain and the kit restoration are asserted in **one chain inside Ac03**: the same test that imports the kit resolves webfinger → self link → profile on the restored instance and compares both public keys string-for-byte with what the origin resolved through the identical chain (`ResolvedAsync`, `:358`) — "webfinger resolve + kit restaura" proven together | filtered xunit (Ac03) | PASS |

Baseline before this QA wrote anything: `20261108-004409-test-gate.log` → `exit: 0`,
`Aprovado 167, Com falha 0`; Recovery filter `20261108-004513-test-filter.log` → `exit: 0`,
`Aprovado 11, Com falha 0` (both read from the logs, S-22).

## Inherited artifacts — read, verified, never trusted

The previous QA agent died mid-run and left three artifacts. Verified before use:

- `backend/tests/Squid.Tests/Recovery/Qa0015RecoveryAcceptanceTests.cs` (4 tests) — mapped line
  by line to the ACs above (the mapping table is this run's own re-execution, not its claims).
  Its provenance: the agent's first run `20261108-001551-test-filter.log` was **red on its own
  helper** (`Ac03 … FormatException at PemBody`, old line 476); the fixed helper strips exactly
  the marker the product emits (`ProfileJsonLd.cs:20` = `-----BEGIN PUBLIC KEY-----`) and the
  string-equality + signature checks around it were never weakened — its rerun `001758` went
  11/11. This attempt re-ran everything; no verdict above depends on those logs.
- `docs/sdd/qa/qa-check-task-0016.ps1` / `.sh` — 7 checks each: newest gate/filter classified
  from the logs' own `exit:` + summary (S-22), `TOUCHES-NO-OUTSIDE` against a 69-entry baseline
  that was compared with the **live** `git status --porcelain -uall` (exactly equal + the QA
  artifacts), filesystem route greps (S-24: `Recovery/**` is untracked, never `git grep`), and a
  `RESTORE-SHA256` pin. The pin was verified against the file's real PRE hash **before** any
  mutation (`1E14DA6A…E9E`). Both scripts executed green **and red** (below).

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `backend/tests/Squid.Tests/Recovery/Qa0015RecoveryAcceptanceTests.cs` | xunit (project's own runner) | The 4 scenarios above over real HTTP against `SquidServiceHost`; QA's own JSON walks, own base58 decoder, own RC parser |
| `docs/sdd/qa/qa-check-task-0016.ps1` | Windows PowerShell 5.1 | 7 static + log + touches checks; exit 0 = `SUMMARY total=7 fails=0` |
| `docs/sdd/qa/qa-check-task-0016.sh` | bash / WSL2 | Linux twin: same 7 check ids, same baseline, same exit contract |
| `docs/sdd/_tools/sdd-test.ps1 test [-TestFilter …]` | pwsh + dotnet | The project's runner: baseline, the 8 filtered runs (4 red + 4 green), both gates |

## How to reproduce

Prerequisites: Windows with .NET SDK 10, repo at `C:\dev\squid-squad-net\squid-execution`
(no network needed after restore); the `.sh` twin needs WSL (static/log/touches checks only —
see Not covered).

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Recovery   # expected: Aprovado 11, Com falha 0, log exit: 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0016.ps1                                                     # expected: SUMMARY total=7 fails=0, exit 0
wsl -e bash docs/sdd/qa/qa-check-task-0016.sh                                                                                              # expected: SUMMARY total=7 fails=0, exit 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test                                                      # expected: Aprovado 167, Com falha 0, log exit: 0
```

Mutation proof (S-16) — restore recipe if you repeat it: SHA256
`backend/src/Squid.Api/Recovery/RecoveryEndpoints.cs` (expected PRE `1E14DA6A…E9E`), copy it to
`%TEMP%`, apply the described edit, run the filtered suite (red) and, for M1/M4, the checker
(red), copy the backup back, verify SHA256 PRE == POST, **bump the file's mtime** (a restore
rewinds it behind the dll built from the mutated source — S-23), re-run the suite (green). The
`%TEMP%\qa0015-*.bak` files were removed after all four byte-exact verifications; re-creating
them is the first step above.

## Last run

| Date/Time | Command | Exit | Output (max 10 lines) |
| --- | --- | --- | --- |
| 2026-11-08 00:44 | `powershell … sdd-test.ps1 test` (baseline, AC-05) | 0 | log `20261108-004409` `exit: 0`, `Aprovado: 167, Com falha: 0` |
| 2026-11-08 00:45 | `powershell … sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Recovery` (baseline) | 0 | log `20261108-004513` `exit: 0`, `Aprovado: 11, Com falha: 0` (DEV 7 + QA 4) |
| 2026-11-08 00:45 | `powershell … qa-check-task-0016.ps1` (baseline) | 0 | `SUMMARY total=7 fails=0` / `RESULT: PASS` — gate `20261108-004409`, filter `20261108-004513`, touches `missing=0 out-of-scope=0` |
| 2026-11-08 00:46 | `wsl -e bash docs/sdd/qa/qa-check-task-0016.sh` (baseline) | 0 | `SUMMARY total=7 fails=0` / `RESULT: PASS` (same 7 ids on WSL2) |
| 2026-11-08 00:47 | same filter (**mutation M1** — GET route serves the kit) | 1 | log `20261108-004724` `exit: 1`, `Com falha: 2` — QA `Ac01_…:106` and DEV `The_kit_route…:83`, both `Expected: MethodNotAllowed / Actual: Created` |
| 2026-11-08 00:47 | `powershell … qa-check-task-0016.ps1` (under M1, can-fail) | 1 | `FAIL FILTER-NEWEST-EXIT`, `FAIL FILTER-NEWEST-SUMMARY (Com falha=2)`, `FAIL RESTORE-SHA256 (61816A88…)` / `SUMMARY total=7 fails=3` |
| 2026-11-08 00:48 | restore M1 + `Get-FileHash` + mtime bump (S-16/S-23) | 0 | `POST = PRE = 1E14DA6A…E9E`, `BYTE_EXACT=True`, mtime bumped to 2026-11-08 03:47:58 UTC |
| 2026-11-08 00:49 | same filter (after M1 restore) | 0 | log `20261108-004901` `exit: 0`, `Aprovado: 11, Com falha: 0` |
| 2026-11-08 00:51 | same filter (**mutation M2** — verify does not persist) | 1 | log `20261108-005108` `exit: 1`, `Com falha: 2` — QA `Ac02_…:177` `Expected: ["kit_stale"] / Actual: ["kit_unverified","kit_stale"]` + DEV `The_me_route…` |
| 2026-11-08 00:51 | restore M2 + `Get-FileHash` + mtime bump | 0 | `POST = PRE = 1E14DA6A…E9E`, `BYTE_EXACT=True`, mtime bumped to 2026-11-08 03:51:37 UTC |
| 2026-11-08 00:52 | same filter (after M2 restore) | 0 | log `20261108-005241` `exit: 0`, `Aprovado: 11, Com falha: 0` |
| 2026-11-08 00:54 | same filter (**mutation M3** — `wrappedSigningKey` dropped from the kit) | 1 | log `20261108-005410` `exit: 1`, `Com falha: 6` — QA `Ac01` field set `5 → 4`, QA `Ac03` round-trip `KeyNotFoundException` (`:350` via `:200`), QA `Ac04`, + DEV kit/import tests; **wrapper console printed exit 3 (sandbox signature) while the log says exit 1 — S-22 reproduced live** |
| 2026-11-08 00:54 | restore M3 + `Get-FileHash` + mtime bump | 0 | `POST = PRE = 1E14DA6A…E9E`, `BYTE_EXACT=True`, mtime bumped to 2026-11-08 03:54:49 UTC |
| 2026-11-08 00:55 | same filter (after M3 restore) | 0 | log `20261108-005552` `exit: 0`, `Aprovado: 11, Com falha: 0` |
| 2026-11-08 00:57 | same filter (**mutation M4** — wrong-key path writes a row) | 1 | log `20261108-005738` `exit: 1`, `Com falha: 2` — QA `Ac04:296` `Expected "1eeda497…" / Actual "39e6678e…"` (the state-hash assertion) + DEV `A_wrong_password…` same shape |
| 2026-11-08 00:57 | `wsl -e bash docs/sdd/qa/qa-check-task-0016.sh` (under M4, can-fail) | 1 | `FAIL FILTER-NEWEST-EXIT`, `FAIL FILTER-NEWEST-SUMMARY`, `FAIL RESTORE-SHA256 (6D6AF0C0…)` / `SUMMARY total=7 fails=3` |
| 2026-11-08 00:58 | restore M4 + `Get-FileHash` + mtime bump | 0 | `POST = PRE = 1E14DA6A…E9E`, `BYTE_EXACT=True`, mtime bumped to 2026-11-08 03:58:15 UTC |
| 2026-11-08 01:00 | same filter (after M4 restore) | 0 | log `20261108-010007` `exit: 0`, `Aprovado: 11, Com falha: 0` |
| 2026-11-08 01:01 | `powershell … sdd-test.ps1 test` (**final gate, AC-05**) | 0 | log `20261108-010157` `exit: 0`, `Aprovado: 167, Com falha: 0, Total: 167, Duração: 1 m 25 s` |
| 2026-11-08 01:03 | `grep QA-MUTATION` over `backend/` + `git status --porcelain -uall` + backup cleanup | 0 | 0 mutation markers; 72 entries = 69-entry baseline + 3 QA artifacts (this doc not yet written); `%TEMP%\qa0015-*.bak` = 0 left |
| 2026-11-08 01:11 | `powershell … qa-check-task-0016.ps1` (final, this doc present) | 0 | `SUMMARY total=7 fails=0` / `RESULT: PASS` — gate `20261108-010157` exit 0 167/167, filter `20261108-010007` exit 0 11/11, touches `missing=0 out-of-scope=0 new-allowed=4/4` |
| 2026-11-08 01:11 | `wsl -e bash docs/sdd/qa/qa-check-task-0016.sh` (final) | 0 | `SUMMARY total=7 fails=0` / `RESULT: PASS` (same 7 ids on WSL2, `new-allowed=4/4`) |

Rule S-22 respected: every classification above comes from the log's `exit:` header and its
summary line. In the M3 run the console and the log disagreed **on purpose** — the wrapper's
signature matcher saw the bare substring `handle` (parameter name `String handle` inside
`ExportWithOriginAsync`'s failing stack frame) while its pt-BR summary regex does not know
`Com falha!`, so it printed `test: exit 1 with the sandbox signature` and exited **3
(environment)** for a real red; the log `20261108-005410-test-filter.log` says `exit: 1` with
`Com falha: 6`, which is the truth used here.

## Mutation proof (S-16)

| # | Mutation (product path, inside the task's touches) | Can-fail evidence | Restore proof |
| --- | --- | --- | --- |
| M1 (AC-01) | `backend/src/Squid.Api/Recovery/RecoveryEndpoints.cs:34` — `app.MapGet("/api/recovery/kit", KitAsync)` added: a read route now hands the kit back | xunit red: log `20261108-004724` `exit: 1`, `Com falha: 2` — QA `Ac01…:106` + DEV `The_kit_route…:83`, both `Expected: MethodNotAllowed / Actual: Created`; checker red: `SUMMARY total=7 fails=3`, exit 1 | SHA256 PRE = POST = `1E14DA6A…E9E`, `BYTE_EXACT=True`; mtime bumped to 03:47:58 UTC (after the mutated dll); filtered suite green 11/11 (`20261108-004901`) |
| M2 (AC-02) | same file, `VerifyAsync` — `await recovery.MarkVerifiedAsync(…)` removed: verify answers 200 but never persists | xunit red: log `20261108-005108` `exit: 1`, `Com falha: 2` — QA `Ac02…:177` `Expected: ["kit_stale"] / Actual: ["kit_unverified","kit_stale"]` + DEV `The_me_route…` | SHA256 PRE = POST = `1E14DA6A…E9E`, `BYTE_EXACT=True`; mtime bumped to 03:51:37 UTC; filtered suite green 11/11 (`20261108-005241`) |
| M3 (AC-03) | same file, `KitAsync` — `wrappedSigningKey` dropped from the response: the kit no longer carries the RSA | xunit red: log `20261108-005410` `exit: 1`, `Com falha: 6` — the AC-03 round-trip itself red (`KeyNotFoundException` reading `wrappedSigningKey` in `ExportWithOriginAsync:350` → `Ac03:200`) plus QA `Ac01` (`Expected 5 fields / Actual 4`), QA `Ac04`, and DEV's kit + AC-03 + AC-04 tests; clean 5 remaining | SHA256 PRE = POST = `1E14DA6A…E9E`, `BYTE_EXACT=True`; mtime bumped to 03:54:49 UTC; filtered suite green 11/11 (`20261108-005552`) |
| M4 (AC-04) | same file, `ImportAsync` wrong-password catch — `await recovery.MarkVerifiedAsync(handle, …)` inserted before the typed refusal: still 401, but the row is written | xunit red: log `20261108-005738` `exit: 1`, `Com falha: 2` — QA `Ac04:296` **fails on the before/after state hash** (`Expected "1eeda497…" / Actual "39e6678e…"`) while its 401/`recovery_failed` assertions passed, + DEV `A_wrong_password…` the same shape; checker (sh) red: `SUMMARY total=7 fails=3`, exit 1 | SHA256 PRE = POST = `1E14DA6A…E9E`, `BYTE_EXACT=True`; mtime bumped to 03:58:15 UTC; filtered suite green 11/11 (`20261108-010007`) |

After all four restores: full gate `exit: 0` with 167/167 and both checkers green — the restored
bytes are proven by actual green runs, not only by hash. Mutations touched **only**
`backend/src/Squid.Api/Recovery/RecoveryEndpoints.cs`; `Auth/AuthEndpoints.cs`,
`SqliteRecoveryStore.cs`, the test files, `Crypto/`, `Discovery/`, `Grains/Actors/`, other QA
docs and the task control block were never written by this run (outside its own QA doc +
checkers + test file), and `grep QA-MUTATION backend/` returns 0 after the last restore.

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| 1 | MINOR (harness, pre-existing — S-22, reproduced live again) | `docs/sdd/_tools/sdd-test.ps1:79-82` | On a real red whose output contains the bare substring `handle` (here: `ExportWithOriginAsync(String handle)` in M3's failing stack frames) and whose pt-BR summary starts with `Com falha!`, the wrapper's sandbox-signature matcher fires and its summary regex misses, so the **console** reports `exit 3` ("environment, not a failing test") for a real product-test red. Proven again this attempt: console exit 3 vs log `exit: 1` / `Com falha: 6` | `20261108-005410-test-filter.log` header+summary vs wrapper `WRAPPER_EXIT=3` |
| 2 | INFO (inherited provenance — not a product defect) | `docs/sdd/runs/test-runs/20261108-001551-test-filter.log` (line 30-37) | The dead QA agent's first run was red on **its own** `PemBody` helper (`FormatException` parsing the profile PEM) — its fix (strip the exact `-----BEGIN PUBLIC KEY-----`/`END` markers + whitespace) was verified legitimate against the product's marker (`ProfileJsonLd.cs:20`) and the surrounding assertions (origin==restored PEM string equality, RSA/Ed25519 signature verification) were not weakened | `001551` `exit: 1` vs `001758` `exit: 0`; `ProfileJsonLd.cs:20`; this attempt's own 11/11 + 167/167 runs |
| 3 | MINOR (data-model §2.12 field vs implementation — OUT of this task's touches, pre-existing from TASK-0014 — memory-bank proposal) | `specs/001-squid-mvp/data-model.md:272` vs `backend/src/Squid.Api/Auth/AuthAccount.cs:13` and `AuthEndpoints.cs:98` | data-model §2.12 lists `RecoveryCredentialHash` for `RecoveryKit`; the implementation persists the credential **in clear** (`KitRecord.RecoveryCode` = the very bytes `RecoveryCredential.Format` renders) so the kit route can re-derive the identical mnemonic on re-export. `RecoveryCredentialHash` appears nowhere in the repo but the data-model itself — the deviation was never adjudicated. Impact bounded: the RK still requires the Argon2id password, no route leaks the credential (AC-01 green), and the flow has already accepted the re-export/idempotence behaviour that clear storage enables (`RecoveryImportTests:84`) — so either the data-model row or the storage needs an ODEV decision at phase close. Not an AC of TASK-0016 → not a red here, never fixed here | `grep RecoveryCredentialHash` → 1 hit (the data-model line); `AuthEndpoints.cs:98` |

Out-of-scope interference: none. No red ever originated in `Auth/`, `Crypto/`,
`Grains/Actors/`, `Discovery/` or another task's suite — the final gate's 167/167 includes the
peer TASK-0012..0015 suites green, and this run's writes were exactly its own artifacts plus the
four (restored) mutations in `Recovery/RecoveryEndpoints.cs`.

## Not covered, and why

- **Linux `dotnet test` of the suite** — the only WSL distro has no .NET SDK and installing one
  needs a gate approval that was not granted; `sdd-test.sh` also cannot run here (finds `*.sln`,
  the solution is `*.slnx` → exit 3, memory bank `build-run-test.md:42`) → **unverified**, never
  assumed equal to the Windows result. The checker's `.sh` twin **did** execute on Linux (WSL2:
  green 7/7 twice, red `fails=3` under M4) — the static/log/touches half is two-platform; the
  xunit scenarios are Windows-this-attempt, with CI (ubuntu, same `sdd-test.ps1` via pwsh) as the
  Linux executor of the suite.
- **Signature through a product HTTP endpoint after import** — no sign route exists in P-02
  (GAP-09: runtime unlock of the RK is a P-06 decision); AC-03's "assina/verifica com as chaves
  antigas" is proven in-process against the very keys the restored instance publishes over HTTP.
  Risk: a defect in a *future* signing endpoint would not be caught by this task's tests.
- **Strict one-time issuance of the mnemonic across repeated owner exports** — the task's AC-01
  defines "exactly once" as *no other route recovers it* (proven), and the design re-derives the
  same words from the stored RC for the session owner (proven identical by DEV's re-export at
  `RecoveryImportTests:84`); if ODEV wants strictly-one-time issuance per FR-005's wording, that
  is a contract decision, not a red of this run.
- **Performance (< 500 ms) and federation** — SPEC-0007 non-functional / later phases; not ACs
  of this task.
