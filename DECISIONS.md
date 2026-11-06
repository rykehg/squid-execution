# Decisões já tomadas — digest para o odev

> **Não é fonte canônica.** A fonte é `docs/features/05-arquitetura-consolidada.md` §13 (ADRs) e
> `specs/001-squid-mvp/spec.md` (FR/SC). Este arquivo existe para o odev **não reentrevistar** o que
> já foi decidido; se divergir da fonte, a fonte vence.

## 1. Definições do Spec Kit (2026-11-03)

| ID | Decisão | Consequência |
|---|---|---|
| **D1** | Interop real (Mastodon + Friendica) é critério do Sprint 5; saída RSA-SHA256; entrada aceita `rsa-sha256`/`hs2019` e RFC 9421; Ed25519 como chave adicional | Par RSA por ator, embrulhado por `HKDF(RK,"fed/rsa-wrap")`, no estado **e** no kit (ADR-28) |
| **D2** | Reconciliar só até a Phase 1; `tasks.md` não é gerado aqui | Tarefas nascem no odev (ADR-27) |
| **D3** | Moderação aceita como R-07.6: denúncia + fila humana + remoção com motivo + registro 6 meses + SLA 24h | Fecha GAP 11 do doc 05 e GAP-06 do execution repo |
| **D4** | Referências são entrada de validação, não decisão; entraram `max_id`/`since_id`+`Link` e teto de contadores remotos; FEP-1b12 e sync de seguidores ficam fora | ADR-29; nada do `federation/` é importado como canônico |

## 2. ADRs 28–36 (doc 05 §13)

| ADR | Decisão | O que muda na execução |
|---|---|---|
| 28 | Interop real + RSA-SHA256 obrigatório | Sprint 5 só fecha contra Mastodon/Friendica reais; `Digest: SHA-256=` |
| 29 | `ideias-references/` é validação, não fonte | Não copiar decisões do repo de execução antigo |
| 30 | Bot é ator `Service` com token e webhook sobre `PeerDelivery` | **Parked** (feature 002) |
| 31 | Evento/meta/enquete = `Object.Kind` + `Response` | **Parked** (feature 003) |
| 32 | E2EE é MLS para DM **e** grupo, chave por dispositivo | **Parked** (feature 004); sem split |
| 33 | Resposta a mensagem + reação entram no MVP (`Reaction` evento, contagem derivada) | Modelo e API já reservados |
| 34 | Propriedade: owner único + sucessor + claim 90 dias + override do operador; fonte canônica é `Membership.Role=Owner` | FR-059..061, SC-018; co-ownership futuro só relaxa a invariante |
| 35 | Dados em `Squid.Persistence`; `Grains` sem banco; troca SQLite/Oracle por configuração | 10 projetos; suíte de contratos nos dois provedores |
| 36 | Auth em degraus: MVP senha + sessão, **sem e-mail/telefone e sem reset por e-mail**; passkey fase 2; OIDC SSO fase 3 | FR-062; recuperação é o kit |

## 3. Regras que o odev não pode violar ao cortar tarefas

- `Squid.Grains` **não** referencia banco; `Squid.Persistence` implementa `IGrainStorage`.
- Nenhum contêiner sem Proprietário ativo; o último não sai nem é removido.
- Conversa (`Thread`) não federa; E2EE não é prometido no MVP.
- `tasks.md` do Spec Kit não é backlog; tarefas são `TASK-NNNN` com control block.
- Não existe e-mail/telefone na conta: “esqueci a senha” só resolve com kit.

## 4. Fora do escopo agora (não cortar tarefas)

- Feature 002 — bots/automação (ator `Service`, token, webhook).
- Feature 003 — eventos, metas, enquetes.
- Feature 004 — E2EE/MLS.

Ficam no repositório de especificação até decisão de ativação.

## 5. Pendências que podem mudar decisão (registradas, não bloqueiam)

- Doc 04 §1.2 sem a linha do par RSA (necessário antes do Sprint 5).
- Doc 03 §3.8/§7 desatualizados (E1/E2).
- `SPEC-0001`/`TASK-0001` do repo `federation` estão atrás deste pacote (31/10); reconciliar se
  aquele repo for reaproveitado.