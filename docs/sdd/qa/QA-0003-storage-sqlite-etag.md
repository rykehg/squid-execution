<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: VERDE
- owner: qa
- lastVerified: 2026-11-06 08:55:44

# QA-0003 — TASK-0003 Storage SQLite com ETag imposto e store name como parâmetro

- task: TASK-0003
- spec: SPEC-0001
- runId: R-20261104-01
- qaAttempt: 1/2
- verdict: PASSED

## What is proven

Tested only against the `## Acceptance criteria` list AC-01..AC-07 of the task file. Everything below
was executed in this session on `C:\dev\squid-squad-net\squid-execution` (Windows, PowerShell 5.1,
SDK 10.0.403, integrity Medium). DEV's claim was re-run, not trusted.

| AC | Scenario | Command | Exit | Result |
| --- | --- | --- | --- | --- |
| AC-01 | duas escritas com o mesmo ETag esperado: a 2ª lança `InconsistentStateException` e o disco fica com o estado da 1ª | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.Storage"` | 0 | PASS — `Aprovado! … Aprovado: 11 … Total: 11` (logs `20261106-084156-test-filter.log` e `20261106-084205-test-filter.log`); caso `SqliteGrainStorageTests.Write_WithTheSameExpectedETag_SecondThrowsAndTheFirstStateStaysOnDisk` lê de volta `Value=2` e o ETag da 1ª escrita depois da 2ª lançar. **Não é teste decorativo**: mutação M1 (abaixo) removeu `AND Version = @Version` e o caso falhou em `SqliteGrainStorageTests.cs:62` com exit 1 |
| AC-02 | escrita com ETag nulo sobre linha existente lança `InconsistentStateException` e não sobrescreve | mesmo filtro (AC-01) | 0 | PASS — `Write_WithNullETagOnAnExistingRow_ThrowsAndDoesNotOverwrite`: grava `Value=1`, tenta sobrescrever com ETag nulo, lança, e a leitura devolve `Value=1`. Caminho do INSERT (`SqliteGrainStorage.cs:101-106`) devolve 0 linhas em conflito → conflito |
| AC-03 | dois stores com o mesmo `grainId` guardam linhas separadas; leitura cruzada nos dois sentidos | mesmo filtro (AC-01) | 0 | PASS — `TwoStores_SameGrainId_KeepSeparateRows_ReadInBothDirections`: `actors`=11 e `containers`=22 lidos nos dois sentidos; chave primária `(StoreName, GrainId)` em `SqliteGrainStorage.cs:26` |
| AC-04 | leitura de chave inexistente devolve `State = null` e `ETag = null`, sem exceção | mesmo filtro (AC-01) | 0 | PASS — `Read_OfAMissingKey_ReturnsNullStateAndNullETagWithoutThrowing` (`SqliteGrainStorageTests.cs:114`) |
| AC-05 | `ClearStateAsync` respeita a regra de ETag e depois a leitura volta a ser ausente | mesmo filtro (AC-01) | 0 | PASS — 4 casos: ETag divergente lança e mantém a linha, ETag nulo sobre linha existente lança, ETag correto apaga (`Clear_WithTheMatchingETag_RemovesTheRow_ThenReadIsAbsent` → `State=null`, `ETag=null`, `RecordExists=false`), clear sem linha é no-op |
| AC-06 | grep scriptado sobre os `.cs` do projeto falha se houver nome de store como literal; exit 0 no estado correto | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0003.ps1` e o gêmeo `bash docs/sdd/qa/qa-check-task-0003.sh` (WSL) | 0 e 0 | PASS — Windows: `ok AC-06 positive - 0 store-name literal` + `ok AC-06 can-fail` (mesmo scanner achou `"actors"` injetado numa fixture em %TEMP%, apagada no `finally`); Linux/WSL: mesma dupla, exit 0. O teste xunit `StoreNameLiteralTests` **também foi provado capaz de falhar**: mutação M2 injetou `StoreName = 'actors'` em `PersistenceAssembly.cs` → `Com falha 1 … StoreNameLiteralTests.cs:line 44`, exit 1 |
| AC-07 | suíte roda contra SQLite em arquivo temporário real, deixa o arquivo limpo e passa duas vezes seguidas | `… sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.Storage"` duas vezes seguidas, no mesmo processo | 0 e 0 | PASS — run A `11/11` (log `20261106-084156`) e run B `11/11` (log `20261106-084205`), 9 s de intervalo; arquivo real por teste (`Data Source=<temp>\squid-grainstate-<guid>.db;Pooling=False`, um GUID por caso, nunca memória compartilhada) comprovado lendo a coluna `Version` direto do arquivo (`Qa0003EtagFormatTests`); limpeza: `ok AC-07 leftovers - 0 squid-grainstate-* file in %TEMP%` depois das execuções |

Gate completo re-executado por QA (reivindicação de DEV, verificada): `sdd-test.ps1 test` → **exit 0,
Aprovado 27, Com falha 0, Total 27** (log `20261106-083953-test-gate.log`), antes de qualquer escrita
minha. Gate final depois das mutações e restaurações: **exit 0, 28/28** (27 de DEV + 1 sonda QA,
log `20261106-085028-test-gate.log`).

## GAP-04 — verificação pedida ao QA (ETag = `GrainStateVersion` em base64 + comparação exata)

A suposição em vigor no `## Gaps` da tarefa é `etag` = `GrainStateVersion` em base64 e comparação
exata em `WHERE Version = @Version`. Verificada em três camadas independentes:

| Camada | O que foi verificado | Comando / artefato | Exit |
| --- | --- | --- | --- |
| Estático | UPDATE e DELETE comparam `Version = @Version` | `qa-check-task-0003.ps1` seção 4 (idem `.sh`) | 0 |
| Dinâmico | o ETag devolvido pela API **é** a coluna `Version` do arquivo SQLite, e ela decodifica para `"1"` na 1ª escrita e `"2"` na 2ª (base64 do decimal, `Convert.ToBase64String(UTF8("1"))`) | `backend/tests/Squid.Tests/Storage/Qa0003EtagFormatTests.cs`, dentro do filtro de storage: `Aprovado 12 Total 12` (log `20261106-084624-test-filter.log`) | 0 |
| Mutação | tirar a comparação exata quebra a suíte | ver Findings #1 | 1 |

Conclusão: a implementação em `backend/src/Squid.Persistence/SqliteGrainStorage.cs:111-117`
(`WHERE StoreName = @StoreName AND GrainId = @GrainId AND Version = @Version`) e `:154-158` (DELETE)
corresponde à suposição do GAP-04, e o encode em `:187-195` é base64 do decimal da versão. O caminho
de INSERT (`:101-106`) não tem `WHERE Version` — não há versão a comparar — e resolve por
`ON CONFLICT … DO NOTHING` + `written == 0`, que entrega o comportamento exigido por AC-02.

## ADR-25 / humanGate D-01 (pedido do dispatch)

| Verificação | Resultado |
| --- | --- |
| `Version=` em algum `PackageReference` de `.csproj` | **0** em 10 csproj (grep em `qa-check-task-0003.ps1` seção 3 e `.sh` twin) — os únicos `Version=` em `.csproj` são comentários (`Squid.Tests.csproj:4`, `Squid.Persistence.csproj:10`) |
| Versões fixadas em `backend/Directory.Packages.props` | `Microsoft.Data.Sqlite 10.0.14`, `Microsoft.Orleans.Core 10.2.2` (mais Test.Sdk 17.14.1, xunit 2.9.3, xunit.runner.visualstudio 3.1.4) — dentro do escopo do humanGate D-01 |
| Escopo do D-01 | nenhum pacote além dos dois autorizados foi acrescentado ao `Directory.Packages.props` |

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `docs/sdd/_tools/sdd-test.ps1 test [-TestFilter …]` | PowerShell 5.1 → `dotnet test` | harness do projeto: gate inteiro ou filtro, grava log em `docs/sdd/runs/test-runs/`, classifica ambiente (exit 3) × produto (exit 1) |
| `docs/sdd/qa/qa-check-task-0003.ps1` | PowerShell 5.1 | 9 verificações independentes: AC-06 positivo + probe que não pode passar errado, ADR-25 (csproj + pinning), GAP-04 nas duas instruções SQL, leftovers do AC-07 |
| `docs/sdd/qa/qa-check-task-0003.sh` | bash (WSL2, `Linux 6.18.54.1-microsoft-standard-WSL2`) | gêmeo `.sh` das mesmas 9 verificações, para a plataforma Linux do projeto |
| `backend/tests/Squid.Tests/Storage/Qa0003EtagFormatTests.cs` | xunit (runner existente) | sonda GAP-04: lê a coluna `Version` do arquivo SQLite e compara com o ETag da API |

Nenhum framework novo, nenhuma dependência nova: só arquivos de teste/script no local de teste já
usado pelo projeto.

## How to reproduce

Prerequisites: Windows com .NET SDK 10 e o repositório clonado (sem rede necessária; `backend/.packages`
é o cache local).

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test
# expected: exit 0, "Aprovado: 27 … Total: 27" (ou 28 com a sonda QA-0003 presente)

powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.Storage"
# expected: exit 0, "Aprovado: 11 … Total: 11" — rode duas vezes seguidas para o AC-07

powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0003.ps1
# expected: exit 0, "summary: all checks behaved as specified"

bash docs/sdd/qa/qa-check-task-0003.sh        # no WSL
# expected: exit 0, mesma linha de resumo
```

Prova de que a suíte pode falhar (temporário — restaurar antes de sair; QA-0003 fez isso e
restaurou por SHA256):

```
# 1) tirar a comparação exata do GAP-04 em SqliteGrainStorage.cs (UPDATE ... AND Version = @Version)
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.Storage"
# expected: exit 1, "Com falha: 1" em SqliteGrainStorageTests.cs:62
# 2) injetar um literal de store em qualquer .cs de backend/src/Squid.Persistence
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~StoreNameLiteralTests"
# expected: exit 1, "Com falha: 1" em StoreNameLiteralTests.cs:44
```

## Last run

| Date/Time | Command | Exit | Output (max 10 lines) |
| --- | --- | --- | --- |
| 2026-11-06 08:39:53 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` | 0 | `Aprovado! – Com falha: 0, Aprovado: 27, Ignorado: 0, Total: 27, Duração: 29 s` · `test: exit 0` (log `20261106-083953-test-gate.log`) |
| 2026-11-06 08:41:56 | `… sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.Storage"` (run A) | 0 | `Aprovado! – Com falha: 0, Aprovado: 11, Ignorado: 0, Total: 11, Duração: 346 ms` (log `20261106-084156-test-filter.log`) |
| 2026-11-06 08:42:05 | mesmo comando (run B, seguida) | 0 | `Aprovado! – Com falha: 0, Aprovado: 11, Ignorado: 0, Total: 11, Duração: 264 ms` (log `20261106-084205-test-filter.log`) |
| 2026-11-06 08:43 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0003.ps1` | 0 | `ok AC-06 positive`, `ok AC-06 can-fail`, `ok ADR-25 (10 csproj)`, `ok ADR-25 pinned ×2`, `ok GAP-04 SQL ×2`, `ok AC-07 leftovers` → `summary: all checks behaved as specified - exit 0` |
| 2026-11-06 08:46:24 | `… sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.Storage"` (com sonda QA) | 0 | `Aprovado! – Com falha: 0, Aprovado: 12, Ignorado: 0, Total: 12, Duração: 349 ms` (log `20261106-084624-test-filter.log`) |
| 2026-11-06 08:47:41 | mesma, com **mutação M1** (`AND Version = @Version` removido do UPDATE) | 1 | `Com falha! – Com falha: 1, Aprovado: 11, … Total: 12` · `at …Write_WithTheSameExpectedETag_SecondThrowsAndTheFirstStateStaysOnDisk() … SqliteGrainStorageTests.cs:line 62` · `test: exit 1 (real test failure)` (log `20261106-084741-test-filter.log`) |
| 2026-11-06 08:49:03 | `… -TestFilter "FullyQualifiedName~StoreNameLiteralTests"`, com **mutação M2** (literal `'actors'` injetado) | 1 | `Com falha! – Com falha: 1, Aprovado: 0, … Total: 1` · `at …StoreNameLiteralTests.SquidPersistenceSourcesDeclareNoStoreNameLiteral() … StoreNameLiteralTests.cs:line 44` (log `20261106-084903-test-filter.log`) |
| 2026-11-06 08:50:28 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (final, após restauração) | 0 | `Aprovado! – Com falha: 0, Aprovado: 28, Ignorado: 0, Total: 28, Duração: 28 s` (log `20261106-085028-test-gate.log`) |
| 2026-11-06 08:52 | `powershell … -File docs/sdd/qa/qa-check-task-0003.ps1` (pós-restauração) | 0 | `summary: all checks behaved as specified - exit 0` · `backups_left=0` |
| 2026-11-06 08:54 | `bash docs/sdd/qa/qa-check-task-0003.sh` (WSL2) | 0 | `Linux 6.18.54.1-microsoft-standard-WSL2 x86_64` · mesmas 9 verificações `ok` → `summary: all checks behaved as specified - exit 0` |

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| 1 | MINOR (limitação, não defeito hoje) | `backend/tests/Squid.Tests/Storage/StoreNameLiteralTests.cs:16` | O detector do AC-06 é a regex `\b(?:actors?\|containers?)\b`: ele cobre exatamente os stores que existem hoje. Não existe no produto um registro de nomes de store do qual a lista possa ser derivada (`git grep -nE '\b(actors\|containers)\b' -- backend/src` → nenhuma ocorrência), então um store novo precisa entrar na lista à mão — enquanto isso um literal dele passaria no AC-06 | `qa-check-task-0003.ps1` (a fixture prova que o scanner detecta o que está na lista); risco: regressão silenciosa do §4.7 #2 no próximo store |

Nenhum BLOCKER, nenhum MAJOR.

## Not covered, and why

- **Suíte xunit em Linux/WSL** — a distro WSL2 da máquina não tem o SDK (`bash -lc 'command -v dotnet'` → `dotnet-absent`); instalar pacote é gate humano e as aprovações estão desabilitadas nesta sessão. Risco: as verificações portáteis (AC-06, ADR-25, GAP-04 estático) passaram em Linux via `qa-check-task-0003.sh`, mas **o comportamento em runtime do `SqliteGrainStorage` ficou `unverified` fora do Windows** — nunca assumir igual.
- **Corrida real entre duas conexões simultâneas** — AC-01 pede duas escritas com o mesmo ETag esperado, o que foi testado de forma sequencial. Duas conexões disputando a mesma linha ao mesmo tempo não foi exercitado; o `UPDATE … WHERE Version = @Version` é atômico no SQLite, mas isso é raciocínio, não medição. Nenhuma AC pede isso.
- **ETag que não seja base64-decimal** (ex.: gravado por outro provedor, GAP-05) — `NextVersion` (`SqliteGrainStorage.cs:191-195`) lança `FormatException` em vez de `InconsistentStateException`. Nenhuma AC cobre; só aparece quando outro provedor compartilhar a mesma linha.
- **`docs/sdd/_tools/sdd-test.sh` (gêmeo `.sh` do gate)** — não executado: ele chamaria `dotnet`, ausente no WSL. O gêmeo `.sh` que executei foi o do meu script de verificação.
