<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: VERDE
- owner: qa
- lastVerified: 2026-11-06 18:12

# QA-0006 — TASK-0007 ContainerPolicy, IPolicyEvaluator e testes da matriz no Domain

- task: TASK-0007
- spec: SPEC-0003-domain-container-policy
- runId: R-20261106-01
- qaAttempt: 1/2
- verdict: PARTIAL

## What is proven

Tested only against the `## Acceptance criteria` list AC-01..AC-06 of the task file. Everything
below was executed in this session on `C:\dev\squid-squad-net\squid-execution` (Windows,
PowerShell 5.1, SDK 10.0.403). DEV's claims were re-run, not trusted. No stagingRoot: everything
was written and run directly in the repo. Every green below was first made to fail — the
three-mutation battery (product code, all restored byte-exact) is what makes the green runs mean
something. Expected values for the matrix were derived from the source documents (data-model
§3.2/§2.4, doc 05 §2.2/§2.3, FR-012/FR-014), not remembered.

| AC | Scenario | Command | Exit | Result |
| --- | --- | --- | --- | --- |
| AC-01 | `ContainerPolicy` immutable record with `Posting`/`Visibility`/`Join`/`Interaction`/`PublisherIds`/`Version`, no setter on any property, `EqualityContract` present; `Group()` = membersOnly+invite and `Community()` = public+open\|request, both posting `membersOnly` | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.Domain"` (DEV tests executed by QA) + `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0007.ps1` | 0 and 0 | PASS — `ContainerPolicyTests.PolicyIsAnImmutableRecordCarryingEveryField`, `GroupPresetFollowsDoc05Defaults`, `CommunityPresetFollowsDoc05Defaults` green in `Aprovado 55 Com falha 0 Total 55` (log `20261106-175844-test-filter.log`); static checks `AC-01 field <name> exists` ×6 + both presets green (26 PASS, exit 0). **Sub-clause "não listado/listada" (discovery): no code path in this task — see F1** |
| AC-02 | `SinglePublisher` with empty `PublisherIds` throws at construction; null list throws; a populated list builds | same filter, plus executed break-it proof (mutation M1: the constructor guard deleted) | 0 (1 under M1) | PASS — `ContainerPolicyTests.SinglePublisherWithoutPublisherIdsIsRejectedAtConstruction` green baseline; **M1 red: exit 1, exactly 1 failure** at `ContainerPolicyTests.cs:72` (`Assert.Throws` got no exception) in log `20261106-175956-test-filter.log` — removing the guard is exactly what breaks this test; restored byte-exact (SHA256 below) |
| AC-03 | `Evaluate(action, role, policy)` returns `PolicyDecision` with permitted, non-empty reason and the evaluated `PolicyVersion`; invalid input throws instead of deciding silently | same filter | 0 (7 under M2) | PASS — `DecisionCarriesPermissionReasonAndPolicyVersion` and `InvalidEvaluationInputThrowsInsteadOfDeciding` green baseline; under M2 the reason/permitted assertions also failed (1 of the 7), proving the test reads both fields, not just one |
| AC-04 | The full 5 roles × 8 actions = 40 cells asserted against the strict hierarchy; Guest denied on every write even under a permissive policy | same filter + `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0007.ps1` (independent oracle) + executed break-it proofs (M2, M3) | 0, 0 (1 under M3, 7 under M2) | PASS — 40 `InlineData` cells, 40 unique, all 5 roles and all 8 actions present, matrix runs under `Permissive()` (5 `Everyone` gates + `Join=Open` proved from source), **0 mismatches vs the oracle**; **M3 red: exit 1, exactly the `Member/Moderate` cell** (`expected: False`, `PolicyEvaluatorTests.cs:85`) in log `20261106-180213-test-filter.log`; **M2 red: all 5 Guest write cells** failed (Post/Comment/React/Share/Attach `expected: False`) in log `20261106-180054-test-filter.log` |
| AC-05 | Guest with `Posting=Everyone` stays denied for `Post` — a permissive policy grants nothing the role does not hold | same filter + mutation M2 | 0 (1 under M2) | PASS — `GuestStaysDeniedWhenPostingIsEveryone` green baseline; **M2 (Guest branch flipped to Allow) red**: that test failed at `PolicyEvaluatorTests.cs:94` alongside the 5 Guest matrix cells (log `20261106-180054-test-filter.log`) — only the Guest deny branch stands between the AC and a green suite |
| AC-06 | `Squid.Domain` has no Orleans/JSON-LD/I/O reference and the full gate exits 0 with the new tests counted | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (run 3× by QA) + `-TestFilter "Category=Architecture"` + static scan | 0, 0, 0 | PASS — gate `Aprovado 97 Total 97` (42 pre-existing from DEV's baseline log `20261106-173120` exit 0 + 55 new = 97), QA's re-run log `20261106-175244-test-gate.log`, final post-restore log `20261106-180400-test-gate.log`; architecture filter `Aprovado 17 Total 17` (log `20261106-180612-test-filter.log`); static scan: 2 product `.cs` files, 0 forbidden hits (Orleans/JSON-LD/Newtonsoft/System.IO/HttpClient/File/Stream/Directory/Path), csproj 0 `PackageReference`/`ProjectReference` |

### AC-04 matrix — expected values checked against the source documents, not only counted

The oracle used by `qa-check-task-0007.ps1` was written from the documents first, then compared to
the 40 asserted cells (0 mismatches). Citations:

- content (post/comment/react/share/attach): the matrix policy is `Everyone` ⇒ gate needs
  `Member` rank, **and** data-model §2.4 + doc 05 §2.2 say `Guest` is read-only whatever the
  policy says → Guest ✗, everyone else ✓ (FR-012 strict inheritance: Owner ⊃ Admin ⊃ Moderator ⊃ Member ⊃ Guest);
- `Join`: `Join=Open` admits entry for all roles — entering is not a write (FR-014; SPEC-0003 D-02);
- `Moderate`: rank ≥ `Moderator` (data-model §3.2: Moderator removes content, silences, approves entry);
- `Admin`: rank ≥ `Admin` (data-model §3.2: Admin manages roles, policy, content, ban).

| role \ action | Post | Comment | React | Share | Attach | Join | Moderate | Admin |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Owner | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Admin | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Moderator | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ |
| Member | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ | ✗ |
| Guest | ✗ | ✗ | ✗ | ✗ | ✗ | ✓ | ✗ | ✗ |

Spot-check against doc 05 §2.2 roles table (line 102-108): Owner "Tudo" ✓; Admin "gerenciar
papéis, configurar política, remover conteúdo, banir" → Admin ✓ and Moderate ✓ (inherits);
Moderator "remover conteúdo, silenciar, aprovar entrada" → Moderate ✓, Admin ✗; Member
"participar conforme a política (postar, comentar, reagir)" → content ✓ under a permissive
policy, Moderate ✗, Admin ✗; Guest "somente leitura" → all writes ✗. Every cell of the table
above agrees with both data-model §3.2 and doc 05 §2.2.

### GAP-12 — enum names vs data-model §2.3 (executed, not remembered)

The check parses the expected lists **out of** `specs/001-squid-mvp/data-model.md` §2.3 and the
declared lists out of `ContainerPolicy.cs`, then compares them in order:

| Enum | data-model §2.3 (expected) | `ContainerPolicy.cs` (actual) | Match |
| --- | --- | --- | --- |
| `PostingPolicy` | Everyone, Members, Moderators, Admins, SinglePublisher | same | yes |
| `VisibilityPolicy` | Public, Unlisted, MembersOnly, Private | same | yes |
| `JoinPolicy` | Open, Request, Invite, Closed | same | yes |

Doc 05 §2.4's lowercase table (`everyone`, `singlePublisher`, …) is **not** used — GAP-12 says the
data model prevails, and the code follows it. GAP-11 also verified: `Evaluate(PolicyAction,
ContainerRole, ContainerPolicy)` — no `Actor`-typed parameter anywhere in `Squid.Domain`.

### Mutation battery — every green above was first made to fail

| # | Broken on purpose (product code, restored after) | Suite exit | What failed | Restore proof |
| --- | --- | --- | --- | --- |
| M1 | `ContainerPolicy` constructor: the `SinglePublisher && publisherIds.Count == 0 → throw` guard deleted (AC-02) | 1 | 1 failure: `ContainerPolicyTests.SinglePublisherWithoutPublisherIdsIsRejectedAtConstruction` at `ContainerPolicyTests.cs:72` (log `20261106-175956-test-filter.log`) | SHA256 `C6521ACF5D80AD2114AE4C9179F6BB8C6EE8EDCBA65A028694C39041E98BAEF8` byte-exact (captured before the mutation) |
| M2 | `PolicyEvaluator.Content`: the Guest deny branch flipped to `Allow` — "Guest passes on write" (AC-05 + AC-04 Guest cells) | 1 | 7 failures: 5 Guest matrix cells (`Post/Comment/React/Share/Attach, expected: False`), `GuestStaysDeniedWhenPostingIsEveryone` (`PolicyEvaluatorTests.cs:94`, AC-05), `DecisionCarriesPermissionReasonAndPolicyVersion` (log `20261106-180054-test-filter.log`) | SHA256 `94CDDFA4DC578B2B6655A702136177A2BDB68F25045318F8AC73A2517D6D6DCF` byte-exact (captured before the mutation) |
| M3 | `PolicyAction.Moderate`: required role weakened `Moderator → Member` (the hierarchy itself, AC-04 negative cell) | 1 | 1 failure: exactly `CapabilityMatrixMatchesStrictHierarchy(role: Member, action: Moderate, expected: False)` at `PolicyEvaluatorTests.cs:85` (log `20261106-180213-test-filter.log`) | SHA256 `94CDDFA4…` byte-exact (same file as M2) |

After the three restores: both files hash exactly to their pre-mutation SHA256 (captured **before**
any mutation — the S-16 trap from QA-0005), the marker scan reports `MARCADORES DE MUTACAO:
NENHUM` (0 hits for `QA-MUT` in `backend/src/Squid.Domain/*.cs`), and the final full gate is
97/97 exit 0.

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `backend/tests/Squid.Tests/Domain/ContainerPolicyTests.cs` | xunit via `sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Domain` | DEV's AC-01/AC-02: record shape, presets, SinglePublisher rejection, enum-domain rejection, version increment |
| `backend/tests/Squid.Tests/Domain/PolicyEvaluatorTests.cs` | xunit (same runner) | DEV's AC-03/AC-04/AC-05: decision fields, the 40-cell matrix, Guest-stays-denied, SinglePublisher fail-closed, join policies, per-gate interaction, invalid input |
| `docs/sdd/qa/qa-check-task-0007.ps1` | PowerShell 5.1 (`powershell -NoProfile -ExecutionPolicy Bypass -File …`) | **QA, new**: GAP-12 enums parsed from data-model §2.3 vs source; GAP-11 signature; AC-04 40 cells vs an independent hierarchy oracle; AC-06 static scan (sources + csproj); AC-01/AC-02 static shape; gate logs exit 0 with 97/97 (both QA's and DEV's claimed log) |
| `docs/sdd/qa/qa-check-task-0007.sh` | bash under WSL (`wsl -e bash …`) | Linux twin of the same static checks (25 PASS, exit 0); explicitly does not execute the xunit suite — no .NET SDK in WSL |

## How to reproduce

Prerequisites: Windows, .NET SDK 10.0.403 (WSL optional for the static twin only).

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test
# expected: exit 0 - "Aprovado! - Com falha: 0, Aprovado: 97, Ignorado: 0, Total: 97"
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.Domain"
# expected: exit 0 - "Aprovado: 55 ... Total: 55" (42 pre-existing + 55 new = 97 in the gate)
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0007.ps1
# expected: exit 0 - "RESULT: PASS - all checks green" (26 checks)
wsl -e bash /mnt/c/dev/squid-squad-net/squid-execution/docs/sdd/qa/qa-check-task-0007.sh
# expected: exit 0 - "RESULT: PASS - all checks green" (25 checks)
```

To see them fail on purpose (the proof they are not decoration): delete the
`SinglePublisher` guard in `ContainerPolicy.cs:94` (expect AC-02 red, 1 failure), or change the
Guest branch in `IPolicyEvaluator.cs:82` to `Allow` (expect 7 failures: AC-05 + 5 matrix cells +
AC-03), or weaken `IPolicyEvaluator.cs:69` `ContainerRole.Moderator → ContainerRole.Member`
(expect the `Member/Moderate` cell red); rerun the filter command, expect exit 1 with the
assertions quoted in the battery above; restore afterwards — the SHA256 of `ContainerPolicy.cs` /
`IPolicyEvaluator.cs` before and after this run was `C6521ACF…` / `94CDDFA4…` (byte-exact,
mutation markers 0).

## Last run

| Time (from the run log's own filename/header) | Command | Exit | Output (tail) |
| --- | --- | --- | --- |
| 2026-11-06 17:52:44 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` | 0 | `Aprovado! – Com falha: 0, Aprovado: 97, Ignorado: 0, Total: 97, Duração: 41 s` (log `docs/sdd/runs/test-runs/20261106-175244-test-gate.log`) — QA re-run of DEV's claim, 42+55 |
| 2026-11-06 17:58:44 | `… sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.Domain"` | 0 | `Aprovado: 55 … Total: 55, Duração: 138 ms` (log `20261106-175844-test-filter.log`) — the 55 new tests |
| 2026-11-06 17:59 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0007.ps1` (first run) | 1 | 1 false positive: the scan hit `obj/…/GlobalUsings.g.cs` (SDK implicit using `System.IO`); scope narrowed to product sources, no product issue — see note below |
| 2026-11-06 18:00 | `… qa-check-task-0007.ps1` (corrected scope) | 0 | `RESULT: PASS - all checks green` (26 checks) |
| 2026-11-06 17:59:56 | filter run under mutation M1 (SinglePublisher guard removed) | 1 | `1 … SinglePublisherWithoutPublisherIdsIsRejectedAtConstruction [FAIL]` at `ContainerPolicyTests.cs:72` (log `20261106-175956-test-filter.log`) |
| 2026-11-06 18:00:54 | filter run under mutation M2 (Guest passes on write) | 1 | `Com falha: 7 … GuestStaysDeniedWhenPostingIsEveryone` + 5 Guest cells + `DecisionCarries…` (log `20261106-180054-test-filter.log`) |
| 2026-11-06 18:02:13 | filter run under mutation M3 (Moderate weakened to Member) | 1 | `1 … CapabilityMatrixMatchesStrictHierarchy(role: Member, action: Moderate, expected: False)` (log `20261106-180213-test-filter.log`) |
| 2026-11-06 18:03 | SHA256 restore verification + marker scan | 0 | `RESTORE M1 file byte-exact: True`, `RESTORE M2/M3 file byte-exact: True`, `MARCADORES DE MUTACAO: NENHUM` |
| 2026-11-06 18:04:00 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (final, after all restores) | 0 | `Aprovado! – Com falha: 0, Aprovado: 97, Ignorado: 0, Total: 97, Duração: 44 s` (log `20261106-180400-test-gate.log`) |
| 2026-11-06 18:06:12 | `… sdd-test.ps1 test -TestFilter "Category=Architecture"` | 0 | `Aprovado: 17 … Total: 17` (log `20261106-180612-test-filter.log`) — DomainHasNoInfrastructureTests + DependencyRuleTests build proofs |
| 2026-11-06 18:09 | `wsl -e bash /mnt/c/dev/squid-squad-net/squid-execution/docs/sdd/qa/qa-check-task-0007.sh` | 0 | `RESULT: PASS - all checks green` (25 checks) |

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| F1 | ADJUDICATION (the one accepted coverage gap — drives the PARTIAL verdict; no product defect today) | task file AC-01 clause `presets de grupo (…/não listado) e comunidade (…/listada)` vs `backend/src/Squid.Domain/ContainerPolicy.cs:112` (`Group()`) and `:120` (`Community()`) | The discovery half of the preset ("não listado"/"listada") has **no reachable code path in this task**: `ContainerPolicy` has no discovery field (data-model §2.3 does not define one; AC-01's own field list does not either) and SPEC-0003 puts "Descoberta/listagem de comunidades (FR-016)" out of scope → P-03. The two field-backed preset values (privacy + entry) and posting `membersOnly` are proven (5 of 7 preset values); that clause is not testable as written. ODEV should confirm the AC clause means the aggregate-level default deferred to P-03 — if it was meant to demand a field now, AC-01 needs DEV work, not QA | `specs/001-squid-mvp/data-model.md:99-106` (no discovery field), `docs/sdd/specs/SPEC-0003-domain-container-policy.md:79` (FR-016 → P-03), `ContainerPolicy.cs:107-121` (comments state discovery belongs to the aggregate) |
| F2 | INFO (QA bookkeeping, no product impact) | `docs/sdd/qa/qa-check-task-0007.ps1` AC-06 scan | The first run of the static scan reported 1 hit: `backend/src/Squid.Domain/obj/Debug/net10.0/Squid.Domain.GlobalUsings.g.cs:4 [System.IO]` — the SDK's **generated** implicit-usings file, not product code. Scope corrected to product sources (`obj/`+`bin/` excluded) and re-run green on both platforms; the two real product files have 0 hits either way. Recorded so the correction is visible, not hidden | first run `RESULT: FAIL - 1 check(s)` → corrected run `RESULT: PASS` (both quoted above); `DomainHasNoInfrastructureTests` (csproj-based) never saw it |

No BLOCKER, no MAJOR — every executable scenario is green, including the three break-it
mutations. The verdict is **PARTIAL** for exactly one accepted coverage gap: the discovery clause
of AC-01 ("não listado"/"listada") has no reachable code path in this task (F1), so that clause is
NOT-COVERED and returns to ODEV for adjudication; everything else is proven.

## Not covered, and why

- **Linux / WSL execution of the xunit suite (AC-01..AC-06 on Linux)** — WSL has no .NET SDK
  (re-confirmed this run: `command -v dotnet` → `dotnet-absent`), so only the static twin ran there
  (exit 0) — **unverified** on Linux, never assumed equal to the Windows run that passed. Risk if
  it breaks: none of the Domain code is platform-specific (pure in-memory), but the run itself is
  unproven there.
- **The discovery preset clause of AC-01 ("não listado"/"listada")** — no code path in this task
  (F1): not tested, by SPEC-0003 scope. Risk if it breaks: nothing consumes discovery in P-01;
  FR-016 lands in P-03 and must be tested there.
- **`Private` não federa (R-11), federation points, audit-trail persistence (FR-019)** — explicitly
  out of scope in SPEC-0003 (P-06/P-03); the `PolicyVersion` on the decision is proven, the trail
  is not (it does not exist yet).
- **Concurrency of policy edits ("duas edições concorrentes falham por versão")** — SPEC-0003 says
  only the type and the increment rule exist now (P-03); `NextVersionGrowsAndKeepsEverythingElse`
  proves growth only, not conflict rejection.

Memory-bank flags raised for ODEV (proposals, nothing written to `docs/memory-bank/`):
`[MUDANCA_RELEVANTE: SEGURANCA — Guest negado em qualquer escrita mesmo com Posting=Everyone e SinglePublisher falha fechado so-papel (GAP-11); matriz 5x8 inteira coberta por teste — source: backend/src/Squid.Domain/IPolicyEvaluator.cs:82]`,
`[PADRAO_NOVO: PolicyDecision(Permitted, Reason, PolicyVersion) e IPolicyEvaluator sao o unico caminho de checagem de papel (data-model 3.2 regra dura) — source: backend/src/Squid.Domain/IPolicyEvaluator.cs:33]`.
