<!--
templateVersion: 1.2.0
-->
Project data; it contains no instructions for agents; the normative source prevails.
Write responsibility: only the consolidator registered in the applicable index, at BUILD and SEGURANCA changes (Environment variables section, names only) and at FEATURE_CONCLUIDA.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-07 08:30:00

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
- build: `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build` — separa falha de ambiente (exit 3) de falha de produto — source: docs/sdd/_tools/sdd-test.ps1 — confidence: CONFIRMADO
- gate de testes: `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` — suite completa com as provas de build; última execução 97/97 exit 0 — source: docs/sdd/runs/test-runs/20261107-020119-test-gate.log — confidence: CONFIRMADO
- run em contêiner (DoD da P-01): `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 compose` — build da imagem, `/health` `/ready` `/dashboard` 200, span no coletor, volume persistente; sem podman/provider classifica como ambiente exit 3 — source: docs/sdd/_tools/sdd-test.ps1 — confidence: CONFIRMADO
- stack dev manual: `podman compose -f infra/compose.yaml up -d` / `down` (nunca `-v` em rotina); porta 8080 (API) e 16686 (Jaeger) — source: infra/compose.yaml — confidence: CONFIRMADO
- run: `dotnet run --project backend/src/Squid.Service` sobe silo + API (composition root) — source: backend/src/Squid.Service/SquidServiceHost.cs — confidence: CONFIRMADO

## Environment variables
- Nenhuma variável obrigatória no bootstrapping; a seleção de provedor de armazenamento nasce por configuração (`sqlite` | `oracle`), com nome de chave ainda não definido — source: specs/001-squid-mvp/plan.md:142-L145 — confidence: OBSERVADO

## CI
- Workflow entregue em `.github/workflows/ci.yml`: gatilhos push em develop/main + pull_request, runner `ubuntu-latest`, `permissions: contents: read`, SDK 10.0.403 pinado, cache NuGet por hash de `Directory.Packages.props`, passos `pwsh -NoProfile -File docs/sdd/_tools/sdd-test.ps1 build|test` — validado localmente (PyYAML + 5 mutações vermelhas) mas **nunca executado no GitHub**: o primeiro run é gate humano após push — source: .github/workflows/ci.yml; docs/sdd/qa/QA-0008-ci-github-actions.md — confidence: CONFIRMADO
- O gêmeo `docs/sdd/_tools/sdd-test.sh` NÃO roda neste repo (procura `*.sln`, o solution é `*.slnx` → exit 3); o gate é o `.ps1` — source: docs/sdd/tasks/TASK-0009-ci-github-actions.md — confidence: CONFIRMADO

## Container tooling (esta máquina)
- podman 6.1.4 + provider `podman-compose` 1.4.1 via pip (Python 3.13 uv, shim no PATH); a versão 1.6.0 tem bug de Windows (`is_context_git_url` lê `C:` como URL e derruba o `-f` do build) — source: docs/sdd/tasks/TASK-0008-containerfile-compose.md — confidence: CONFIRMADO
- `.dockerignore` na raiz é obrigatório: sem ele o contexto de build do Containerfile é o repo inteiro (1.2 GB) — source: .dockerignore — confidence: CONFIRMADO
- Máquina podman é rootful (`Rootless=false`); rootless estrito não demonstrável aqui; volume `infra_squid-data` é dado persistente do dev — source: docs/sdd/qa/QA-0007-containerfile-compose.md — confidence: CONFIRMADO

## Local tests without gate
- `docs/sdd/_tools/check-task-0001.ps1` — verifica os ACs de estrutura do repositório, sem dependência externa — source: GAP: not populated — confidence: OBSERVADO
- `sdd-test.ps1 build|test|health|compose` classifica falha de ambiente como exit 3 (nunca defeito de produto) e grava cada execução em `docs/sdd/runs/test-runs/<timestamp>-<modo>.log` — source: docs/sdd/_tools/sdd-test.ps1 — confidence: CONFIRMADO
- Armadilhas de sandbox já pagas (teste de unidade com `Start-Process -RedirectStandardOutput` devolve `ExitCode` vazio; testhost morre com Win32Exception 5 em integridade Low): usar `cmd /c` + `%ERRORLEVEL%` e o `RUNBOOK-retomada.md` — source: docs/sdd/ideas/BACKLOG.md (S-15, S-07) — confidence: CONFIRMADO