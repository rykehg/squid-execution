<!--
templateVersion: 1.0.0
-->
# QA-0010 — TASK-0011: /dashboard sem barra final quebra o navegador (restaurar 301)

- runId: `R-20261107-01` · taskId: `TASK-0011` · qaAttempt: **1/2** · status na entrada: `AWAITING_QA`
- authority: CODE (bugfix pós-fecho da P-01, gate humano: restaurar o 301; alternativa `<base>` rejeitada)
- Política: ponytail-qa `full`. Escopo estrito: `## Acceptance criteria` AC-01..AC-05 — nada fora da lista foi exigido.
- Harness: nenhum script novo; probes inline (curl.exe/PowerShell) + runner existente `sdd-test.ps1`.
- Stack para os probes: `podman compose -f infra/compose.yaml up -d` (VS fechado), `down` simples ao final — nunca `-v`.

## AC → cenário (executado pela QA, não lido do log do DEV)

| AC | Cenário | Comando | Exit | Resultado |
| --- | --- | --- | --- | --- |
| AC-01 | 301 no `/dashboard` sem barra + follow até 200 + canônica 200, contra a stack viva | `curl.exe -s -D - http://127.0.0.1:8080/dashboard` · `curl.exe -sL -w "%{http_code} %{url_effective}"` · `curl.exe -s -o NUL -w "%{http_code}" .../dashboard/` | 0 | PASS — `HTTP/1.1 301 Moved Permanently`, `Location: /dashboard/`, `final_code=200 final_url=http://127.0.0.1:8080/dashboard/`, `slash_code=200` |
| AC-02 | Página em `/dashboard/` resolve as refs relativas e a API contra a URL final | `curl.exe -s .../dashboard/` + regex `(src\|href)="..."` + `curl` por ref + `curl .../dashboard/ClusterStats` + trecho do `index.min.js` | 0 | PASS — refs `./index.css`, `./index.min.js`, `favicon.ico` → **200** em `/dashboard/...` (`relative_fail=0`); `GET /dashboard/ClusterStats = 200`; bundle chama `Sc.get(\`ClusterStats\`,...)` — relativo, sem barra inicial, logo resolve contra `/dashboard/` |
| AC-03 | `health` ao vivo + diff do script + modo `compose` completo (outros modos não mudaram) | `powershell ... sdd-test.ps1 health http://127.0.0.1:8080` · `git diff docs/sdd/_tools/sdd-test.ps1` · `powershell ... sdd-test.ps1 compose` | 0 / 0 | PASS — health: `/health 200 /ready 200 /dashboard/ 200`, exit 0. Diff: a única mudança da TASK-0011 no script é o hunk do health (`/dashboard` → `/dashboard/` + linha de usage); o bloco `compose` é pré-existente (TASK-0008, comentários TASK-0008/SPEC-0004) e segue o 301 sem `-MaximumRedirection 0`. Compose mode executado pela QA: **exit 0** com DoD completo — imagem `10001:10001` sem SDK, `/health /ready /dashboard` 200, span no coletor (`GET /health`), volume inode **34925** persistente após down/up sem `-v`. GAP-19 provado por execução |
| AC-04 | grep da reescrita + ciclo de mutação próprio (can-fail) | grep `app.Use` / `Path == "/dashboard"` em `SquidServiceHost.cs` · `sdd-test.ps1 test -TestFilter ...Bare_dashboard...` com mutação e após restauração | 1 → 0 | PASS — grep só acha `app.UseRouting()` (linha 84), nenhuma comparação `Path == "/dashboard"` com atribuição. Mutação da QA: PRE_SHA `569220F455EDE0AA16F02B102B344FC3A7A22689D40B6EA6584A5BBA23248B1D` capturado antes (S-16), reescrita `app.Use` re-adicionada → MUT_SHA `EED587A8C7683B641A0190DF13AA2B375AD8DA5FE2D38AC5EB37836CF452B5BD` → **vermelho exit 1** (`Com falha: 1, Aprovado: 0, Total: 1`, frames `ServiceHostTests.cs:112` e `:115`) → restauração byte-exact (POST_SHA == PRE_SHA, `RESTORE_OK`) + bump de mtime → **verde exit 0** (`Aprovado: 1`) |
| AC-05 | Gate completo + ADR-25 + delta de escopo | `powershell ... sdd-test.ps1 test` · `Select-String '<Version'` em todos os `*.csproj` · `git status --porcelain` + varredura de mtime > 12:16:51 | 0 | PASS — gate **98/98 exit 0** (baseline `20261107-123458` = 97/97 ⇒ +1 = o teste novo conta no total; log `20261107-145249-test-gate.log`); `NO_VERSION_TAG` em nenhum `.csproj`; porcelain 68 → 68 linhas (nenhum arquivo novo); escritas na janela da task: `SquidServiceHost.cs`, `ServiceHostTests.cs`, `sdd-test.ps1` (todos nos `touches`), o próprio task file (append mandatório do fluxo) e **F1** abaixo. Obs.: o mtime de `SquidServiceHost.cs` (14:51:08) é da restauração da mutação da QA — conteúdo byte-exact provado por SHA256 |

## Reproduzir (3 linhas)

```powershell
podman compose -f infra/compose.yaml up -d
curl.exe -s -D - http://127.0.0.1:8080/dashboard          # 301 + Location: /dashboard/
curl.exe -sL -o NUL -w "%{http_code} %{url_effective}" http://127.0.0.1:8080/dashboard   # 200 .../dashboard/
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 health http://127.0.0.1:8080   # exit 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test    # exit 0, 98/98
podman compose -f infra/compose.yaml down                 # nunca -v; volume infra_squid-data sobrevive
```

## Última execução (gate) e artefatos

- cmd: `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` — **exit 0**
- log: `docs/sdd/runs/test-runs/20261107-145249-test-gate.log`
- tail: `Aprovado! – Com falha: 0, Aprovado: 98, Ignorado: 0, Total: 98, Duração: 37 s - Squid.Tests.dll (net10.0)`
- mutação: `docs/sdd/runs/test-runs/20261107-145107-test-filter.log` (vermelho, exit 1) · restauração: `docs/sdd/runs/test-runs/20261107-145142-test-filter.log` (verde, exit 0)
- compose mode: saída integral no job `pwsh-97d316c9b92e4364` (exit 0), espelhada na evidência abaixo

## Findings

- **F1 (menor — escopo/documentação, não bloqueante)** — `docs/sdd/ideas/BACKLOG.md:53` — a linha nova `S-19` (rótulo `TASK-0011 interim 2026-11-07 (DEV)`) tem mtime **12:53:33**, dentro da janela da TASK-0011 (12:16:51–14:31), e `BACKLOG.md` **não está nos `touches`**. Autoria não é demonstrável pela árvore: se ODEV fez o parking (papel dele), é ação de fluxo legal; se foi DEV, é escrita fora do escopo declarado. Conteúdo: apenas documentação de um gap de ambiente (`.dockerignore`/`.vs`), nenhum impacto de produto; a própria task já o registra no `memoryBankFlags` do control block. Não bloqueante; ODEV adjudica (o próprio ODEV sabe se gravou a linha).
- Nenhum BLOCKER. Nenhum defeito de produto encontrado.

## Não coberto / unverified

- **`sdd-test.sh` (twin Linux do runner): não executado** nesta sessão (sem WSL/bash disponível) — `unverified`, nunca assumido verde. AC-03 e AC-05 nomeiam o `.ps1`; o comportamento HTTP dos AC-01/02/03 **foi** executado contra a imagem Linux (podman compose), então o servidor está coberto em Linux; a suíte `dotnet test` em Linux fica `unverified` (o S-11 já é pré-existente e conhecido para o `.sh`).
- CI (GitHub Actions, TASK-0009) não re-executado — fora dos ACs.
- Mutação da QA deixa `SquidServiceHost.cs` com mtime novo (14:51:08); conteúdo comprovado byte-exact por SHA256. Backup em `%TEMP%` removido após a verificação.

## memoryBankFlags

NENHUM — a divergência com a prova antiga do QA-0004 (bare=200) já está registrada no `humanGate` do control block da TASK-0011; o memory bank (`build-run-test.md:32`, `architecture.md:37`) continua verdadeiro para o modo compose, que segue o redirect e observa 200.
