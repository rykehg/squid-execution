<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: VERDE
- owner: qa
- lastVerified: 2026-11-06 00:05:00

# QA-0002 — TASK-0004 migrar Squid.Tests para xunit + Test SDK e fazer `dotnet test` funcionar

- task: TASK-0004
- spec: SPEC-0001
- runId: R-20261104-02
- qaAttempt: 1/2
- verdict: PASSED

## What is proven

Tested only against `## Acceptance criteria` AC-01..AC-07 of the task file. Everything below was
executed in this session on `C:\dev\squid-squad-net\squid-execution` (Windows, PowerShell 5.1,
SDK 10.0.403).

| AC | Scenario | Command | Exit | Result |
| --- | --- | --- | --- | --- |
| AC-01 | gate completo termina sozinho e sai 0 com resumo de aprovados | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` | 0 | PASS — `Aprovado! – Com falha: 0, Aprovado: 16, Ignorado: 0, Total: 16, Duração: 24 s` (log `20261105-235048-test-gate.log`); repetido no fim da sessão: `20261105-235912-test-gate.log`, 27 s, exit 0. Sem pendurar: ambas as execuções terminaram em menos de 30 s (timeout da ferramenta 600 s) |
| AC-02 | os testes de arquitetura existem como fato xunit e cobrem as 4 situações enumeradas | `dotnet test backend/Squid.slnx --list-tests` + sonda de atributos + gate | 0 | PASS — listagem: 8 `BuildRefusesForbiddenEdge` (Domain→Persistence, Grains→Persistence, Persistence→Grains, ReadModels→Grains, Api→Persistence, Protocols→Api, Grains→Service, Domain→Grains) + 5 `BuildAcceptsAllowedEdge` (Grains→Domain, Api→ReadModels, Api→Grains, Service→Persistence, Service→Worker) + `DomainDoesNotReferenceOrleansOrJsonLd`, `DomainDoesNotReferenceInfrastructureProjects`, `DomainProjectTargetsNet10` = 16 casos, todos executados e aprovados no gate. Sonda: `TheoryAttribute <- FactAttribute <- Attribute` nos 2 métodos com Theory, `FactAttribute <- Attribute` nos 3 Fact — todos são fato xunit |
| AC-03 | `dotnet test --filter` roda só o teste nomeado e o resumo mostra o total filtrado | `powershell ... sdd-test.ps1 test backend/Squid.slnx -TestFilter FullyQualifiedName~DomainProjectTargetsNet10` | 0 | PASS — `Aprovado! – Com falha: 0, Aprovado: 1, Ignorado: 0, Total: 1, Duração: 22 ms` (log `20261105-235107-test-filter.log`) |
| AC-04 | nenhum `.csproj` carrega `Version=`; versões só em `Directory.Packages.props` | `git grep -n -E PackageReference.*Version -- *.csproj` e `git grep -n PackageVersion Include -- backend/Directory.Packages.props` | 1 e 0 | PASS — nenhum PackageReference com Version (grep literal `Version=` acha só o comentário do `Squid.Tests.csproj:4`); 3 `PackageVersion`: Microsoft.NET.Test.Sdk 17.14.1, xunit 2.9.3, xunit.runner.visualstudio 3.1.4 (ADR-25 preservada) |
| AC-05 | falha de teste real produz `Failed: 1` e exit ≠ 0, revertida antes do fim | `sdd-test.ps1 test backend/Squid.slnx -TestFilter FullyQualifiedName~DomainProjectTargetsNet10` com asserção falsa injetada, depois `git checkout --` | 1 | PASS — `Com falha! – Com falha: 1, Aprovado: 0, ... Total: 1`; cabeçalho do log `20261105-235200-test-filter.log`: `command: dotnet test ... --filter ...` / `exit: 1` (exit bruto do dotnet); mensagem `Expected: "net9.9-QA0002-DELIBERATE-BREAK" / Actual: "net10.0"` em `DomainHasNoInfrastructureTests.cs:70`. Revertido: `git status --porcelain` vazio, `git diff --exit-code` 0, nenhuma ocorrência de `QA0002`/`net9.9` no arquivo; gate final verde |
| AC-06 | runner de reflexão removido, não mantido em paralelo | `git grep -n -e TestRunner -e ISelfCheckingTest -e ITestClass -e RunAll -- backend/tests` e `git ls-files backend/tests` | 1 e 0 | PASS — nenhuma ocorrência; rastreados apenas `Squid.Tests.csproj`, `Architecture/BuildProbe.cs`, `Architecture/DependencyRuleTests.cs`, `Architecture/DomainHasNoInfrastructureTests.cs`; `Framework/`, `Framework/TestFramework.cs`, `Framework/Assert.cs` e `Program.cs` não existem |
| AC-07 | `sdd-test.ps1 test` roda `dotnet test` (não o executável do runner) e classifica falha de ambiente como exit 3 | gate + execução com `TEMP` negado | 0 e 3 | PASS — (a) todo gate grava `command: dotnet test ...` no cabeçalho do log e a fonte chama `& dotnet @dotnetArgs` com `test` (`sdd-test.ps1:167`,`:179`); não existe executável de runner (AC-06). (b) `TEMP`/`TMP` apontados para `C:\Windows\System32\qa0002-temp` (escrita negada) → `MSB1025` + `System.UnauthorizedAccessException: Access to the path ... is denied` → `environment: this is NOT a failing test. Do not write it into the task as a defect.` → **exit 3** (log `20261105-235452-test-gate.log`) |

## Scripts

No new test script was added to the repository: the project's own runner (`sdd-test.ps1 test`) is the
harness, and the suite itself is the test. One throwaway probe was written outside the repository and
deleted after the run.

| Script | Language / runner | What it does |
| --- | --- | --- |
| `docs/sdd/_tools/sdd-test.ps1 test` | pwsh (PS 5.1) | gate: `dotnet test backend/Squid.slnx -c Debug -m:1`, grava log em `docs/sdd/runs/test-runs/`, classifica ambiente (exit 3) × produto (exit 1) |
| `docs/sdd/_tools/sdd-test.sh test` (gêmeo `.sh`) | bash (Git Bash) | mesmo contrato em sh; executado também nesta sessão (ver findings) |
| `C:\Users\Ryke\AppData\Local\Temp\qa0002-probe\` (removido) | dotnet (net10.0) | imprime do `Squid.Tests.dll` real o atributo de cada método de teste e a hierarquia (`TheoryAttribute <- FactAttribute`) |

## How to reproduce

Prerequisites: Windows, .NET SDK 10, repositório clonado com `backend/.packages` preenchido (GAP-02).

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test
# expected: exit 0, "Aprovado! ... Aprovado: 16 ... Total: 16", log novo em docs/sdd/runs/test-runs/

powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test backend/Squid.slnx -TestFilter "FullyQualifiedName~DomainProjectTargetsNet10"
# expected: exit 0, "Aprovado: 1 ... Total: 1"
```

Falha real (AC-05) — temporário, reverter antes de sair:

```
# trocar, em backend/tests/Squid.Tests/Architecture/DomainHasNoInfrastructureTests.cs:70, ExpectedTargetFramework por "net9.9-TESTE"
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test backend/Squid.slnx -TestFilter "FullyQualifiedName~DomainProjectTargetsNet10"
# expected: exit 1 e "Com falha: 1"
git checkout -- backend/tests/Squid.Tests/Architecture/DomainHasNoInfrastructureTests.cs   # expected: git status limpo
```

## Last run

| # | Command | Exit | Output (tail) |
| --- | --- | --- | --- |
| 1 | `git grep -n -E PackageReference.*Version -- *.csproj` | 1 | nenhuma ocorrência (AC-04) |
| 2 | `git grep -n PackageVersion Include -- backend/Directory.Packages.props` | 0 | `Microsoft.NET.Test.Sdk 17.14.1` · `xunit 2.9.3` · `xunit.runner.visualstudio 3.1.4` |
| 3 | `git grep -n -e TestRunner -e ISelfCheckingTest -e ITestClass -e RunAll -- backend/tests` | 1 | nenhuma ocorrência (AC-06) |
| 4 | `git ls-files backend/tests` | 0 | 4 arquivos: csproj + Architecture/{BuildProbe,DependencyRuleTests,DomainHasNoInfrastructureTests} |
| 5 | `dotnet test backend/Squid.slnx --list-tests` | 0 | 16 casos listados: 8 proibidas, 5 legais, 3 do domínio |
| 6 | `powershell ... sdd-test.ps1 test` (gate) | 0 | `Aprovado! – Com falha: 0, Aprovado: 16, Ignorado: 0, Total: 16, Duração: 24 s` |
| 7 | `powershell ... sdd-test.ps1 test backend/Squid.slnx -TestFilter FullyQualifiedName~DomainProjectTargetsNet10` | 0 | `Aprovado: 1 ... Total: 1, Duração: 22 ms` |
| 8 | mesmo comando com asserção falsa injetada (AC-05) | 1 | `Com falha! – Com falha: 1, Aprovado: 0, ... Total: 1`; log `20261105-235200-test-filter.log` grava `exit: 1` do dotnet |
| 9 | `git checkout -- ...DomainHasNoInfrastructureTests.cs` + `git status --porcelain` + `git diff --exit-code` | 0 | árvore limpa, sem resíduo da injeção |
| 10 | `dotnet run --project <TEMP>\Probe.csproj -- Squid.Tests.dll` | 0 | `TheoryAttribute <- FactAttribute <- Attribute` (2 métodos) · `FactAttribute <- Attribute` (3 métodos) |
| 11 | `powershell ... sdd-test.ps1 test backend/Squid.slnx` com `TEMP`/`TMP` = `C:\Windows\System32\qa0002-temp` | 3 | `MSBUILD : error MSB1025` + `System.UnauthorizedAccessException: Access to the path ... is denied` → `environment: this is NOT a failing test` → exit 3 |
| 12 | `powershell ... sdd-test.ps1 test` (gate final, pós-revert) | 0 | `Aprovado: 16 ... Total: 16, Duração: 27 s` (log `20261105-235912-test-gate.log`) |
| 13 | `bash docs/sdd/_tools/sdd-test.sh probe` | 0 | `dotnet: 10.0.403`, gêmeo `.sh` executa |
| 14 | `bash docs/sdd/_tools/sdd-test.sh test` (sem target) | 1 | stderr `sdd-test: no *.sln found within depth 3; pass the path` + `MSB1003` rotulado "real test failure" (finding 1) |
| 15 | `bash docs/sdd/_tools/sdd-test.sh test backend/Squid.slnx` | 0 | `Aprovado: 16 ... Total: 16` — gêmeo `.sh` roda a suíte (sob Git Bash/Windows) |

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| 1 | MAJOR (fora dos ACs de TASK-0004) | `docs/sdd/_tools/sdd-test.sh:20` (e `:25`) | `sdd-test.sh test` sem target procura só `*.sln`, mas a solução do repo é `backend/Squid.slnx`; `find_sln` sai com 1 dentro de substituição de comando, `$sln` fica vazio e o script segue → `MSB1003` e exit 1 rotulado "real test failure". O gate Linux nasce quebrado e com diagnóstico enganoso (BACKLOG I-10) | linha 14 da tabela acima: exit 1, stderr `no *.sln found within depth 3` |
| 2 | MINOR (fora dos ACs) | `docs/sdd/_tools/sdd-test.sh:64` | o gêmeo `.sh` roda `dotnet test --no-build` (o `.ps1` abandonou `--no-build` porque falharia em clone limpo — log da task 2026-11-04 15:44:21), não grava log em `docs/sdd/runs/test-runs/` e não classifica NU1301/rede; o comentário `:3` promete "same contract and exit codes" | leitura do script × execução 13/15 |
| 3 | MINOR | `docs/sdd/_tools/sdd-test.ps1:188` | na falha de ambiente real (TEMP negado) a mensagem escolhida foi a assinatura do testhost ("testhost cannot open the parent process handle") porque o stack contém a palavra `handle`; o exit 3 está correto, mas o motivo impresso aponta para o sintoma errado | log `20261105-235452-test-gate.log`: `handle` 2×, `testhost` 0×, `UnauthorizedAccessException` 2× |
| 4 | MINOR (leitura do AC) | `backend/tests/Squid.Tests/Architecture/DependencyRuleTests.cs:35` e `:63` | AC-02 diz "4 testes ... como `[Fact]`"; o suite tem 5 métodos (2 `[Theory]` + 3 `[Fact]`) reportados como 16 casos. A sonda prova `TheoryAttribute <- FactAttribute`, ou seja, todos são fatos xunit, e todos os comportamentos enumerados estão cobertos — nada quebra, a contagem/atributo literal do AC é que ficou para trás da implementação | sonda da linha 10 da tabela de execuções |

Nenhum BLOCKER. Nenhuma falha pré-existente encontrada (`preExistingFailures: NENHUM`): todos os
runs de suíte desta sessão saíram verdes fora da injeção deliberada do AC-05.

## Not covered, and why

- **Gate em Linux/WSL (o gêmeo `.sh` em plataforma Linux)** — a única distro WSL desta máquina é
  `podman-machine-default` e não tem `.NET SDK` nem `pwsh` (`DOTNET_ABSENT`, `PWSH_ABSENT`);
  instalar exigiria gate. O `.sh` foi executado só sob Git Bash no Windows (linhas 13-15).
  Risco: em Linux podem diferir line endings, caminhos e o comportamento do MSBuild/VSTest.
- **Instalação de qualquer coisa** (SDK, container, imagem) — gate não solicitado; `docker` ausente,
  `podman` presente mas sem daemon configurado para o teste.
- **Nada fora de AC-01..AC-07** foi exigido desta rodada; os findings 1 e 2 ficam para ODEV
  (provável nova task/backlog, já que `sdd-test.sh` não está no `touches` da TASK-0004).
