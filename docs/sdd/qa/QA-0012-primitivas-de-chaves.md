<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: CONCLUIDO
- owner: qa
- lastVerified: 2026-11-07 18:00:31

# QA-0012 — Primitivas de chaves: RK/RC, HKDF, Argon2id, embrulho RSA

- task: TASK-0012
- spec: SPEC-0007
- runId: R-20261107-02
- qaAttempt: 1/2
- verdict: PASSED

Independent verification of the 5 ACs of TASK-0012 against the acceptance list, plus the D-01
architecture facet (DomainHasNoInfrastructureTests + ADR-25). Every scenario below was executed
in this session; two product mutations prove the tests can fail; both mutations were restored
byte-exact (S-16). The xunit scenarios are DEV's `KeyPrimitivesTests` (existing tests first, per
ponytail-qa) — QA executed them, proved they can fail, and added the static checkers
`qa-check-task-0012.ps1` / `.sh` for the invariants xunit cannot see.

## What is proven

| AC | Scenario (executed) | Script | Result |
| --- | --- | --- | --- |
| AC-01 | RK = 32 bytes from the platform CSPRNG and two RKs differ (static: `RandomNumberGenerator.Fill`; runtime `Ac01_RkIs32CspRngBytesAndNeverPrintsItself`); `ToString` masked, 0 logging APIs in the module, 0 consumers of `Squid.Api.Crypto` outside it (no response path exists); same RK → same Ed25519 twice, other RK → other (`Ac01_SameRkDerivesSameEd25519AndOtherRkDerivesAnother`); RC ⊥ RK: two `KeyState.Create` on the same RK → equal Ed25519, different RC (`Ac01_RecoveryCodeIsNotAFunctionOfTheRk`) — **mutation M2 (RC = HKDF(RK)) turns this red at :50** | filtered xunit + `qa-check-task-0012.ps1` AC01-* + M2 | PASS |
| AC-02 | RSA-2048 wrap round-trip: unwrapped key == exported key; wrong RK throws typed `CryptographicException` without key material (`Ac02_RsaWrapRoundTripsAndWrongRkFailsAsTypedException`) — **mutation M1 (box no longer keyed) turns it red at :64 `Assert.ThrowsAny() Failure: No exception was thrown`**; static: 0 `catch`, 0 `ex.ToString` in the new dirs, `new AesGcm` present in Seal + Open | filtered xunit + `qa-check-task-0012.ps1` AC02-* + M1 | PASS |
| AC-03 | Argon2id round-trip: decrypt returns the original 32 bytes; wrong password throws and the blob is byte-identical after the failed attempt (`Ac03_Argon2idRoundTripsAndWrongPasswordDestroysNothing`) — M1 also turns the wrong-password assertion red at :78; static: `Argon2id` marker + cost pinned (4096 KiB / 3 / 1) | filtered xunit + `qa-check-task-0012.ps1` AC03-* + M1 | PASS |
| AC-04 | `JsonSerializer.Serialize(KeyState)` and `state.ToString()` contain neither the RK nor the RSA private key in clear (base64 nor hex), while still serializing `EncryptedRootKey` (`Ac04_SerializedStateCarriesNeitherRkNorPrivateKeyInClear`); static: no clear `RootKey` member on `KeyState` | filtered xunit + `qa-check-task-0012.ps1` AC04-* | PASS |
| AC-05 | Gate `sdd-test.ps1 test` → log `exit: 0`, `Aprovado! – Com falha: 0, Aprovado: 119, Total: 119` (log `20261107-175625-test-gate.log`); ADR-25: `Version="` attributes in the 10 csproj = 0, `PackageVersion` outside the props = 0, exactly 1 `BouncyCastle.Cryptography Version="2.7.0"` at `Directory.Packages.props:38`, exactly 1 PackageReference without `Version=` in `Squid.Api.csproj`; touches: 0 product files outside the 4 touches (peer TASK-0013 paths listed, never modified) | full gate + `qa-check-task-0012.ps1/sh` AC05-* | PASS |
| D-01 facet (AC-05) | `DomainHasNoInfrastructureTests` → log `exit: 0`, `Aprovado 3, Com falha 0` (`20261107-173542-test-filter.log`); Domain csproj untouched (`git status` has no `backend/src/Squid.Domain` entry) | filtered xunit | PASS |

Baseline for comparison: filtered Crypto run before anything else → `exit: 0`, `Aprovado 6, Total 6`
(`20261107-173517-test-filter.log`).

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `docs/sdd/qa/qa-check-task-0012.ps1` | pwsh / Windows PowerShell 5.1 | 16 static checks (AC-01 source invariants, no catch/ex.ToString, AEAD box real, Argon2id + pinned cost, no clear member, ADR-25 placement, touches classification). Exit 0 = all green. |
| `docs/sdd/qa/qa-check-task-0012.sh` | bash / WSL2 (Linux 6.18.54.1) | Linux twin: same check ids, same exit contract. |
| `backend/tests/Squid.Tests/Crypto/KeyPrimitivesTests.cs` | xunit (existing, DEV) | The 6 runtime scenarios; QA executed all of them and mutation-proved each red path. |
| `docs/sdd/_tools/sdd-test.ps1 test [-TestFilter …]` | pwsh + dotnet | The project's runner: filtered suite, architecture test, full gate. |

## How to reproduce

Prerequisites: Windows with .NET SDK 10, repo at `C:\dev\squid-squad-net\squid-execution` (no network needed after restore).

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0012.ps1   # expected: SUMMARY total=16 fails=0, exit 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Crypto   # expected: Aprovado 6, Com falha 0, exit 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test     # expected: Aprovado 119, Com falha 0, exit 0
wsl -e bash docs/sdd/qa/qa-check-task-0012.sh                                             # expected: SUMMARY total=16 fails=0, exit 0 (Linux static half)
```

Mutation proof (S-16) — restore recipe if you repeat it: SHA256 the file, apply the described
edit, run the filtered suite (red), copy the backup back, verify SHA256 PRE == POST, **bump the
file's mtime** (see finding 2), re-run the suite (green).

## Last run

| Date/Time | Command | Exit | Output (max 10 lines) |
| --- | --- | --- | --- |
| 2026-11-07 17:35:17 | `sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Crypto` | 0 | `Aprovado! – Com falha: 0, Aprovado: 6, Total: 6` (log header `exit: 0`) |
| 2026-11-07 17:35:42 | `sdd-test.ps1 test -TestFilter FullyQualifiedName~DomainHasNoInfrastructureTests` | 0 | `Aprovado! – Com falha: 0, Aprovado: 3, Total: 3` (log header `exit: 0`) |
| 2026-11-07 17:46 | `powershell … qa-check-task-0012.ps1` (first run) | 1 | `FAIL AC02-aead-box … hits = 0` — QA's own script bug (file passed where a dir was expected); fixed, re-run green |
| 2026-11-07 17:47 | `powershell … qa-check-task-0012.ps1` (after fix) | 0 | `SUMMARY total=16 fails=0 / RESULT: PASS` |
| 2026-11-07 17:48 | `wsl -e bash docs/sdd/qa/qa-check-task-0012.sh` | 0 | `SUMMARY total=16 fails=0 / RESULT: PASS` on `Linux 6.18.54.1-microsoft-standard-WSL2 x86_64` |
| 2026-11-07 17:49:12 | `sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Crypto` (**under mutation M1**) | 1 | log `exit: 1`; `Com falha: 2, Aprovado: 4` — `Ac02… [FAIL]` :64 `Assert.ThrowsAny() Failure: No exception was thrown`, `Ac03… [FAIL]` :78 |
| 2026-11-07 17:51:08 | `sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Crypto` (**under mutation M2**) | 1 | log `exit: 1`; `Com falha: 1, Aprovado: 5` — `Ac01_RecoveryCodeIsNotAFunctionOfTheRk [FAIL]` :50 |
| 2026-11-07 17:53:35 | `sdd-test.ps1 test` (gate, first attempt after restore) | 1 | log `exit: 1`; `Com falha: 1, Aprovado: 118, Total: 119` — `Ac01_RecoveryCode…` :50 **while source SHA256 already == PRE** → stale incremental build (finding 2) |
| 2026-11-07 17:56:25 | `sdd-test.ps1 test` (gate after mtime bump, content unchanged) | 0 | log `exit: 0`; `Aprovado! – Com falha: 0, Aprovado: 119, Total: 119` |

Rule S-22 respected: every classification above comes from the log's `exit:` header and summary
line, not from the wrapper's console. Console/log pairs agreed in all 6 runs of this attempt
(0/0, 0/0, 1/1, 1/1, 1/1, 0/0) — the S-22 misclassification did not trigger in QA-0012's runs.

## Mutation proof (S-16)

| # | Mutation (product path) | Can-fail evidence | Restore proof |
| --- | --- | --- | --- |
| M1 | `backend/src/Squid.Api/Crypto/AesGcmBox.cs` — `Seal` stores the plaintext unkeyed and `Open` ignores key+tag (breaks the wrong-key failure path of AC-02 and AC-03) | xunit red: `Ac02…` :64 + `Ac03…` :78, log `20261107-174912-test-filter.log` `exit: 1`, `Com falha: 2`; checker red: `FAIL AC02-aead-box`, exit 1 | SHA256 PRE = POST = `3ED0A774CEDED54773B145D29AC825EB70D6D7F2CD839C528127772BA1596948`, `BYTE_EXACT=True`; residue scan 0; gate green after rebuild |
| M2 | `backend/src/Squid.Api/Crypto/KeyState.cs` — `RecoveryCode = HKDF.SHA256(RK)` (RC now derives from RK, breaks AC-01 independence) | xunit red: `Ac01_RecoveryCodeIsNotAFunctionOfTheRk` :50, log `20261107-175108-test-filter.log` `exit: 1`, `Com falha: 1`; checker red: `FAIL AC01-rc-csprng-src`, exit 1 | SHA256 PRE = POST = `C2690BA35B9BEE5CA87D4D639E9428F4DE00377C08D4893D0AC89DF1EDA17700`, `BYTE_EXACT=True`; residue scan 0; gate green after rebuild |

After both restores: `qa-check-task-0012.ps1` exit 0 (16/16), `.sh` exit 0 (16/16), full gate
`exit: 0` with 119/119 — the restored bytes are proven by an actual green run, not only by hash.
Mutations were applied only inside the task's own product paths; no file of TASK-0013
(`backend/src/Squid.Grains/Actors/**`, `backend/tests/Squid.Tests/Actors/**`) was read-modified or
reverted by QA-0012 at any moment.

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| 1 | MINOR | `docs/sdd/tasks/TASK-0012-primitivas-de-chaves.md:140` (DEV evidence row) | The evidence command `git grep -n -e catch -- backend/src/Squid.Api/Crypto backend/tests/Squid.Tests/Crypto` is vacuous: those directories are untracked, and `git grep` silently skips untracked files — it exits 1 for ANY pattern. Probe: `git grep -n RootKey -- backend/src/Squid.Api/Crypto` → exit 1 while a direct content scan finds 28 hits. The conclusion (0 catch) is TRUE — QA re-verified it with a direct scan (`AC02-no-catch`, both platforms) — but the recorded command does not prove it. | probe in this session; `qa-check-task-0012.ps1` `AC02-no-catch` |
| 2 | MINOR | `docs/sdd/runs/test-runs/20261107-175335-test-gate.log:1` | Gate red on an already-restored tree: byte-exact restore via file copy rewinds `LastWriteTime` (KeyState.cs 17:13:46) behind the dll built from the mutated source (Squid.Api.dll 17:51:04), so `dotnet test` incremental build skipped recompilation and ran the stale mutated binaries (`Ac01_RecoveryCode…` failed with source SHA256 == PRE). Not a product defect; operational companion of S-16: after restore, bump mtime (content hash unchanged) and re-run the gate. Fixed run: `20261107-175625-test-gate.log` exit 0, 119/119. | mtime/hash table in this session; both gate logs |
| 3 | MINOR (observation) | `backend/tests/Squid.Tests.csproj:4` (+ `Squid.Persistence.csproj:10`, `Squid.Service.csproj:14`) | AC-05's literal substring `Version=` occurs 3× in csproj files — only inside XML comments that warn against it (`no Version= here.`). The ADR-25 behaviour (no version declaration outside the props) holds: attribute scan `Version\s*=\s*"` = 0 across the 10 csproj. Recorded so the literal and the intended reading of AC-05 are both on file. | `Select-String -Pattern 'Version\s*=\s*"'` → 0 hits |

Out-of-scope interference: none observed. The final gate (119 tests) includes the peer TASK-0013
suite (15 tests) green; no failure originated in `Grains/Actors` or `Tests/Actors` during any of
QA-0012's 6 runs, and QA-0012 never wrote there.

## Not covered, and why

- **Linux build/test path (`dotnet test` under Linux)** — the only WSL distro
  (`podman-machine-default`) has no .NET SDK and installing one requires a gate approval that was
  not granted — `unverified`, never assumed equal to the Windows result. The static half *did*
  run on Linux (`.sh` twin, 16/16, WSL2). Risk: a Linux-only compile/test divergence would not be
  caught by this attempt.
- **Dynamic "RK never in an HTTP response"** — no response surface exists in this task (0 files
  under `backend/src` outside the module reference `Squid.Api.Crypto`; GAP-09). Proven
  structurally (executed scan, both platforms), not by an HTTP test; the runtime assertion
  belongs to TASK-0014 when registration returns state. Risk today: none reachable; risk if
  TASK-0014 serializes `RootKey` directly: its own QA must assert it.
- **Statistical randomness of RK** — only the CSPRNG call, length and distinctness are proven;
  no entropy test. Risk: negligible (platform `RandomNumberGenerator`).
- **`RecoveryCode` in clear inside the serialized state** — by design (it is the user-held
  recovery code); AC-04 covers RK and private key only, per the AC text.
