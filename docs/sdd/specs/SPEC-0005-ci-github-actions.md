<!--
templateVersion: 1.0.0
-->
Specification: the resolved reading of a requirement, precise enough to cut into tasks and to test against.
Write responsibility: ODEV.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-06 23:20:00

# SPEC-0005 — CI: build + testes no GitHub Actions

- requirement: REQ-0001
- phase: P-01
- dependsOn: SPEC-0001

Item do Sprint 0 do doc 05: "CI: build + testes". Ambiente de CI fixado pelo doc 05 §7.4:
`ci | GitHub Actions`. Decisões do humano em 2026-11-06: **runner só ubuntu** (aceita o risco
de revelar falhas de plataforma novas e caminha para o alvo de produção §7.2) e **prova local**
(workflow válido + mesmos comandos verdes na máquina; o primeiro run no GitHub é gate humano
separado depois do push — ODEV nunca faz push).

## Behaviour

- `.github/workflows/` contém um workflow que dispara em `push` para `develop`/`main` e em
  `pull_request`, executando **build** e **gate de testes** — os mesmos comandos do gate local
  (`sdd-test.ps1`/`sdd-test.sh build` e `test`), não uma reimplementação paralela.
- Runner `ubuntu-latest` (decisão humana 2026-11-06); `pwsh` existe pré-instalado nos runners
  ubuntu do GitHub — o workflow invoca o script do repo diretamente.
- SDK .NET 10 pinado (linha LTS da REQ-0001) via `actions/setup-dotnet` com versão explícita,
  nunca `latest` flutuando sem pin.
- Restore de pacotes com cache (chave derivada de `Directory.Packages.props`/lockfile) para
  acelerar runs; a fonte continua `nuget.org` (GAP-02) — a CI tem rede.
- O workflow não contém segredo algum: nada de token/credencial em texto; sem `secrets.*`
  desnecessário (build e teste público não exigem).
- Falha de qualquer passo quebra o job (sem `continue-on-error` em build/teste).

## Inputs and outputs

| Direction | Name | Shape | Notes |
| --- | --- | --- | --- |
| in | `push` em `develop`/`main`, `pull_request` | trigger | gatilhos do workflow |
| in | `.github/workflows/*.yml` | YAML | validável localmente |
| out | job build+test verde/vermelho | check run no GitHub | só após push humano (gate separado) |
| out | prova local | mesmos comandos exit 0 nesta máquina | é o que a AC exige agora |

## Rules and edge cases

- YAML inválido ou action inexistente: falha na validação local, nunca "descoberta" só no GitHub.
- Comando do workflow diferente do gate local (caminho, filtro, flag) é divergência — a AC de
  prova local exige que sejam os mesmos comandos.
- Runner Windows não entra (decisão humana: só ubuntu) — a matriz de plataforma fica:
  Windows no gate local, Ubuntu no CI; paridade entre eles é o run humano pós-push.
- `dotnet test` no ubuntu pode falhar onde o Windows passou (lacuna histórica: suite nunca
  rodou em Linux) — se o run humano revelar, é achado de CI, tratado como tarefa nova/correção,
  nunca silenciosamente removido do workflow.
- Sem `push` desta tarefa: entregar o workflow pronto é o fim; executá-lo no GitHub é o gate.

## Non-functional

- performance: cache de NuGet deve fazer o run típico (< 10 min) — alvo, não SLA.
- security: nenhum segredo no YAML; permissões do workflow mínimas (`permissions: contents: read`).
- platform: `ubuntu-latest`; scripts do repo devem funcionar sob `pwsh` no Linux (o gêmeo
  `.sh` é alternativa válida se o `.ps1` tiver dependência Windows — decisão do DEV registrada).

## Out of scope (explicitly)

- Push/merge e o primeiro run no GitHub — gate humano posterior (fora do alcance do ODEV).
- Matrix com Windows no CI — decisão humana 2026-11-06 (só ubuntu).
- Coverage, lint de código, release/CD, restore drill de backup (entrega P-07), container build
  no CI — nenhum é "build + testes" do Sprint 0.
- Cache avançado de build do dotnet (só NuGet restore).

## Gaps carried from the requirement

| Gap | Status | Resolution or assumption in force |
| --- | --- | --- |
| GAP-16 | ASSUMED | gatilhos: `push` em `develop`/`main` + `pull_request` em qualquer branch — padrão do ecossistema, nenhum contrato depende dele |
| GAP-17 | ASSUMED | script invocado no ubuntu: `pwsh docs/sdd/_tools/sdd-test.ps1` se compatível com Linux, senão o gêmeo `sdd-test.sh` — DEV prova qual dos dois roda no contexto do workflow e registra a escolha |
| GAP-13, GAP-02 | ASSUMED | herdados: rede para restore existe na CI; sem rede é ambiente |

## Tasks

| Task | Title | Parallel-safe | Status |
| --- | --- | --- | --- |
| TASK-0009 | Workflow GitHub Actions de build + testes (ubuntu) com prova local | yes — toca só `.github/**` + `_tools` se GAP-17 exigir ajuste; disjunto de tudo aberto | TASK_READY |
