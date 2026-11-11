<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: VEREDITO
- owner: qa
- lastVerified: 2026-11-10 15:14

# QA-0028 — Cota de mídia da instância (`507`)

- task: TASK-0028
- spec: SPEC-0009
- runId: R-20261109-01
- verdict: PASSED
- qaAttempt: 1/2

Normative reading used: SPEC-0009 section `F` + `Gaps` (GAP-19/20), `specs/001-squid-mvp/data-model.md`
§2.13 (escopo instância, `UsedBytes`/`LimitBytes` INTEIROS R-19, default 10 GiB, `UpdatedAt` = carimbo
da reconciliação diária) + §5 (ledger = script SQL + Dapper, a emenda humana GAP-20), contrato
`http-api.md` §5 (`507 quota_exceeded` no upload) e §8 (catálogo de erros), e o humanGate da task
(migration/liberação registrada; `SkiaSharp.NativeAssets.Linux` 4.155.0 aprovado 2026-11-10 08:00:15
com a AC-06).

**Verdict: PASSED** — the six ACs are proven by executed scenarios, no BLOCKER finding, and every
DEV decision the dispatch asked to verify was verified **by execution, not by reading**:

1. the gate reserves the **PROCESSED** bytes — a 9000-byte JPEG whose EXIF padding is stripped
   before storage answers `200` with `sizeBytes=329` while the request itself is above the 8192
   quota (`Ac01c`; the discriminating case: counting the request would refuse it);
2. a refused upload touches **NOTHING** — the full ledger row hash (including `limit_bytes`, pinned
   to a sentinel 777), the whole-volume digest (path + size + SHA-256 of every file, not just the
   names) and the upload-record count are identical before/after the `507`, and with no row at all
   no row is born (`Ac01a`/`Ac01b`);
3. the optimistic increment does **NOT** move `UpdatedAt` — only the sweep stamps it (§2.13) (`Ac02b`);
4. the ledger row is born on the first use (`ON CONFLICT DO NOTHING`, DEV test) and the migration
   `0001` that creates/seeds the table was not touched (checksum-registered);
5. reconcile is the source of truth: it corrects drift **both ways** and follows a blob deleted
   behind the ledger's back (`Ac02a`);
6. the hosted service lives in `Squid.Worker` and runs the same pass (`RunOnceAsync`), while the API
   host does **not** register it (AC-04's own test), and `Quota:ReconcileHours` falls back to 24
   (`Program.cs:41`, static);
7. AC-06's static declaration (both packages at `4.155.0` + `PackageReference`) and `dotnet restore`
   exit 0 with `skiasharp.nativeassets.linux/4.155.0` in the cache.

## What is proven

| AC | Scenario (executed) | Script | Result |
| --- | --- | --- | --- |
| AC-01 | DEV: `QuotaUploadGateTests.Ac01_upload_above_the_quota_is_507_with_nothing_written_then_a_valid_upload_sums` — MP4 de 9000 > 8192 → `507 quota_exceeded`, hash da linha / conjunto de blobs / contagem de registros iguais antes e depois, GIF seguinte soma o tamanho. QA: `Ac01a` — **digest do volume inteiro** (path+size+SHA-256 de cada arquivo) + hash da linha com `limit_bytes=777` sentinela + registros idênticos após a recusa; `Ac01b` — recusa com **nenhuma** linha no ledger não nasce linha (0 linhas, volume e registros iguais); `Ac01c` — 9000 pedidos / **329 guardados** / `200` (a gate conta os bytes processados). Can-fail **M1** (condição do UPDATE neutralizada) e **M2** (a própria recusa grava blob+registro antes do 507): M2 vermelho **exatamente na linha 73** (digest do volume) com status/linha/limit/used ainda passando — o hard spot pega a escrita; **M8** (gate conta o original) vermelho no `Ac01c` | `backend/tests/Squid.Tests/Quota/QaTask0028Tests.cs` · `QuotaUploadGateTests.cs` · `sdd-test.ps1 test -TestFilter FullyQualifiedName~Quota` → `20261110-144447` 11/11 exit 0 · `QA-0028-observations.log` · `QA-0028-mutations.log` | PASS |
| AC-02 | DEV: `QuotaReconcileTests.Ac02_the_blob_sweep_corrects_used_bytes_and_stamps_updated_at` — drift `+4242` e `UpdatedAt` antigo corrigidos pela função invocada diretamente. QA: `Ac02a` — drift **para baixo** (`used_bytes=1`) corrigido para o real, e blob apagado do volume na segunda varredura (`real=371 → aposExclusao=42`); `Ac02b` — o incremento deixa `updated_at` no valor sentinela (`2000-01-01…`), só a varredura carimba (`2026-11-10T17:43:09.6746454Z`). Can-fail **M3** (varredura para de escrever) e **M9** (incremento passa a carimbar `updated_at`) | mesmo arquivo · `QuotaReconcileTests.cs` · filtros `.Ac02` | PASS |
| AC-03 | DEV: `QuotaLedgerTests.Ac03_ledger_columns_are_integer_and_the_default_limit_is_10_gib` — `pragma_table_info` devolve `INTEGER` nas duas colunas (R-19), config sem `Quota:LimitBytes` → `10737418240`, e migration nova semeia `id=1` com o mesmo default; `Ac03_the_ledger_row_is_born_on_the_first_use` — linha apagada renasce no primeiro uso aceito com o limite configurado. Migration `0001_init_projections.sql` lida: `used_bytes INTEGER … limit_bytes INTEGER … INSERT … ON CONFLICT (id) DO NOTHING` — script intocado. Can-fail **M5** (default 10 GiB → 1 MiB) | `QuotaLedgerTests.cs` · `Select-String quota_ledger backend/src/Squid.Persistence/schema/0001_init_projections.sql` | PASS |
| AC-04 | DEV: `QuotaReconcileTests.Ac04_the_hosted_service_runs_the_sweep_without_touching_the_upload_write_path` — o serviço hospedado invocado pelo teste corrige o ledger sem tocar blobs nem registros de upload, e **o API host não o registra** (`Assert.DoesNotContain` sobre `IHostedService`); registro real em `Squid.Worker/Program.cs:42-45` (lido). Can-fail **M4** (`RunOnceAsync` vira `Task.FromResult(-1L)`) | `QuotaReconcileTests.cs` · `backend/src/Squid.Worker/Program.cs` | PASS |
| AC-05 | **Gate:** `sdd-test.ps1 test` → `20261110-150722-test-gate.log` com `exit: 0` e `Com falha: 0, Aprovado: 348, Total: 348` lido **dentro** do log (S-22); baseline da QA `20261110-142613` = 343/343 `exit: 0` (343 = 337 do baseline da fase + 6 do DEV). **Escopo:** mtime desde o dispatch 13:11 → **15 arquivos, 0 fora dos touches**; delta git vs `QA-0028-git-baseline.txt` → **+7 entradas, todas** `docs/sdd/qa/QA-0028*`/`qa-*-task-0028.*` + `tests/Squid.Tests/Quota/QaTask0028Tests.cs`; resíduo `QA-MUTATION` em `backend/` = **0**; SHA256 final = **PRE** nos 7 arquivos mutados (M1/M9 `8B7ACE561ADE`, M2/M8 `161721C674C7`, M3 `F3799B2CF0CA`, M4 `A9CE3D45C156`, M5 `C8DAD041088F`, M6 `4652987695A3`, M7 `5376767C0F5D`); backups `qa0028-*.bak` no TEMP = 0 | gate + varredura de escopo + `qa-check-task-0028.ps1` / `.sh` | PASS |
| AC-06 (gate humano 2026-11-10 08:00:15) | `LinuxNativeAssetsTests.Ac06_skia_sharp_linux_natives_are_declared_at_the_skia_sharp_version` (executado, dentro do gate): `PackageVersion` de `SkiaSharp` **e** de `SkiaSharp.NativeAssets.Linux` ambos `4.155.0` em `Directory.Packages.props` + `PackageReference` em `Squid.Service.csproj`. Re-executado: `dotnet restore backend/src/Squid.Service/Squid.Service.csproj` → **exit 0** com `skiasharp.nativeassets.linux/4.155.0` no cache (igual ao `skiasharp/4.155.0`). Can-fail **M6** (PackageReference sai do csproj) e **M7** (a linha do props deixa de casar). **A execução do decode em Linux segue `unverified`** (WSL sem `dotnet`) — como a AC-06 é estática, isso não a derruba | `LinuxNativeAssetsTests.cs` · `dotnet restore` · `QA-0028-sh-twin.log` | PASS (execução Linux: unverified) |

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `backend/tests/Squid.Tests/Quota/QaTask0028Tests.cs` | xunit via `sdd-test.ps1 test -TestFilter` | 5 cenários próprios (hard spots): recusa intocável com digest do volume, recusa sem linha, gate sobre bytes processados, varredura nas duas direções, `updated_at` exclusivo da varredura; fixtures construídos aqui (JPEG com APP1 de ~8,6 KiB) |
| `backend/tests/Squid.Tests/Quota/{QuotaUploadGateTests,QuotaReconcileTests,QuotaLedgerTests,LinuxNativeAssetsTests,QuotaHost}.cs` | xunit (testes do DEV, re-executados) | os 6 testes do gate 343 — re-executados no gate final e no filtro `~Quota` |
| `docs/sdd/qa/qa-mutate-task-0028.ps1` | pwsh (gate `sdd-test.ps1`) | **9** ciclos can-fail (M1..M9): SHA256 PRE (S-16) → mutação `QA-MUTATION` com pattern-count=1 → vermelho classificado **no log** (S-22) → restore byte-exact + bump de mtime (S-23) → hash=PRE → verde |
| `docs/sdd/qa/qa-check-task-0028.ps1` / `.sh` | pwsh / bash (WSL) | 22 checks de evidência (gate, filtros, observações, mutações, restore, escopo, git, task, AC-06, gêmeo Linux) |
| `docs/sdd/qa/QA-0028-observations.log` | artefato (`--logger console;verbosity=detailed`) | as 5 observações com os números reais (9000/329/8192, `limitBytesAposRecusa=777`, `real=371 → 42`, stamps) |
| `docs/sdd/qa/QA-0028-mutations.log` | artefato | saída dos 9 ciclos + `SUMMARY cycles=9 failed=0 residue-QA-MUTATION=0 temp-backups=0` |
| `docs/sdd/qa/QA-0028-sh-twin.log` | artefato (WSL bash) | gêmeo `sdd-test.sh` probe/test: `dotnet: command not found`, `test: exit 127` (Linux = `unverified`) |
| `docs/sdd/qa/QA-0028-git-baseline.txt` | artefato | `git status --porcelain -uall` capturado antes da QA escrever (141 entradas) |

## How to reproduce

Prerequisites: Windows com `dotnet` 10 e o repositório em `C:\dev\squid-squad-net\squid-execution`
(nenhuma instalação, nenhum serviço externo — os testes sobem host local em porta dinâmica).

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Quota"        # esperado: exit 0, Aprovado 11, Total 11
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0028.ps1                                             # esperado: exit 0, SUMMARY cycles=9 failed=0 residue-QA-MUTATION=0 temp-backups=0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test                                               # esperado: exit 0, Com falha 0 / Aprovado 348 / Total 348
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0028.ps1                                              # esperado: RESULT: PASS, exit 0
bash docs/sdd/qa/qa-check-task-0028.sh                                                                                             # esperado: RESULT: PASS, exit 0 (WSL)
```

## Last run

| Date/Time | Command | Exit | Output (max 10 lines) |
| --- | --- | --- | --- |
| 2026-11-10 14:26:13 | `sdd-test.ps1 test` (baseline da QA) | 0 | `20261110-142613-test-gate.log` → `exit: 0` · `Com falha: 0, Aprovado: 343, Total: 343` |
| 2026-11-10 14:34:49 | `sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0028Tests` (1ª execução) | 1 | `20261110-143449-test-filter.log` → `Ac01c` vermelha: `Expected OK / Actual InsufficientStorage` — defeito do **fixture da QA** (APP1 2 bytes grande → stripper manteve o arquivo), corrigido em `QaTask0028Tests.cs` |
| 2026-11-10 14:43:09 | `dotnet test … --filter QaTask0028Tests --logger console;verbosity=detailed` | 0 | `QA-0028-observations.log` → `Aprovados: 5` + `tamanhoOriginal=9000 tamanhoGuardado=329 limiteCota=8192` |
| 2026-11-10 14:44:02 | `sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0028Tests` | 0 | `20261110-144402-test-filter.log` → `Com falha: 0, Aprovado: 5, Total: 5` |
| 2026-11-10 14:44:47 | `sdd-test.ps1 test -TestFilter FullyQualifiedName~Quota` | 0 | `20261110-144447-test-filter.log` → `Com falha: 0, Aprovado: 11, Total: 11` (6 DEV + 5 QA) |
| 2026-11-10 15:11:20 | `dotnet restore backend/src/Squid.Service/Squid.Service.csproj` | 0 | `Todos os projetos estão atualizados` · cache `skiasharp.nativeassets.linux` = `4.155.0` |
| 2026-11-10 14:59:03–15:03:00 | `qa-mutate-task-0028.ps1` (9 ciclos) | 0 | `QA-0028-mutations.log` → M1..M9 `PROVEN` · vermelhos classificados nos logs `20261110-145903..150247` · `SUMMARY cycles=9 failed=0 residue=0 temp-backups=0` |
| 2026-11-10 14:52:57 | `bash docs/sdd/_tools/sdd-test.sh probe` / `test backend/Squid.slnx` (WSL) | 0 / 1 | `QA-0028-sh-twin.log` → `dotnet: command not found` · `test: exit 127 (real test failure)` (Linux = `unverified`) |
| 2026-11-10 15:06:40 | varredura de escopo (mtimes desde 13:11 + git vs baseline + resíduo) | 0 | 15 arquivos, **0 fora dos touches** · git **+7** só artefatos QA/tests · resíduo `QA-MUTATION` 0 |
| 2026-11-10 15:07:22 | `sdd-test.ps1 test` (gate final AC-05) | 0 | `20261110-150722-test-gate.log` → `exit: 0` · `Com falha: 0, Aprovado: 348, Total: 348` |
| 2026-11-10 15:16 | `qa-check-task-0028.ps1` + `bash qa-check-task-0028.sh` | 0 / 0 | `QA-0028-check.log` → `SUMMARY total=22 fails=0` · `RESULT: PASS` nos dois sistemas |

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| — | — | — | **Nenhum achado de produto desta task.** Os 6 ACs passaram e cada hard spot provou pode-falhar (M1/M2/M8 sobre o AC-01, M3/M9 sobre o AC-02, M4 sobre o AC-04, M5 sobre o AC-03, M6/M7 sobre o AC-06) | `QA-0028-mutations.log` |
| 1 | MINOR (pré-existente, re-confirmado) | `docs/sdd/_tools/sdd-test.sh:64` | O gêmeo Linux do gate **não tem a guarda de ambiente** do `.ps1`: sem `dotnet` no WSL ele imprime `test: exit 127 (real test failure)` — classifica ferramenta ausente como falha real (S-22 no traje de Linux). Mesmo achado das QA-0023..0027 | `QA-0028-sh-twin.log` (probe exit 0 / test exit 1) |

Notas (não são defeitos):

- **Migration `0001` semeia `id=1`** (`0, 10737418240, now`, `ON CONFLICT DO NOTHING`): a linha existe
  desde a criação do schema; o caminho de nascimento exigido pela AC-03 é provado apagando-a e
  subindo um upload aceito (teste do DEV), e uma **recusa** não nasce linha nenhuma (`Ac01b`).
  Comportamento coerente com o default 10 GiB do §2.13 — registrado para ODEV julgar se a leitura
  literal de "nasce no primeiro uso" precisa de emenda.
- **Primeira execução do `Ac01c` vermelha por defeito meu**: o APP1 do fixture foi gravado com o
  campo de length 2 bytes grande, o `ExifStripper` aplicou a regra documentada ("estrutura que não
  parseia volta intacta" — `ExifStripper.cs:8`) e os 9000 bytes foram guardados → o `507` estava
  certo e o teste errado. Fixture corrigido (length = marcador + 2 + payload); o log vermelho fica
  registrado como prova de que o cenário pode falhar. Tentativa anterior ainda falhou com
  `CS0246 ITestOutputHelper` (faltava `using Xunit.Abstractions;`) — ambos defeitos da QA, corrigidos
  antes dos registros finais.
- **`Quota:ReconcileHours`**: só verificado estaticamente (`Program.cs:38` lê a chave,
  `Program.cs:41` cai no `: 24`) — a cadência real de 24 h não é executável num teste curto.
- **Varredura sem linha**: `SetUsedAsync` é `UPDATE … WHERE id=1`, então uma varredura com a linha
  apagada não a recria (só o próximo upload aceito nasce). Alcançável apenas por SQL manual;
  a deriva entre varreduras é a assumpção aceita dos gaps.

## Not covered, and why

- **Suíte do produto em Linux** — o WSL desta máquina não tem `dotnet` (instalar exige gate humano;
  aprovações desativadas nesta sessão) → `unverified`, nunca assumido igual ao Windows que passou.
  O que **foi** executado no Linux: os gêmeos bash (`sdd-test.sh` — achado 1 — e
  `qa-check-task-0028.sh`). O decode com `SkiaSharp.NativeAssets.Linux` continua `unverified` até a
  CI ubuntu rodar (a AC-06 é estática por definição).
- **`qa-mutate-task-0028.ps1` em Linux** — o driver orquestra `sdd-test.ps1`/`dotnet`, indisponíveis
  no WSL → gêmeo `.sh` não faz sentido (mesmo precedente das QA-0024..0027); a restauração
  byte-exact (S-16/S-23) é provada no Windows e conferida de novo pelos checkers nos dois sistemas.
- **Corrida de dois uploads no primeiro uso** (a "perda de no máximo um incremento" documentada em
  `SqliteQuotaLedger.cs:51`) — não exercitada; os testes são sequenciais. Risco: sub-registro até a
  próxima varredura, que a assumpção dos gaps já aceita.
- **`Quota:ReconcileHours` em execução** (laço do hosted service por 24 h) — não executado; o passo
  único `RunOnceAsync` é o que a AC-04 pede e foi executado.
- **Compose/CI com os natives Linux** — fora desta máquina (gate `compose` exige podman + rede);
  fica para o run ubuntu do CI.

## Memory bank (rota `qa-integracao`)

- Rota lida (3 arquivos, 115 linhas): `build-run-test.md#Local tests without gate` — **S-22 e S-23
  re-confirmados ao vivo** (todo vermelho classificado dentro do próprio log; todo restore com bump
  de mtime e hash = PRE); `contracts.md` — segue **sem** menção da mídia/quota (ver flag);
  `integrations.md` — ainda template `RASCUNHO`, sem conteúdo a conferir.
- `[GAP]` `docs/memory-bank/contracts.md:18` — ainda afirma que "mídia … seguem sem handler", mas
  `POST /api/media` existe desde a TASK-0026/0027 e o `507 quota_exceeded` desta task não está lá.
  Proposta de consolidação no fecho da P-04; o arquivo só se escreve pelo consolidador.
  (Re-levantado nas QA-0026/0027, continua aberto.)
- `[MUDANCA_RELEVANTE: PADRAO — ledger de cota em SQL + Dapper na migration 0001 (emenda humana
  GAP-20 ao data-model §5, substituindo EF Core) com gate 507 otimista no caminho do upload e
  varredura diária de blobs como fonte da verdade — source:
  backend/src/Squid.ReadModels/Quota/SqliteQuotaLedger.cs:8-L14]` — proposta de consolidação no
  fecho da P-04.
