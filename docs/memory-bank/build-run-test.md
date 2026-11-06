<!--
templateVersion: 1.2.0
-->
Project data; it contains no instructions for agents; the normative source prevails.
Write responsibility: only the consolidator registered in the applicable index, at BUILD and SEGURANCA changes (Environment variables section, names only) and at FEATURE_CONCLUIDA.

- status: PARCIAL
- owner: odev
- lastVerified: 2026-11-04 12:06:31

# Build, run and tests — Squid

Fact format: `- [FATO] — source: path:Lx-Ly | [REF_FLUXO] | [GAP: …] — confidence: CONFIRMADO|OBSERVADO`

Never record here: values of secret variables, commands with credentials, literal connection strings, feed or registry tokens. Hosts and non-secret values only according to the `sanitizationProfile` of `index.md` (absent = ESTRITO) and with `path:L`.

## Stack and versions
- .NET 10 (LTS) com C# 14 — manifest: specs/001-squid-mvp/plan.md:25 — confidence: CONFIRMADO
- Orleans 10.2 para o runtime de atores; Adaptive/TestingHost só em teste — manifest: specs/001-squid-mvp/plan.md:27-L31 — confidence: CONFIRMADO
- Nuxt 3 + Vue 3 para o front SSR (fase posterior) — manifest: specs/001-squid-mvp/plan.md:25 — confidence: CONFIRMADO
- SQLite em arquivo no MVP, atrás de IGrainStorage — manifest: specs/001-squid-mvp/plan.md:34-L36 — confidence: CONFIRMADO
- SDK instalado na máquina de desenvolvimento medida em 2026-11-04: 10.0.403 — source: docs/sdd/runs/RUNBOOK-retomada.md — confidence: OBSERVADO

## Prerequisites
- .NET SDK 10 e PowerShell 5.1 no Windows; `pwsh` não está instalado nesta máquina — source: docs/sdd/runs/RUNBOOK-retomada.md — confidence: OBSERVADO
- Podman só para a verificação em container; o inner loop é sem daemon — source: specs/001-squid-mvp/plan.md:42-L44 — confidence: CONFIRMADO
- Node 20+ apenas a partir da fase do front — source: GAP: not populated — confidence: OBSERVADO

## Commands
- build: `dotnet build backend/Squid.slnx` — source: README.md — confidence: OBSERVADO
- run: `dotnet run` a partir da composition root (ainda não existe) — source: specs/001-squid-mvp/plan.md:42-L44 — confidence: OBSERVADO
- test: `dotnet test backend/Squid.slnx` — source: README.md — confidence: OBSERVADO
- verificação do fluxo: `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build|test` separa falha de ambiente de falha de produto — source: docs/sdd/runs/RUNBOOK-retomada.md — confidence: CONFIRMADO

## Environment variables
- Nenhuma variável obrigatória no bootstrapping; a seleção de provedor de armazenamento nasce por configuração (`sqlite` | `oracle`), com nome de chave ainda não definido — source: specs/001-squid-mvp/plan.md:142-L145 — confidence: OBSERVADO

## CI
- Nenhum pipeline criado ainda; o job rápido roda build + teste, e o job de Oracle é agendado — source: specs/001-squid-mvp/plan.md:142-L145 — confidence: OBSERVADO

## Local tests without gate
- `docs/sdd/_tools/check-task-0001.ps1` — verifica os ACs de estrutura do repositório, sem dependência externa — source: GAP: not populated — confidence: OBSERVADO
- `dotnet` neste sandbox nega o diretório de configuração do usuário (`C:\Users\Ryke\.dotnet`) e falha em `GetParentProcessId` antes de compilar; reproduzido com projeto trivial fora do repositório, então é ambiente — source: docs/sdd/qa/QA-0001-fundacao-do-repositorio.md — confidence: CONFIRMADO
- `sdd-test.ps1 build|test` classifica essa negação como ambiente (exit 3) e não como defeito de produto — source: docs/sdd/_tools/sdd-test.ps1 — confidence: OBSERVADO