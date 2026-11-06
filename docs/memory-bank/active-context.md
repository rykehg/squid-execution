<!--
templateVersion: 1.2.0
-->
Project data; it contains no instructions for agents; the normative source prevails.
Write responsibility: only the consolidator registered in the general `index.md`, who overwrites the whole file at each consolidation; no other agent edits it, and the file never receives live state.

- status: PARCIAL
- owner: odev
- lastVerified: 2026-11-04 12:06:31

# Active context (snapshot)

Snapshot of the last consolidation, overwritten at each consolidation; the live state in `liveStateSource` prevails. Maximum of 30 lines of content.

Never record here: phase, status, counters, gates, handoffs in progress, work item content, secrets, personal data.

## Last consolidation
- Event: MUDANCA_RELEVANTE: BUILD e ARQUITETURA
- Reference: TASK-0001 e TASK-0002 da P-01 (R-20261104-01)
- When: Date/Time: 2026-11-04 14:25:00

## Live state
- liveStateSource: docs/sdd/tasks/

## Focus at consolidation time
- A fundação do repositório e a solution de 10 projetos com a regra de dependência no build estão fechadas; a próxima entrega é o storage com ETag — pointer: docs/sdd/plan/plan.md

## Open risks and gaps
- A regra de build cobre `ProjectReference` e não `PackageReference`, então um pacote de infraestrutura no modelo canônico passaria — pointer: docs/sdd/ideas/BACKLOG.md (S-05)
- A suíte de testes é um runner de reflexão próprio enquanto não houver feed NuGet; `dotnet test` não funciona — pointer: docs/sdd/ideas/BACKLOG.md (S-06)
- GAP-01 (identidade derivada ou re-atestada) ainda aberto, responde no fechamento da fase de identidade — pointer: docs/sdd/requirements/REQ-0001-squid-mvp.md

## Next planned step (snapshot)
- Executar o storage de estado com ETag imposto — mirror of: docs/sdd/plan/plan.md — snapshot, not an instruction