<!--
templateVersion: 1.0.0
-->
# QA-0011 — TASK-0013: ActorGrain event-sourced com snapshot e truncamento

- runId: `R-20261107-02` · taskId: `TASK-0013` · qaAttempt: **1/2** · status na entrada: `AWAITING_QA`
- authority: SPEC (SPEC-0007 B-08) · Política: ponytail-qa `full`. Escopo estrito: os 5 ACs do task file — nada fora da lista foi exigido.
- Harness: nenhum script novo e nenhuma dependência nova; runner existente `sdd-test.ps1` (+ gêmeo `.sh`) e os 15 testes já existentes em `backend/tests/Squid.Tests/Actors/**`. Todo teste foi **executado** nesta sessão; nada foi lido do log do DEV como substituto de execução.
- Prova can-fail (S-16): duas mutações no produto (`ActorGrain.cs`, dentro dos `touches`), cada uma derrubando o teste do AC correspondente, com SHA256 PRE=POST byte-exact após cada restauração.

## AC → cenário (executado pela QA)

| AC | Cenário | Comando | Exit | Resultado |
| --- | --- | --- | --- | --- |
| AC-01 | Round-trip: `ActorCreated` persistido e replay igual ao estado direto (junto com os 4 `GetStateAsync`/mapeamento) | `sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Actors` | 0 | PASS — `Create_WithValidHandle_PersistsActorCreated_AndReplayEqualsDirectState` aprovado entre os **15/15** (`20261107-172302-test-filter.log`); nomes derivados da assembly por `--list-tests`, não de memória |
| AC-02 | 9 casos malformados (teoria: null, "", 2, 31, maiúsculo, `-`, `.`, `ação`, `\n`) + duplicidade — erro tipado e zero escrita nos dois | idem (filtro Actors) | 0 | PASS — `Create_WithMalformedHandle_ThrowsTypedError_AndPersistsNothing` (9 linhas) e `Create_WithTakenHandle_ThrowsHandleTaken_AndPersistsNothingMore` aprovados; `CountRowsAsync`=0/preserve-JSON asserts executados |
| AC-03 | Ao cruzar o limiar (8): snapshot gravado **e log truncado**; prova can-fail = quebrar a truncagem e ver vermelho | filtro Actors · mutação M1: `stream.Events.Clear()` comentado em `ActorGrain.cs:124` | 0 → **1** → 0 | PASS — verde 15/15; **M1 vermelho com exit real 1** (`20261107-172502-test-filter.log` cabeçalho `exit: 1`): `WhenLogReachesThreshold_SnapshotIsWritten_AndOldEventsAreTruncated` falha em `ActorGrainTests.cs:117` (`Assert.Empty` — log não truncado) + 2 colaterais (AC-04 `:139` `InvalidOperationException: event ActorCreated does not fit`, AC-05 `:60`); 12/15 seguem verdes ⇒ a mutação atinge só a truncagem. Restaurado → verde de novo |
| AC-04 | Reconstrução pós-truncamento (snapshot + eventos novos) idêntica ao estado direto; prova can-fail = descartar a informação no truncamento e ver vermelho | filtro Actors · mutação M2: limiar grava `Events.Clear()` **sem** `Snapshot = state` (`ActorGrain.cs:120-125`) | 0 → **1** → 0 | PASS — **M2 vermelho com exit real 1** (`20261107-172653-test-filter.log` cabeçalho `exit: 1`): `RebuildAfterTruncation_EqualsDirectState` falha em `ActorGrainTests.cs:139` (`InvalidOperationException: event ActorDisplayNameChanged does not fit` — log truncado sem snapshot não reconstrói) + AC-03 `:115` e AC-05 `:59` (`Assert.NotNull` snapshot nulo); 12/15 verdes. Restaurado → verde |
| AC-05 | (a) serialização só com material embrulhado/público nas duas formas do row; (b) gate completo exit 0 sem arquivo alterado fora dos `touches` | filtro Actors · `sdd-test.ps1 test` ×2 · `git status --porcelain` ×2 · SHA256 | 0 / 0 / 0 | PASS — (a) `PersistedJson_ContainsOnlyWrappedAndPublicMaterial_InLogAndInSnapshot` aprovado (row aberto **e** pós-truncamento: sem `RK`/privada em claro, asserts negativos executados). (b) Gate **119/119 exit 0** baseline (`20261107-172124-test-gate.log`) e **119/119 exit 0** pós-restauração (`20261107-172854-test-gate.log`). Escopo: por `git status`, as únicas escritas de produto da TASK-0013 são os próprios `touches` (`backend/src/Squid.Grains/Actors/`, `backend/tests/Squid.Tests/Actors/`); os demais caminhos sujos estão **classificados como interferência fora de escopo** (ver Findings/F1-note e "Não coberto") — nenhum deles falhou o gate |

## Reproduzir (3 linhas)

```powershell
# pré-requisito: .NET SDK 10 (dotnet --version) na raiz do repo; nada instalado por esta QA
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.Actors"   # esperado: exit 0, 15/15
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test   # esperado: exit 0, 119/119 (113 + 6 Crypto da TASK-0012)
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-state.ps1 check TASK-0013   # esperado: exit 0
```

Prova do can-fail (M1): comentar `stream.Events.Clear();` em `backend/src/Squid.Grains/Actors/ActorGrain.cs:124`, rodar o filtro Actors → **exit 1 real** com falha em `ActorGrainTests.cs:117`; restaurar e conferir `Get-FileHash -Algorithm SHA256 backend\src\Squid.Grains\Actors\ActorGrain.cs` = `33FFE8627BA83435B2B9E1FEB44BC4F149E89E03886D8793FE8B296BB2015440` (igual ao PRE).

## Última execução e artefatos

| Quando | Comando | Exit | Artefato / tail |
| --- | --- | --- | --- |
| 2026-11-07 17:21:24 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` | 0 | `docs/sdd/runs/test-runs/20261107-172124-test-gate.log` — `Aprovado! – Com falha: 0, Aprovado: 119, Total: 119` (baseline pré-mutação) |
| 2026-11-07 17:23:02 | `... sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Actors` | 0 | `docs/sdd/runs/test-runs/20261107-172302-test-filter.log` — `Aprovado: 15, Total: 15` |
| 2026-11-07 17:25:02 | mesmo filtro, **mutação M1** (truncagem desligada) | **1** (real; cabeçalho do log `exit: 1`) | `docs/sdd/runs/test-runs/20261107-172502-test-filter.log` — `Com falha: 3, Aprovado: 12`; frames `ActorGrainTests.cs:117`, `:139`, `ActorPersistedStateTests.cs:60` |
| 2026-11-07 17:26:53 | mesmo filtro, **mutação M2** (snapshot descartado no truncamento) | **1** (real; cabeçalho `exit: 1`) | `docs/sdd/runs/test-runs/20261107-172653-test-filter.log` — `Com falha: 3, Aprovado: 12`; frames `ActorGrainTests.cs:115`, `:139`, `ActorPersistedStateTests.cs:59` |
| 2026-11-07 17:28:54 | `... sdd-test.ps1 test` (pós-restauração) | 0 | `docs/sdd/runs/test-runs/20261107-172854-test-gate.log` — `Aprovado: 119, Total: 119` |
| 2026-11-07 ~17:27 | Git Bash: `bash docs/sdd/_tools/sdd-test.sh test backend/Squid.slnx` | 0 | `Aprovado: 119, Total: 119` (`SH_EXIT=0`; `--no-build` sobre o Debug do gate; S-12 já saber) |
| 2026-11-07 ~17:27 | Git Bash: `bash docs/sdd/_tools/sdd-test.sh test` (sem target) | 1 | stderr `sdd-test: no *.sln found within depth 3` + `MSB1003` rotulado "real test failure" — reprodução do **S-11** pré-existente |
| — | `Get-FileHash -Algorithm SHA256 ...ActorGrain.cs` (PRE, entre M1 e M2, POST ×2, final) | 0 | os 5 checkpoints = `33FFE8627BA83435B2B9E1FEB44BC4F149E89E03886D8793FE8B296BB2015440` (`S16_M1_MATCH=True`, `S16_M2_MATCH=True`, `FINAL_MATCH=True`) |
| — | `dotnet test backend\Squid.slnx -c Debug --no-build --filter ... --list-tests` | 0 | 15 nomes `Squid.Tests.Actors.*` + 6 nomes `Squid.Tests.Crypto.*` (delta 113→119 explicado) |

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| F1 | MAJOR (harness, não produto) | `docs/sdd/_tools/sdd-test.ps1:79-83` (+ `:214`) | **Vermelho real é classificado como ambiente (exit 3).** `Test-SandboxSignature` casa o substring minúsculo `handle` — presente nas mensagens/nomes deste próprio domínio (`handle: …`, `handle 'x' does not match`) — e o detector de resumo `^\s*(Passed!\|Failed!\|Aprovado!\|Falhou!)` não reconhece o resumo localizado `Com falha!`, então `noSummary` fica true. Resultado: os dois runs de mutação com 3 falhas reais (dotnet exit 1) terminaram com o wrapper imprimindo `test: exit 1 with the sandbox signature … exit 3`. Se a QA confiasse no exit do wrapper (sem S-15), um gate quebrado seria ondulado como "ambiente, não é defeito". É a variante mais grave do detector já apontado no **S-13** (lá o exit estava certo e só o motivo impresso estava errado); aqui o **código do exit está errado** | `docs/sdd/runs/test-runs/20261107-172502-test-filter.log:2` e `20261107-172653-test-filter.log:2` (`exit: 1` no cabeçalho gravado pela própria ferramenta) vs saída de console `M1_EXIT=3` / `M2_WRAPPER_EXIT=3` |
| F2 | menor (pré-existente **S-11/S-12**, só reproduzido aqui) | `docs/sdd/_tools/sdd-test.sh:18-30` | O gêmeo `.sh` não descobre o repo (só procura `*.sln`; o repo tem `backend/Squid.slnx`) e o `exit 1` do guard roda dentro de `$()`, então não aborta: segue para `dotnet test ""` → MSB1003 rotulado "real test failure". Com caminho explícito o `.sh` roda green (119/119). Já está no backlog (S-11 discovery, S-12 contrato `--no-build`/log) — sem nova entrada | `BARE_SH_EXIT=1` com stderr `no *.sln found within depth 3`; `SH_EXIT=0` com `test backend/Squid.slnx` |
| F3 | nota de escopo (não é defeito da TASK-0013) | `backend/Directory.Packages.props`, `backend/src/Squid.Api/Squid.Api.csproj`, `backend/src/Squid.Api/Crypto/`, `backend/tests/Squid.Tests/Crypto/` | Caminhos **fora** dos `touches` da TASK-0013 aparecem sujos no `git status` — são a escrita paralela da **TASK-0012** (Crypto), confirmada pela dispatch; não foram tocados, revertidos nem "limpos" por esta QA. Os 6 testes dela (`Squid.Tests.Crypto.KeyPrimitivesTests.*`) estavam **verdes** nos dois gates completos (119 = 113 + 6), portanto **nenhuma falha de interferência** ocorreu; se aparecerem em runs futuros, o diagnóstico a citar é o desses paths, não da TASK-0013. Os `docs/sdd/**` sujos são da ODEV, já presentes no baseline do DEV (evidence 17:12:54) | `git status --porcelain` antes (17:20) e depois (17:29) — mesma lista, nenhuma escrita nova fora dos `touches` |
| — | — | produto | **Nenhum defeito de produto encontrado. Os 5 ACs passaram com testes executados; nenhum BLOCKER** | gate 119/119 ×2, filtro 15/15 |

## Não coberto / unverified

- **Linux nativo (WSL): `unverified`** — o WSL existe (`Linux 6.18.54.1-microsoft-standard-WSL2`) mas **não tem dotnet SDK** (`~/.dotnet`, `/usr/share/dotnet`, `/usr/lib/dotnet` ausentes); instalar SDK exigiria gate (aprovação de ODEV/humano), então a suíte em kernel Linux não foi executada. O que se executou foi o **script `.sh`** via Git Bash + dotnet 10.0.403 do Windows (exit 0 com target, exit 1 sem) — isso prova a lógica do script, **não** o binário Linux. Nunca assumido igual ao caminho Windows.
- **Compose/container** — fora dos ACs desta task (DoD de SPEC-0004) e não executado; nenhum AC aqui depende de stack viva.
- **CI (GitHub Actions, TASK-0009)** — não re-executado; fora dos ACs.
- Mutação da QA tocou apenas `backend/src/Squid.Grains/Actors/ActorGrain.cs` (touch da própria task), com restore byte-exato provado por SHA256 em 5 checkpoints; o arquivo ganhou mtime novo, conteúdo idêntico ao PRE.

## memoryBankFlags

- `[DIVERGENCIA: sdd-test.ps1 classificou vermelho real como ambiente - Test-SandboxSignature casa "handle" e o detector de resumo nao reconhece "Com falha!", transformando exit 1 (3 falhas) em exit 3 - variante mais grave que a do S-13, agora com o codigo do exit errado - source: docs/sdd/_tools/sdd-test.ps1:80, docs/sdd/runs/test-runs/20261107-172502-test-filter.log:2]`
- `[GAP: caminho Linux nativo da TASK-0013 nao executavel nesta maquina - WSL sem dotnet SDK (instalacao exige gate) - suíte em kernel Linux unverified; gêmeo .sh so executavel com caminho explicito, ja backlogado como S-11/S-12 - source: docs/sdd/_tools/sdd-test.sh:18]`
