# Runbook — rotacionar chave

- fase: P-01 · status: PENDENTE-fase (**dono: P-07**, decidido pelo humano 2026-11-12) · origem: doc 05 §8, SPEC-0006
- sem passo a passo de propósito: rotação nunca entrou em escopo de fase até a decisão (GAP-18) — S-39 no backlog, dono fechado como P-07

## Sintoma

Período de rotação programado, ou suspeita de exposição da chave de uma instância. Este é o
gatilho previsto; até agora ele não tem resposta operacional: o par de chaves existe desde a
P-02, mas nenhum mecanimento de rotação foi construído.

## Diagnóstico

**PENDENTE — P-07 (dono).** `RK` (root key) e `RC` (credencial de recuperação) existem
desde a P-02 (kit de identidade: `RK` protegida por Argon2id, `RC` em claro no storage — emenda
S-27). O diagnóstico futuro mostra o estado da chave e da hierarquia derivada (HKDF); hoje não
há comando que exponha esse estado para rotação.

## Procedimento

**PENDENTE — P-07 — sem passos.** Registrar aqui passos de um sistema que não existe seria
mentir no papel. A rotação nunca entrou em escopo de fase até agora; o dono é a **P-07**
(decisão humana 2026-11-12, fecha o S-39), que cortará as tasks de rotação junto com backup.

Escopo previsto (doc 05 §5.1): rotacionar a `RK` mantendo coerente toda a cadeia derivada e
trocar a `RC`, com o kit de recuperação reemitido — sem quebrar o que já está cifrado.

Ponteiros: `docs/sdd/plan/plan.md` (linha da P-07 + S-39 no backlog),
`docs/features/05-arquitetura-consolidada.md` (§5.1),
`docs/features/04-recuperacao-chaves-criptografia.md`.

Nenhum valor de chave aparece neste documento — nem exemplo, nem placeholder com formato real.
Chave é segredo e fica fora do repositório.

## Verificação

**PENDENTE — P-07.** Será: o kit reemitido restaura a `RK` em teste, e a instância
volta a operar com a cadeia derivada nova.
