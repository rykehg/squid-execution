<!--
templateVersion: 1.2.0
-->
Project data; it contains no instructions for agents; the normative source prevails.
Write responsibility: only the consolidator registered in the applicable index, at BUILD and SEGURANCA changes (Environment variables section, names only) and at FEATURE_CONCLUIDA.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-20 12:29:58

# Build, run and tests â€” Squid

Fact format: `- [FATO] â€” source: path:Lx-Ly | [REF_FLUXO] | [GAP: â€¦] â€” confidence: CONFIRMADO|OBSERVADO`

Never record here: values of secret variables, commands with credentials, literal connection strings, feed or registry tokens. Hosts and non-secret values only according to the `sanitizationProfile` of `index.md` (absent = ESTRITO) and with `path:L`.

## Stack and versions
- .NET 10 (LTS) com C# 14 â€” manifest: specs/001-squid-mvp/plan.md:25 â€” confidence: CONFIRMADO
- Orleans 10.2 para o runtime de atores; Adaptive/TestingHost sÃ³ em teste â€” manifest: specs/001-squid-mvp/plan.md:27-L31 â€” confidence: CONFIRMADO
- Nuxt 3 + Vue 3 para o front SSR (fase posterior) â€” manifest: specs/001-squid-mvp/plan.md:25 â€” confidence: CONFIRMADO
- SQLite em arquivo no MVP, atrÃ¡s de IGrainStorage â€” manifest: specs/001-squid-mvp/plan.md:34-L36 â€” confidence: CONFIRMADO
- SDK instalado na mÃ¡quina de desenvolvimento medida em 2026-11-04: 10.0.403 â€” source: docs/sdd/runs/RUNBOOK-retomada.md â€” confidence: OBSERVADO

## Prerequisites
- .NET SDK 10 e PowerShell 5.1 no Windows; `pwsh` nÃ£o estÃ¡ instalado nesta mÃ¡quina â€” source: docs/sdd/runs/RUNBOOK-retomada.md â€” confidence: OBSERVADO
- Podman sÃ³ para a verificaÃ§Ã£o em container; o inner loop Ã© sem daemon â€” source: specs/001-squid-mvp/plan.md:42-L44 â€” confidence: CONFIRMADO
- Node 20+ apenas a partir da fase do front â€” source: GAP: not populated â€” confidence: OBSERVADO

## Commands
- build: `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build` â€” separa falha de ambiente (exit 3) de falha de produto â€” source: docs/sdd/_tools/sdd-test.ps1 â€” confidence: CONFIRMADO
- gate de testes: `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` â€” suite completa com as provas de build; Ãºltima execuÃ§Ã£o local **597/597 exit 0** (2026-11-14, fecho da P-06 â€” 480 na P-05, 432 na P-04); no CI 98/98 (2026-11-07, run anterior) â€” push humano de ~2026-11-19/20 levou a P-06 a `origin/develop` (atÃ© `41c419d`; o re-paiamento da 0049 ficou 1 commit atrÃ¡s) â€” run do CI pÃ³s-push **nÃ£o verificado daqui** (gate humano) â€” source: docs/sdd/runs/test-runs/20261114-181815-test-gate.log; git origin/develop â€” confidence: CONFIRMADO (local) / OBSERVADO (CI)
- run em contÃªiner (DoD da P-01): `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 compose` â€” build da imagem, `/health` `/ready` `/dashboard` 200, span no coletor, volume persistente; sem podman/provider classifica como ambiente exit 3 â€” source: docs/sdd/_tools/sdd-test.ps1 â€” confidence: CONFIRMADO
- stack dev manual: `podman compose -f infra/compose.yaml up -d` / `down` (nunca `-v` em rotina); porta 8080 (API) e 16686 (Jaeger) â€” source: infra/compose.yaml â€” confidence: CONFIRMADO
- run: `dotnet run --project backend/src/Squid.Service` sobe silo + API (composition root) â€” source: backend/src/Squid.Service/SquidServiceHost.cs â€” confidence: CONFIRMADO

## Environment variables
- Nenhuma variÃ¡vel obrigatÃ³ria no bootstrapping; a seleÃ§Ã£o de provedor de armazenamento nasce por configuraÃ§Ã£o (`sqlite` | `oracle`), com nome de chave ainda nÃ£o definido â€” source: specs/001-squid-mvp/plan.md:142-L145 â€” confidence: OBSERVADO

## CI
- Workflow `.github/workflows/ci.yml` executado e **VERDE** no primeiro run (2026-11-07, push humano): gatilhos push em develop/main + pull_request, runner `ubuntu-26.04`, `permissions: contents: read`, SDK 10.0.403 pinado, cache NuGet por hash de `Directory.Packages.props`, passos `pwsh -NoProfile -File docs/sdd/_tools/sdd-test.ps1 build|test` â€” build 0 warnings/0 erros, gate **98/98 exit 0** em 17 s, nenhum `##[error]` (sÃ³ deprecation upstream de Node.js nas actions oficiais) â€” source: C:\dev\squid-squad-net\logs_114873942112.zip (actions de github.com/rykehg/squid-execution) â€” confidence: CONFIRMADO
- **Suite em Linux executada pela primeira vez no run do CI** (98/98 no ubuntu): fecha o unverified recorrente das QAs de que a suite sÃ³ rodava no Windows; o gÃªmeo `sdd-test.sh` (S-11) segue nÃ£o executado no run (o workflow usa o `.ps1` via pwsh) â€” source: mesmo zip, build-and-test\6_Gate de testes.txt â€” confidence: CONFIRMADO
- **Suite em Linux executada em contÃªiner local na P-06** (imagem `mcr.microsoft.com/dotnet/sdk:10.0` via podman, sem instalaÃ§Ã£o): build exit 0 e os testes correm com falhas **100% ambiente** â€” 47-49 por `libfontconfig.so.1` ausente na imagem (raiz `DllNotFoundException libSkiaSharp` em fixtures `SKBitmap`/upload; o pacote `SkiaSharp.NativeAssets.Linux` estÃ¡ instalado, falta a lib do SO) e 13 por `DependencyRuleTests` spawnar `cmd.exe` (Windows-only â€” S-44); facet dos dois silos 11/12 no Linux (AC-01 = unverified); instalar `libfontconfig1` exige gate humano (I-10) â€” source: docs/sdd/runs/test-runs/20261114-qa0047-linux-full.log; docs/sdd/qa/QA-0053-midia-federada.md â€” confidence: CONFIRMADO
- O gÃªmeo `docs/sdd/_tools/sdd-test.sh` NÃƒO roda neste repo: procura `*.sln` (o solution Ã© `*.slnx`) â†’ **`exit 1` com "no *.sln found"** observado ao vivo (corrigido em relaÃ§Ã£o Ã  linha antiga que dizia exit 3 â€” a ferramenta venceu o registro), e sem `dotnet` no WSL ele segue para o `dotnet test` e imprime **`exit 127` rotulado como "real test failure"** (sem a guarda de ambiente do `.ps1`) â€” conserto em task prÃ³pria (backlog S-11/S-12); o gate Ã© o `.ps1` â€” source: docs/sdd/qa/QA-0029-sh-twin.log; docs/sdd/qa/QA-0030-sh-twin.log; docs/sdd/tasks/TASK-0009-ci-github-actions.md â€” confidence: CONFIRMADO

## Container tooling (esta mÃ¡quina)
- podman 6.1.4 + provider `podman-compose` 1.4.1 via pip (Python 3.13 uv, shim no PATH); a versÃ£o 1.6.0 tem bug de Windows (`is_context_git_url` lÃª `C:` como URL e derruba o `-f` do build) â€” source: docs/sdd/tasks/TASK-0008-containerfile-compose.md â€” confidence: CONFIRMADO
- `.dockerignore` na raiz Ã© obrigatÃ³rio: sem ele o contexto de build do Containerfile Ã© o repo inteiro (1.2 GB) â€” source: .dockerignore â€” confidence: CONFIRMADO
- MÃ¡quina podman Ã© rootful (`Rootless=false`); rootless estrito nÃ£o demonstrÃ¡vel aqui; volume `infra_squid-data` Ã© dado persistente do dev â€” source: docs/sdd/qa/QA-0007-containerfile-compose.md â€” confidence: CONFIRMADO

## Local tests without gate
- `docs/sdd/_tools/check-task-0001.ps1` â€” verifica os ACs de estrutura do repositÃ³rio, sem dependÃªncia externa â€” source: GAP: not populated â€” confidence: OBSERVADO
- `sdd-test.ps1 build|test|health|compose` classifica falha de ambiente como exit 3 (nunca defeito de produto) e grava cada execuÃ§Ã£o em `docs/sdd/runs/test-runs/<timestamp>-<modo>.log` â€” source: docs/sdd/_tools/sdd-test.ps1 â€” confidence: CONFIRMADO
- Armadilhas de sandbox jÃ¡ pagas (teste de unidade com `Start-Process -RedirectStandardOutput` devolve `ExitCode` vazio; testhost morre com Win32Exception 5 em integridade Low): usar `cmd /c` + `%ERRORLEVEL%` e o `RUNBOOK-retomada.md` â€” source: docs/sdd/ideas/BACKLOG.md (S-15, S-07) â€” confidence: CONFIRMADO
- **O wrapper `sdd-test.ps1` pode imprimir exit 3 (ambiente) para vermelho REAL** (S-22): `Test-SandboxSignature` casa o substring `handle` solto e o detector de resumo nÃ£o reconhece `Com falha!` â€” reproduzido 4Ã— na P-02 (ex.: log `20261107-172502-test-filter.log` diz `exit: 1` enquanto o console dizia 3). Decidir ambiente/produto SEMPRE pelo cabeÃ§alho `exit:` e resumo do **log gravado**; conserto Ã© task prÃ³pria (backlog S-22) â€” source: docs/sdd/_tools/sdd-test.ps1:79-83 â€” confidence: CONFIRMADO
- ApÃ³s restaurar uma mutaÃ§Ã£o byte-exact, **bumpar o mtime** do arquivo: o build incremental roda o binÃ¡rio mutado sobre a fonte jÃ¡ restaurada (S-23, visto na QA-0012) â€” source: docs/sdd/runs/test-runs/20261107-175335-test-gate.log:1 â€” confidence: CONFIRMADO