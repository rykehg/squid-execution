<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: VERIFICADO
- owner: qa
- lastVerified: 2026-11-04 12:06:37

# QA-0001 — Fundação do repositório: git, `.gitignore`, estrutura de pastas

- task: TASK-0001
- spec: SPEC-0001
- runId: R-20261104-01
- verdict: PASSED

## What is proven

| AC | Scenario | Script | Result |
| --- | --- | --- | --- |
| AC-01 | 8 probes criados (`*.db`, `*.db-wal`, `*.db-shm`, `.env`, `bin/` aninhado, `obj/` aninhado, `node_modules/` aninhado, `ideias-references/lib/notes.txt`) — todos `git check-ignore` exit 0 e nenhum aparece como `??`; probes removidos no `finally` | `docs/sdd/qa/qa-check-task-0001.ps1` | PASS |
| AC-02 | `git add --dry-run` lista exatamente 3 `.gitkeep`; `backend/`, `web/`, `infra/` contêm apenas `.gitkeep` (0 arquivos de produto) | `docs/sdd/qa/qa-check-task-0001.ps1` | PASS |
| AC-03 | 15 extensões binárias → `text: unset`; `x.png` → `text: unset`; `x.md` → `eol: lf`; `* text=auto eol=lf` é a primeira regra | `docs/sdd/qa/qa-check-task-0001.ps1` | PASS |
| AC-04 | README com 2426 chars, sem caminho absoluto Windows/Unix, sem `../`, sem `squid-specs`; os 8 caminhos citados existem dentro do repo | `docs/sdd/qa/qa-check-task-0001.ps1` | PASS |
| AC-05 | `check-task-0001.ps1` 2x: exit 0 e exit 0, `git status` idêntico antes/depois de cada run | `docs/sdd/_tools/check-task-0001.ps1` (executado pelo QA) + `docs/sdd/qa/qa-check-task-0001.ps1` | PASS |

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `docs/sdd/qa/qa-check-task-0001.ps1` | PowerShell 5.1 (`powershell -File`) | Verifica AC-01..AC-05 sem depender do script do DEV; probes sob `_qa-probe-*` + `ideias-references/`, removidos num bloco `finally` |
| `docs/sdd/_tools/check-task-0001.ps1` | PowerShell 5.1 (script do DEV, existente) | Verificação de referência do próprio entrega; executado 2x isolado |

## How to reproduce

Prerequisites: Windows com `git` 2.56 no PATH e PowerShell 5.1 (não há `pwsh` nesta máquina — por isso os scripts são chamados com `powershell -File`).

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs\sdd\qa\qa-check-task-0001.ps1          # expected: "0 falha(s)." e exit 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs\sdd\_tools\check-task-0001.ps1          # expected: "8 probes limpos; 0 falha(s)." e exit 0 (rodar 2x)
git status --porcelain --ignored                                                                # expected: sem linha _qa-probe / ideias-references após o run
```

## Last run

| Date/Time | Command | Exit | Output (max 10 lines) |
| --- | --- | --- | --- |
| 2026-11-04 12:06:37 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs\sdd\qa\qa-check-task-0001.ps1` | 0 | ```probes vistos: 8; fora do esperado: 0/PASS AC-01/PASS AC-02/PASS AC-03/PASS AC-04/PASS AC-05/resultado: AC-01=PASS AC-02=PASS AC-03=PASS AC-04=PASS AC-05=PASS/0 falha(s).``` |
| 2026-11-04 12:06:37 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs\sdd\_tools\check-task-0001.ps1` (run 1) | 0 | ```PASS AC-01 ignores / PASS AC-02 pastas reservadas / PASS AC-03 gitattributes / PASS AC-04 README sem caminho externo / 8 probes limpos; 0 falha(s).``` |
| 2026-11-04 12:06:37 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs\sdd\_tools\check-task-0001.ps1` (run 2) | 0 | ```mesma saída da run 1; git status idêntico ao baseline``` |

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| 1 | MINOR | `README.md:41` | README pede "PowerShell 7" como pré-requisito, mas `pwsh` não existe nesta máquina (só PowerShell 5.1) e o próprio README usa `powershell -File` na linha 46. Seguir o README como está induz à instalação de um runtime que o projeto não usa. | `Get-Command pwsh` → não encontrado; `$PSVersionTable.PSVersion` → `5.1.26100.9711` |
| 2 | MINOR | `backend/.gitkeep:1`, `web/.gitkeep:1`, `infra/.gitkeep:1` | Os 3 `.gitkeep` são CRLF com BOM (`EF BB BF 0D 0A`, 5 bytes). O `.gitattributes` aplica `* text=auto eol=lf`, então o blob no índice será convertido para LF — o arquivo que o `git status` mostra nunca será igual ao blob. Funciona (o BOM não impede o staging: `git add --dry-run` lista os 3), mas é sujeira binária em arquivo que deveria ser texto vazio por convenção. | hexdump dos 3 arquivos; `git add --dry-run backend/.gitkeep web/.gitkeep infra/.gitkeep` lista 3 |

Nenhum BLOCKER e nenhum MAJOR. Os dois achados são de consistência de documentação/convenção e não quebram nenhum AC.

### Concorrência observada — TASK-0002 escrevendo em `backend/` durante esta verificação

Não é um achado contra TASK-0001, e o QA não tocou nesses arquivos.

- `docs/sdd/tasks/TASK-0002-solution-e-regra-de-dependencia.md` está com `status: IN_DEVELOPMENT` e `nextAction: "DEV em andamento: solution com 10 projetos e regra de dependencia como erro de build"`.
- Durante esta sessão, um DEV concorrente criou `backend/Directory.Build.props` (12:12:33), `backend/Directory.Build.targets` (12:13:50), `Directory.Packages.props` e `backend/src/Squid.*/*.csproj` + `backend/tests/Squid.Tests/Squid.Tests.csproj` (12:13:24), além de um build do `Squid.Domain` em `bin/`/`obj/` (12:13:31–12:13:35).
- Consequência para o AC-02: o critério foi verificado e passa com a árvore de TASK-0001 (só `.gitkeep` nas 3 pastas). Depois disso, `backend/` deixou de estar vazio porque TASK-0002 começou a escrever — exatamente o que o AC-02 espera na sequência. Re-executado agora, o cenário AC-02 reporta esses arquivos como "código de produto" e falha; isso é o AC-02 vencendo, não uma regressão de TASK-0001.
- Verificação colateral que importa: os artefatos de build do DEV concorrente **são** ignorados — `git check-ignore backend/src/Squid.Domain/bin/Debug/net10.0/Squid.Domain.dll` → `.gitignore:2:bin/` e `.../obj/project.assets.json` → `.gitignore:3:obj/`. `git status --ignored` mostra `!! backend/src/Squid.Domain/bin/` e `!! .../obj/`. Ou seja, o `.gitignore` está funcionando no código real, não só nos probes.
- O repositório ainda não tem commit (`git rev-parse HEAD` → `fatal: Needed a single revision`), coerente com QA/deploy feito antes do primeiro `git add`: todo o conteúdo aparece como `??` até o primeiro commit.

## Not covered, and why

- **`dotnet build backend/Squid.slnx` / `dotnet test` executados de fato** — o `dotnet` 10.0.403 falha antes de qualquer build nesta máquina, dentro do sandbox: `System.UnauthorizedAccessException: Access to the path 'C:\Users\Ryke\.dotnet' is denied` e `Win32Exception (5): Acesso negado` em `GetParentProcessId`. Reproduzido com um projeto trivial (`net10.0`) fora do repo, então não é defeito do produto — é falha de ambiente. Além disso a solution `backend/Squid.slnx` ainda não existe (nasce em TASK-0002), o que o AC-04 admite ("ainda que o comando seja `dotnet build` a partir da P-01"). Risco: o comando do README só será provado quando TASK-0002 criar a solution; se estiver errado, ninguém descobre agora.
- **Round-trip real do índice (BOM→LF do `.gitkeep`)** — exigiria `git add`, que esta sessão de QA não executa. Prova limitada a bytes em disco + `--dry-run`. Risco: baixo; o conteúdo dos `.gitkeep` é irrelevante por convenção.
- **`.gitkeep` de `bin/` sob ignore** — `git add --dry-run` de um path explícito **não respeita** as regras de ignore (medido: `add` em `.gitkeep` sob `bin/` retorna 0). Por isso ele não serve como controle negativo; o controle de ignore usado é `git check-ignore`. Risco: nenhum para o produto, mas quem confiar em `add --dry-run` para concluir "não está ignorado" vai errar.
- **Portabilidade Linux/container** — o projeto também roda em Linux (Podman/WSL presentes), mas não há twin `.sh` nem execução fora do Windows nesta entrega. AC-01..AC-05 são sobre estado do git, não sobre comportamento de runtime. Risco: `eol` e ignore podem divergir em CI Linux; o `.gitattributes` correto mitiga.
- **`.editorconfig`** está em `touches` mas nenhum AC o cobre; não foi avaliado além de existir.