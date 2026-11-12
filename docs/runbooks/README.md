# Runbooks de operação

Cinco runbooks mínimos do §8 do documento mestre (`docs/features/05-arquitetura-consolidada.md`),
entregues no Sprint 0 (P-01) conforme `docs/sdd/specs/SPEC-0006-runbooks-iniciais.md`.

Regra de honestidade (GAP-18): quatro dos cinco descrevem sistemas que ainda não existem
(backup = P-07, rotação de chave = sem dono de fase desde 2026-11-12, peer/DLQ = P-06). Esses
são **esboços com ponteiro de fase**, sem passo a passo. Runbook que manda num comando que não
roda é pior que runbook ausente.

## Índice

| Runbook | Escopo | Status | O que falta e em que fase |
| --- | --- | --- | --- |
| [disco-cheio.md](disco-cheio.md) | liberar espaço em disco com o stack parado | EXECUTÁVEL | — |
| [restaurar-backup.md](restaurar-backup.md) | devolver o SQLite de um arquivo para o volume | PARCIAL | geração do backup — P-07 |
| [rotacionar-chave.md](rotacionar-chave.md) | rotacionar `RK`/`RC` | PENDENTE-fase | sem dono de fase — chaves existem desde a P-02, mecanismo nunca entrou em escopo (S-39; candidata P-07) |
| [peer-entrega-travada.md](peer-entrega-travada.md) | entrega federada travada num par | PENDENTE-fase | P-06 (federação) |
| [dlq-cheia.md](dlq-cheia.md) | fila de falhas de entrega cheia | PENDENTE-fase | P-06 (federação) |

Status, na prática: **EXECUTÁVEL** roda hoje com o stack entregue (podman + compose dev +
endpoints `/health` e `/ready`); **PARCIAL** tem o trecho que existe com passos e o resto como
ponteiro de fase; **PENDENTE-fase** não tem passos de propósito — o sistema não existe nesta fase.

## Estrutura de cada runbook

Os cinco runbooks usam estas quatro seções, nesta ordem:

## Sintoma

O que o operador vê — o trigger que dispara o runbook.

## Diagnóstico

Como confirmar o problema: os comandos de verificação, marcados **EXECUTÁVEL** quando já
executados com exit 0 na prova desta task (ver `## Evidence` em
`docs/sdd/tasks/TASK-0010-runbooks-iniciais.md`).

## Procedimento

O que fazer: passo a passo nos runbooks EXECUTÁVEL e PARCIAL; nos PENDENTE-fase apenas o
escopo e o ponteiro da fase, sem passo nenhum (GAP-18).

## Verificação

Como saber que deu certo: a checagem de sucesso de cada runbook, mais o gate local abaixo.

## Marcas dos comandos

- **EXECUTÁVEL** — executado na prova com exit 0.
- **NÃO-executar** — destrutivo: não roda na prova nem em rotina; leia o efeito antes de
  rodar por conta própria.
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

Sai exit 0 com a suíte verde (contagem cresce a cada fase: **480/480** no fecho da P-05,
2026-11-12). É o gate que prova que nada quebrou.
