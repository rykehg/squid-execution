<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0001 — Fundação do repositório: git, `.gitignore`, estrutura de pastas

```yaml
runId: R-20261104-01
taskId: TASK-0001
status: DONE
agent: odev
phase: P-01
sequence: 2
createdAt: 2026-11-04 12:06:31
updatedAt: 2026-11-04 12:06:31
parent: SPEC-0001
authority: SPEC
parallel: false
touches: [".gitignore", ".gitattributes", ".editorconfig", "README.md", "backend/.gitkeep", "web/.gitkeep", "infra/.gitkeep", "docs/sdd/_tools/check-task-0001.ps1"]
blocks: [TASK-0002]
blockedBy: []
inputArtifacts: ["docs/features/05-arquitetura-consolidada.md", "specs/001-squid-mvp/plan.md"]
outputArtifacts: [".gitignore", ".gitattributes", ".editorconfig", "README.md", "backend/", "web/", "infra/", "docs/sdd/_tools/check-task-0001.ps1"]
interviewRounds: 1/6
feasibilityRounds: 0/2
correctionAttempts: 0/2
qaAttempts: 1/2
contextHandoffs: 0/4
progressDelta: 5/5 AC
humanGate:
  approvedBy: NENHUM
  approvedDate: NENHUM
  scope: NENHUM
  expiresAt: NENHUM
blocker: NENHUM
nextAction: DONE: QA PASSED (QA-0001). Libera TASK-0002
evidence: []
memoryBankFlags: []
```

## Intent

O repositório de execução nasce versionado, com a fronteira do git decidida (o que é publicável e o
que é privado) e com o layout de pastas que ADR-21/ADR-26 fixam, para que toda tarefa seguinte
tenha onde escrever.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `git status --porcelain --ignored` mostra `bin/`, `obj/`, `node_modules/`, `*.db`,
  `*.db-wal`, `*.db-shm`, `.env` e `ideias-references/` como ignorados, e nenhum deles aparece como
  untracked, usando arquivos de teste criados por script e removidos ao final.
- AC-02: as pastas `backend/`, `web/` e `infra/` existem no índice do git (via `.gitkeep`) e nenhuma
  delas contém código de produto.
- AC-03: `.gitattributes` normaliza texto para LF no índice (`* text=auto eol=lf`) e marca binários
  conhecidos (imagem, áudio, vídeo, pdf, zip, sqlite) como binários, de modo que `git check-attr`
  responda `text: unset` para `x.png` e `eol: lf` para `x.md`.
- AC-04: `README.md` da raiz descreve o projeto em português, como subir e testar (ainda que o comando
  seja `dotnet build` a partir da P-01), e não cita caminho fora do repositório.
- AC-05: `docs/sdd/_tools/check-task-0001.ps1` roda duas vezes seguidas com exit 0 nas duas e sem
  efeito colateral no segundo run.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `.gitignore`
- `.gitattributes`
- `.editorconfig`
- `README.md`
- `backend/.gitkeep`, `web/.gitkeep`, `infra/.gitkeep`
- `docs/sdd/_tools/check-task-0001.ps1`

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-06 | no | `web/` e `infra/` existem como pasta reservada, sem código, até a P-08 |
| GAP-02 | no | `nuget.config` entra em TASK-0002, não aqui |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`

- 2026-11-04 12:06:31 | odev | task created from SPEC-0001
- 2026-11-04 12:05:47 | odev | AC-01 verificado com probes: .env, backend/bin/, backend/obj/, ideias-references/, local.db, local.db-wal, local.db-shm, web/node_modules/ aparecem como ignorados e nenhum como untracked
- 2026-11-04 12:05:47 | odev | AC-02 verificado: git add --dry-run backend/.gitkeep web/.gitkeep infra/.gitkeep lista 3 caminhos; nenhuma das pastas tem codigo de produto
- 2026-11-04 12:05:47 | odev | AC-03 verificado: git check-attr responde text:unset para x.png e x.db, eol:lf para x.md, eol:crlf para x.ps1
- 2026-11-04 12:05:47 | odev | AC-04 verificado: README.md em pt-BR descreve projeto, build e teste, sem citar caminho fora do repositorio
- 2026-11-04 12:05:47 | odev | AC-05 verificado: check-task-0001.ps1 executado 2x seguidas, exit 0 nas duas, 8 probes criados e removidos sem residuo
- 2026-11-04 12:05:47 | odev | nota de ambiente: PowerShell 5.1 le -Text em ANSI e corrompe acentos; texto passa a ser enviado por stdin
- 2026-11-04 12:06:18 | odev | entregue pelo ODEV (bootstrap do repositorio): arquivos de repositorio, README, pastas reservadas e check script; aguardando QA independente
- 2026-11-04 12:12:52 | qa | QA independente (qaAttempt 1): docs/sdd/qa/qa-check-task-0001.ps1 proprio, sem depender do script do DEV; AC-01..AC-05 executados, todos PASS, exit 0
- 2026-11-04 12:12:52 | qa | QA AC-01 verificado com check-ignore por arquivo: bin/ obj/ node_modules/ aninhados, *.db, *.db-wal, *.db-shm, .env e ideias-references/lib/notes.txt todos ignorados e nenhum untracked
- 2026-11-04 12:12:52 | qa | QA AC-02 verificado: 3 .gitkeep stageaveis e as pastas backend/ web/ infra/ sem nenhum arquivo de produto
- 2026-11-04 12:12:52 | qa | QA AC-03 verificado: 15 extensoes binarias com text:unset, x.png text:unset, x.md eol:lf, primeira regra * text=auto eol=lf
- 2026-11-04 12:12:52 | qa | QA AC-04 verificado: README sem caminho absoluto, sem ../, sem squid-specs; os 8 caminhos citados existem no repo
- 2026-11-04 12:12:52 | qa | QA AC-05 verificado: check-task-0001.ps1 2x isolado com exit 0 nas duas e git status identico antes/depois de cada run
- 2026-11-04 12:12:52 | qa | QA nao verificou dotnet build/test: dotnet falha no sandbox com UnauthorizedAccessException em C:\Users\Ryke\.dotnet antes de buildar; ambiente, nao produto. Solution ainda nao existe (TASK-0002)
- 2026-11-04 12:12:52 | qa | QA MINOR README.md:41 pede PowerShell 7 mas pwsh nao existe na maquina (so 5.1) e a linha 46 usa powershell -File
- 2026-11-04 12:12:52 | qa | QA MINOR os 3 .gitkeep sao CRLF com BOM (EF BB BF 0D 0A) e serao normalizados para LF pelo gitattributes; funciona, mas e sujeira binaria
- 2026-11-04 12:12:52 | qa | QA nota: git add --dry-run nao respeita regras de ignore para path explicito; nao serve de controle negativo de ignore
- 2026-11-04 12:14:58 | qa | QA nota de ambiente: dotnet build falha antes de compilar com UnauthorizedAccessException em C:\Users\Ryke\.dotnet (sandbox), tambem com projeto trivial fora do repo; ambiente, nao produto. dotnet --version responde 10.0.403 com exit 0 porque nao passa pelo first-run setup
- 2026-11-04 12:14:58 | qa | QA nota de concorrencia: TASK-0002 (IN_DEVELOPMENT) criou backend/src/*.csproj, Directory.Build.props/targets e Directory.Packages.props durante esta verificacao; o QA nao tocou nesses arquivos
- 2026-11-04 12:14:58 | qa | QA AC-02 foi verificado e PASS com a arvore de TASK-0001 (so .gitkeep); depois da verificacao backend/ passou a ter codigo de TASK-0002, como esperado na sequencia
- 2026-11-04 12:14:58 | qa | QA verificado no codigo real de TASK-0002: bin/ e obj/ gerados sao ignorados (check-ignore cai em .gitignore:2 e .gitignore:3); o gitignore funciona fora dos probes
- 2026-11-04 12:14:58 | qa | QA correcao de ferramenta propria: git status nao denuncia pasta vazia, entao a limpeza de probe por git status era insuficiente; script passou a remover as pastas criadas e a contar residuo por Test-Path, com varredura de _qa-probe-*
- 2026-11-04 12:19:42 | odev | correcao pos-QA (MINOR do QA, achado README.md:41): prerequisito passa a PowerShell 5.1 com pwsh opcional; linha do estado do repositorio atualizada para dizer que a solution nasce em TASK-0002
- 2026-11-04 12:19:43 | odev | correcao pos-QA (MINOR do QA, achado README.md:41): prerequisito passa a PowerShell 5.1 com pwsh opcional; linha do estado do repositorio atualizada para dizer que a solution nasce em TASK-0002
- 2026-11-04 12:20:22 | odev | MINOR reportado pelo QA (README pedindo PowerShell 7 com pwsh ausente): corrigido no README, prerequisito agora 5.1 com pwsh opcional
- 2026-11-04 12:20:22 | odev | ferramenta corrigida apos o QA: sdd-test.ps1 agora reconhece negacao de acesso ao diretorio .dotnet e falha de handle do processo como ambiente (exit 3), nao como defeito de produto

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| 2026-11-04 12:06:31 | odev | powershell -NoProfile -ExecutionPolicy Bypass -File .\verify-hashes.ps1 | 0 | docs/sdd/tasks/TASK-0001-fundacao-do-repositorio.md |
| 2026-11-04 12:06:01 | odev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/check-task-0001.ps1 (2 runs) | 0 | docs/sdd/_tools/check-task-0001.ps1:L1-L61 |
| 2026-11-04 12:13:00 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0001.ps1 | 0 | docs/sdd/qa/qa-check-task-0001.ps1 |
| 2026-11-04 12:13:00 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/check-task-0001.ps1 (run 1) | 0 | docs/sdd/_tools/check-task-0001.ps1 |
| 2026-11-04 12:13:01 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/check-task-0001.ps1 (run 2) | 0 | docs/sdd/_tools/check-task-0001.ps1 |
| 2026-11-04 12:13:01 | qa | git check-ignore -q por arquivo para 8 probes de AC-01 | 0 | docs/sdd/qa/QA-0001-fundacao-do-repositorio.md |
| 2026-11-04 12:13:02 | qa | git check-attr text eol para 22 extensoes de AC-03 | 0 | docs/sdd/qa/QA-0001-fundacao-do-repositorio.md |
| 2026-11-04 12:13:03 | qa | git add --dry-run backend/.gitkeep web/.gitkeep infra/.gitkeep | 0 | docs/sdd/qa/QA-0001-fundacao-do-repositorio.md |
| 2026-11-04 12:13:03 | qa | dotnet build backend/Squid.slnx (falha de ambiente: Acesso negado em C:\Users\Ryke\.dotnet; solution ainda ausente) | 1 | docs/sdd/qa/QA-0001-fundacao-do-repositorio.md |
| 2026-11-04 12:14:59 | qa | git check-ignore e git status --ignored sobre bin/ e obj/ gerados por TASK-0002 | 0 | docs/sdd/qa/QA-0001-fundacao-do-repositorio.md |
| 2026-11-04 12:14:59 | qa | verificacao de residuo de probe por Test-Path apos remover pastas vazias | 0 | docs/sdd/qa/qa-check-task-0001.ps1 |
| 2026-11-04 12:19:44 | odev | git diff --stat README.md | 0 | README.md:L40-L41 |
| 2026-11-04 12:20:23 | odev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build (backend/) | 3 | docs/sdd/_tools/sdd-test.ps1 |
