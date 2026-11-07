<!--
templateVersion: 1.0.0
-->
Specification: the resolved reading of a requirement, precise enough to cut into tasks and to test against.
Write responsibility: ODEV.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-07 00:36:08

# SPEC-0006 — Runbooks iniciais de operação (§8)

- requirement: REQ-0001
- phase: P-01
- dependsOn: SPEC-0004

Item do Sprint 0 do doc 05: "Runbooks iniciais (§8)". O §8 lista cinco runbooks mínimos —
restaurar backup, rotacionar chave, peer com entrega travada, DLQ cheia, disco cheio — mas
quatro descrevem sistemas que ainda não existem nesta fase (backup=P-07, chaves=P-02,
peer/DLQ=P-06). A separação abaixo é a leitura honesta: **procedimento executável agora** para
o que existe; **esboço com contrato futuro preenchido das specs** para o que não existe, sempre
marcado como pendente da fase que o entrega. Runbook que mente sobre um comando que ainda não
roda é pior que runbook ausente.

## Behaviour

- `docs/runbooks/` contém um runbook por item do §8, cada um com: sintoma/trigger, diagnóstico
  (comando), procedimento passo a passo, e verificação de sucesso — sempre com o comando real
  entre crases.
- **`disco-cheio.md`** (existe agora): diagnóstico (`podman system df`, `df`), procedimento com
  `podman system prune`/remoção de imagens antigas, cuidado com `compose down -v` (nunca como
  rotina), verificação (volume `infra_squid-data` intacto, `/health` volta a 200).
- **`restaurar-backup.md`** (parcialmente executável): o ciclo que existe hoje é
  arquivo-para-volume (parar serviço, copiar o SQLite de volta para o volume, subir, `/ready`
  200); a parte "gerar o backup" fica marcada **P-07** com o ponteiro para a spec da backup.
- **`rotacionar-chave.md`** (esboço): escopo RK/RC da §5.1, marcado **P-02** — o runbook declara
  que o procedimento ainda não existe e não dá passos falsos.
- **`peer-entrega-travada.md`** e **`dlq-cheia.md`** (esboços): marcados **P-06**, com o formato
  pronto (trigger/diagnóstico/verificação) e o conteúdo vazio dos passos.
- O índice `docs/runbooks/README.md` lista os cinco, diz o status de cada um (EXECUTÁVEL /
  PARCIAL / PENDENTE de fase) e aponta o gate local de verificação.
- Nenhum runbook introduz comando fora do stack já entregue (podman, compose, `sdd-test.ps1`,
  endpoints `/health` `/ready` `/dashboard`).

## Inputs and outputs

| Direction | Name | Shape | Notes |
| --- | --- | --- | --- |
| in | operador (humano) | leitura | público-alvo: quem opera a instância |
| in | comandos citados | shell/pwsh | os executáveis precisam existir HOJE |
| out | `docs/runbooks/*.md` + índice | 6 arquivos | conteúdo verificável por checagem executável |

## Rules and edge cases

- Todo comando citado como EXECUTÁVEL passa por execução real na prova (ou é marcado
  `PARCIAL` com o trecho não-executável explícito) — checagem faz parte das ACs.
- Runbook PENDENTE de fase não contém passo a passo — só o escopo e o ponteiro; inventar
  procedimento para sistema inexistente é defeito.
- `compose down -v` só aparece como destruição explícita, nunca como passo de rotina.
- Nenhum segredo, token ou caminho absoluto de máquina pessoal nos runbooks.

## Non-functional

- performance: N/A (documentação).
- security: runbooks não expõem procedimento que vaze segredo; `rotacionar-chave.md` não
  documenta valor algum de chave (nem placeholder com formato real).
- platform: comandos dos runbooks executáveis no stack Windows/WSL desta máquina e no Linux
  de produção (§7.2) — onde divergir, o runbook diz qual.

## Out of scope (explicitly)

- Executar backup real, rotacionar chave real, operar peer/DLQ — os sistemas são P-02/P-06/P-07.
- Runbooks de deploy/Caddy/quadlets — fase de deploy.
- Automação dos runbooks (scripts executáveis) — o Sprint 0 pede runbooks (documentos).

## Gaps carried from the requirement

| Gap | Status | Resolution or assumption in force |
| --- | --- | --- |
| GAP-18 | ASSUMED | runbooks de sistemas futuros são esboços com status PENDENTE e ponteiro de fase — leitura de que o Sprint 0 pede os arquivos existentes desde já, não os procedimentos de sistemas ainda não construídos |
| GAP-15, GAP-09 | ASSUMED | herdados: comandos dos runbooks usam o stack entregue (compose dev, endpoints da SPEC-0002) |

## Tasks

| Task | Title | Parallel-safe | Status |
| --- | --- | --- | --- |
| TASK-0010 | Cinco runbooks do §8 + índice com status e comandos executáveis provados | yes — toca só `docs/runbooks/**`; disjunto de tudo aberto | TASK_READY |
