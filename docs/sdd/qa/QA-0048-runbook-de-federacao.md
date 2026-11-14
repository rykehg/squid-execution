<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: ATIVO
- owner: qa
- lastVerified: 2026-11-14 18:40:00

# QA-0048 — Runbook de federação (manual dos dois silos + runbooks P-06 executáveis)

- task: TASK-0048
- spec: SPEC-0011
- runId: R-20261113-01
- verdict: PASSED

Escopo desta QA: o delivery é só `docs/runbooks/**`, então o núcleo do teste é (a) verificar
**cada comando/citação dos três runbooks contra o repositório** (paths, chaves de config, flags,
portas, rotas, colunas de SQL, `arquivo:L` — nada inventado, AC-01) e (b) **re-executar as partes
reproduzíveis locais** (silo A/B + Workers, a sequência da Verificação, os cenários do
`Squid.PeerClient`, as leituras de fila/DLQ, o modo `Instance:Domain`). Mastodon/Docker é
`GATE-HUMANO` por design (GAP-47): verificado só por marca e fontes oficiais, **nunca executado**.

## What is proven

| AC | Scenario | Script | Result |
| --- | --- | --- | --- |
| AC-01 | Comandos reais do repositório: 134 verificações estáticas (projetos `dotnet run`, 7 chaves de config no código, 11 citações `arquivo:L` com claim na linha exata, 14 rotas mapeadas em `MapGet/MapPost`, colunas das consultas na migration `0008`, quickstart/fontes oficiais) + execução dinâmica dos silos/validação | `docs/sdd/qa/qa-check-task-0048.ps1` + `docs/sdd/qa/qa-run-federation-task-0048.ps1` | PASS (exit 0 nos dois) |
| AC-02 | Seção 7 "[Opcional] Instância pública real" marcada **GATE HUMANO**, blockquote "Esta seção inteira é gate humano", cobre domínio/TLS/túnel, referencia SPEC-0011 decisão 6 | `docs/sdd/qa/qa-check-task-0048.ps1` | PASS (exit 0) |
| AC-03 | `peer-entrega-travada.md` e `dlq-cheia.md`: sem token `PENDENTE` (case-sensitivo), `status: EXECUTÁVEL`, quatro seções, ≥3 tipos de comando real + re-execução das leituras de fila/DLQ e cenários 500/401 do PeerClient | `qa-check-task-0048.ps1` + `qa-run-federation-task-0048.ps1` | PASS (exit 0) |
| AC-04 | Índice `README.md`: linhas dos três runbooks com status e fase P-06 corretos (dlq via "idem" apontando para a linha com P-06), `rotacionar-chave` continua PENDENTE-fase/P-07, links existem, contagem 597/597 confirmada **por execução** | `qa-check-task-0048.ps1` + gate | PASS (exit 0) |
| AC-05 | Gate `sdd-test.ps1 test` exit 0 com 597/597 (9m48s); escopo: `git show --stat 8f06ee1` só nos 4 arquivos de `docs/runbooks/**`; working tree só com carimbos do ODEV + artefatos desta QA | gate + `git show --stat` + `git status --porcelain` | PASS (exit 0) |

Os dois gêmeos (`.ps1` no Windows, `.sh` no WSL/Linux) foram **provados capazes de falhar**:
uma cópia mutada dos runbooks (projeto fantasma `Squid.Fantasma`, token `PENDENTE-fase`
reintroduzido, status do `dlq-cheia` trocado no índice) derruba os dois com exit 4, 4 FAILs
direcionados — um por AC atingida.

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `docs/sdd/qa/qa-check-task-0048.ps1` | PowerShell 5.1 | Verificador estático: extrai comandos/citações/rotas/consultas dos runbooks e confere contra o repo; AC-02/03/04 estruturais; grava `QA-0048-static-check.log` |
| `docs/sdd/qa/qa-check-task-0048.sh` | bash (WSL/Linux) | Gêmeo do verificador estático (mesmas famílias de checagem, UTF-8 nativo); grava `QA-0048-static-check.sh.log` |
| `docs/sdd/qa/qa-run-federation-task-0048.ps1` | PowerShell 5.1 + fragmentos verbatim | Re-executa o manual: sobe silo A/B/C e Workers com os comandos do runbook, roda a Verificação 2–10, cenários do PeerClient, leituras de fila/DLQ, prova `Instance:Domain`; limpa tudo; grava `QA-0048-federation-run.log` |

## How to reproduce

Prerequisites: .NET SDK 10, `python` 3, `curl.exe`, Windows (formas executadas); o gêmeo `.sh`
roda em WSL/bash apenas para as checagens estáticas.

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0048.ps1
                                                        # expected: exit 0 — 134 PASS / 0 FAIL
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-run-federation-task-0048.ps1
                                                        # expected: exit 0 — 52 PASS / 0 FAIL (~4 min)
wsl -e bash docs/sdd/qa/qa-check-task-0048.sh           # expected: exit 0 — 145 PASS / 0 FAIL
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test
                                                        # expected: exit 0 — 597/597
```

## Last run

| Date/Time | Command | Exit | Output (max 10 lines) |
| --- | --- | --- | --- |
| 2026-11-14 18:38 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0048.ps1` | 0 | `qa-check-task-0048: 134 PASS / 0 FAIL` (log: `docs/sdd/qa/QA-0048-static-check.log`) |
| 2026-11-14 (mutação) | `powershell ... qa-check-task-0048.ps1 -RunbooksDir <copia-mutada>` | 4 | `130 PASS / 4 FAIL` — FAIL em projeto fantasma, contagem de silos, PENDENTE-fase e status do índice (`QA-0048-mutation.log`) |
| 2026-11-14 18:05:34 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-run-federation-task-0048.ps1` | 0 | `qa-run-federation-task-0048: 0 FAIL` — 52 PASS, silos 5601/5602/5603, Verificação completa, 500/401, DLQ, limpeza (`QA-0048-federation-run.log`) |
| 2026-11-14 | `wsl -e bash docs/sdd/qa/qa-check-task-0048.sh` | 0 | `qa-check-task-0048.sh: 145 PASS / 0 FAIL` (`QA-0048-static-check.sh.log`) |
| 2026-11-14 (mutação) | `wsl -e bash docs/sdd/qa/qa-check-task-0048.sh <copia-mutada>` | 4 | `141 PASS / 4 FAIL` (`QA-0048-mutation.log`) |
| 2026-11-14 18:18:15 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` | 0 | `Aprovado! – Com falha: 0, Aprovado: 597, Ignorado: 0, Total: 597, Duração: 9 m 48 s` (`docs/sdd/runs/test-runs/20261114-181815-test-gate.log`) |
| 2026-11-14 | `git show --stat 8f06ee1` | 0 | 4 files: `docs/runbooks/{README,dlq-cheia,federacao-interop,peer-entrega-travada}.md` only |
| 2026-11-14 | `git status --porcelain` | 0 | `M docs/sdd/ideas/BACKLOG.md`, `M docs/sdd/tasks/TASK-0048-...md` (carimbo do ODEV) + artefatos QA não rastreados |

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| 1 | MINOR | `docs/runbooks/federacao-interop.md:289` | A Verificação 9 afirma "EXIT 0" mas não traz o comando; na prática o documento da outbox só traz o ponteiro `first` — para ver a nota do silo vizinho é preciso buscar a primeira página (`/c/{slug}/outbox?page=1`), como a prova teve de fazer | `QA-0048-federation-run.log`, check `outbox do contener B cita nota de origem do silo A` (PASS só com `?page=1`) |
| 2 | MINOR | `docs/runbooks/federacao-interop.md:231` | `curl.exe -w "%{http_code}"` não termina em `\n`: em execução automatizada os códigos colam (`201201`) e o harness precisou reconstruí-los em blocos de 3; em terminal interativo o próximo prompt nasce colado ao código — leitura possível, mas a manualidade paga | `CodeChunks` em `qa-run-federation-task-0048.ps1` (comentário na função) e PASS de todos os códigos 201/202 |

Nenhum BLOCKER/MAJOR: os dois achados são de legibilidade do manual; nenhum comando é inventado
nem falso, e tudo que o manual promete como EXECUTÁVEL foi executado nesta QA.

## Not covered, and why

- **Formas Linux dos comandos dinâmicos** (`bash`, `curl` sem `.exe`, `python3`) — o próprio
  runbook declara "a forma Linux não rodou nesta prova" e o WSL desta máquina não tem `dotnet`;
  os fatos estáticos (paths, chaves, rotas, SQL) foram verificados no Linux pelo gêmeo `.sh` —
  RISCO baixo: mesma lógica HTTP; sobe apenas se alguém seguir o manual em Linux sem adaptar.
- **Mastodon e Friendica em Docker** — `GATE-HUMANO` por design (GAP-47, TASK-0049); marcados
  `EXTERNO + GATE-HUMANO` com fontes oficiais (`github.com/mastodon/mastodon`,
  `friendica.net`) e versões do quickstart — RISCO: a interop real (SC-016) segue sendo da 0049,
  junto dos riscos já conhecidos S-41 (PEM) e S-43 (`following` nunca estabelece no par Squid-Squid).
- **`--Instance:ForwardedHeaders=true` atrás de TLS/proxy** — passo 6/7 do manual, marcado
  GATE-HUMANO (exige proxy com TLS local) — não executado; a chave `Instance:ForwardedHeaders`
  foi verificada no código (`SquidServiceHost.cs:282`) e o modo `Instance:Domain` puro foi
  executado (5603, id `https://squid-a.test`, webfinger).
- **Stack dev `podman compose` (coletor OTLP)** — pré-requisito opcional marcado EXECUTÁVEL na
  TASK-0010; não re-executado (exigiria build/pull = gate); `infra/compose.yaml` e portas
  8080/16686 verificados estaticamente — RISCO: apenas o aviso `OTLP export failed` já documentado
  como inofensivo.
- **`scenario=timeout`/`slow` do PeerClient** — documentados e aceitos pelo CLI (verificado via
  `TryScenario`/`--help`), mas os cenários executados nesta QA foram `500` e `401` (os que geram
  as linhas de fila dos dois runbooks) — RISCO baixo: mesma máquina de estados.
