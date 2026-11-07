# Runbook — peer com entrega travada

- fase: P-01 · status: PENDENTE-fase (P-06) · origem: doc 05 §8, SPEC-0006
- sem passo a passo de propósito: a entrega federada não existe nesta fase (GAP-18)

## Sintoma

A entrega para um par remoto específico falha repetidamente e não avança: o recuo não
descansa, as entregas pendentes se acumulando para aquele par. Este é o gatilho previsto;
em P-01 não há entrega federada para ficar travada.

## Diagnóstico

**PENDENTE — P-06.** Nenhum comando nesta fase: envio a pares com recuo e fila de falhas só
existe a partir da P-06 (federação ligada). O diagnóstico futuro observará falhas por par e o
contador de entregas pendentes, com span por delivery federado.

## Procedimento

**PENDENTE — P-06 — sem passos.** Sem sistema de entrega não há o que inspecionar nem o que
reprocessar; descrever passos aqui seria inventar procedimento.

Escopo previsto: inspecionar a fila de falhas do par, decidir reprocessamento ou descarte, e
revalidar a política de entrega reavaliada no par.

Ponteiros: `docs/sdd/plan/plan.md` (P-06),
`specs/001-squid-mvp/contracts/activitypub.md`,
`docs/features/05-arquitetura-consolidada.md` (§3 entrega federada, §8 observabilidade).

## Verificação

**PENDENTE — P-06.** Será: a fila daquele par zera e as entregas voltam a sair, com o span de
delivery federado registrando `peer.domain`, `attempt` e `statusCode`.
