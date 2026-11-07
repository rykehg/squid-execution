# Runbook — DLQ cheia

- fase: P-01 · status: PENDENTE-fase (P-06) · origem: doc 05 §8, SPEC-0006
- sem passo a passo de propósito: a DLQ não existe nesta fase (GAP-18)

## Sintoma

A fila de falhas de entrega atingiu o limite e para de aceitar — ou chega perto dele, com a
métrica DLQ size subindo sem descansar. Este é o gatilho previsto; em P-01 não existe DLQ.

## Diagnóstico

**PENDENTE — P-06.** Nenhum comando nesta fase: a fila de falhas é do sistema de entrega
federado, construído na P-06. O diagnóstico futuro lê o tamanho da DLQ e a idade da falha mais
antiga entre as métricas de produto do §8.

## Procedimento

**PENDENTE — P-06 — sem passos.** Sem fila não há o que esvaziar; passo a passo aqui seria
procedimento de sistema inexistente.

Escopo previsto: esvaziar a DLQ com reprocessamento decidido (reentregar, descartar ou
desparar política), garantindo que nada é apagado sem decisão registrada.

Ponteiros: `docs/sdd/plan/plan.md` (P-06),
`specs/001-squid-mvp/contracts/activitypub.md`,
`docs/features/05-arquitetura-consolidada.md` (§8 métricas de produto: DLQ size).

## Verificação

**PENDENTE — P-06.** Será: tamanho da DLQ volta ao normal e as entregas pendentes voltam a
ser consumidas.
