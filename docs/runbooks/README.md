# Runbooks de operação

Cinco runbooks mínimos do §8 do documento mestre (`docs/features/05-arquitetura-consolidada.md`),
entregues no Sprint 0 (P-01) conforme `docs/sdd/specs/SPEC-0006-runbooks-iniciais.md`, mais o
manual de federação pedido pelo humano (decisões 3 e 5 da SPEC-0011), entregue na P-06 pela
TASK-0048 — seis no total.

Regra de honestidade (GAP-18): um runbook que manda num comando que não roda é pior que runbook
ausente. Hoje `rotacionar-chave.md` segue esboço com ponteiro de fase (**P-07**, decidido
2026-11-12, fecha S-39) e a geração de `restaurar-backup.md` era **P-07** — **entregue e
EXECUTÁVEL pela TASK-0056** (2026-11-22);
`peer-entrega-travada.md` e `dlq-cheia.md` viraram **EXECUTÁVEL** na P-06 junto com o motor de
entrega (TASK-0046, comandos provados na TASK-0048). Em `federacao-interop.md` o caminho local é
EXECUTÁVEL e a interop com pares reais + a instância pública são **gate humano** (TASK-0049).

## Índice

| Runbook | Escopo | Status | O que falta e em que fase |
| --- | --- | --- | --- |
| [disco-cheio.md](disco-cheio.md) | liberar espaço em disco com o stack parado | EXECUTÁVEL | — |
| [restaurar-backup.md](restaurar-backup.md) | gerar o backup cifrado e restaurar (log + projeções + ledger de cota) | **EXECUTÁVEL** (P-07, TASK-0056 — comandos executados e logados, ver `runs/test-runs/20261122-runbook-restaurar-backup.log`) | — |
| [rotacionar-chave.md](rotacionar-chave.md) | rotacionar `RK`/`RC` | PENDENTE-fase | dono: **P-07** (decidido 2026-11-12, fecha S-39) — chaves existem desde a P-02, mecanismo nasce na P-07 |
| [peer-entrega-travada.md](peer-entrega-travada.md) | entrega federada travada num par | EXECUTÁVEL | — (motor de entrega da P-06: TASK-0046; comandos provados na TASK-0048) |
| [dlq-cheia.md](dlq-cheia.md) | fila de falhas de entrega cheia | EXECUTÁVEL | — (idem) |
| [federacao-interop.md](federacao-interop.md) | manual: dois silos locais, Mastodon/Friendica em Docker e validação da comunicação | EXECUTÁVEL (caminho local) · **gate humano** (pares de referência e instância pública) | execução conjunta com o humano — **P-07/TASK-0049** (re-paiada da P-06 em 2026-11-19) |

Status, na prática: **EXECUTÁVEL** roda hoje com o stack entregue (e foi executado com exit 0 na
prova da task que atualizou o runbook: podman + compose dev, endpoints `/health` e `/ready`, e
desde a P-06 a fila federada); **PARCIAL** tem o trecho que existe com passos e o resto como
ponteiro de fase; **PENDENTE-fase** não tem passos de propósito — o sistema não existe nesta fase;
**gate humano** marca o trecho que só executa com o humano junto (TASK-0049/produção).

## Estrutura de cada runbook

Os cinco runbooks de sintoma usam estas quatro seções, nesta ordem:

## Sintoma

O que o operador vê — o trigger que dispara o runbook.

## Diagnóstico

Como confirmar o problema: os comandos de verificação, marcados **EXECUTÁVEL** quando já
executados com exit 0 na prova da task que os escreveu (ver `## Evidence` em
`docs/sdd/tasks/TASK-0010-runbooks-iniciais.md` e, para a federação,
`docs/sdd/tasks/TASK-0048-runbook-de-federacao.md`).

## Procedimento

O que fazer: passo a passo nos runbooks EXECUTÁVEL e PARCIAL; nos PENDENTE-fase apenas o
escopo e o ponteiro da fase, sem passo nenhum (GAP-18).

## Verificação

Como saber que deu certo: a checagem de sucesso de cada runbook, mais o gate local abaixo.

O sexto runbook (`federacao-interop.md`) é um **manual** (decisão 5 da SPEC-0011), não um
diagnóstico de sintoma: suas seções são **Escopo · Pré-requisitos · Procedimento · Verificação**,
com o passo a passo dos dois silos locais, dos pares de referência em Docker e da validação da
comunicação.

## Marcas dos comandos

- **EXECUTÁVEL** — executado na prova com exit 0.
- **NÃO-executar** — destrutivo: não roda na prova nem em rotina; leia o efeito antes de
  rodar por conta própria.
- **GATE-HUMANO** — só executa com o humano junto (TASK-0049, produção); nunca roda sozinho nem
  em CI.
- **EXTERNO** — runtime ou projeto fora deste repositório (podman, python, compose oficial de
  terceiros); a fonte fica nomeada no próprio runbook.
- PENDENTE (fase) — sistema inexistente nesta fase; nenhum passo.

## Regras

- Nunca use `podman compose down -v` como rotina: é destruição explícita que apaga o volume
  `infra_squid-data` com o dado dentro. Ele aparece nos runbooks só em aviso de destruição.
- Runbook não guarda segredo, credencial nem valor de chave — tudo isso fica fora do repositório.
- Comandos `podman compose` rodam da raiz do repositório com `-f infra/compose.yaml`
  (equivalente a mudar para `infra/` antes do `compose`; mesmo projeto, mesmo volume).

## Gate local de verificação

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test
```

Sai exit 0 com a suíte verde (a contagem cresce a cada fase: **480/480** no fecho da P-05,
2026-11-12; **597/597** na P-06/TASK-0048, 2026-11-14). É o gate que prova que nada quebrou.
