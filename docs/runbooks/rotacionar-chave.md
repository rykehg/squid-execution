# Runbook — rotacionar chave

- fase: P-01 · status: PENDENTE-fase (P-02) · origem: doc 05 §8, SPEC-0006
- sem passo a passo de propósito: o sistema de chaves não existe nesta fase (GAP-18)

## Sintoma

Período de rotação programado, ou suspeita de exposição da chave de uma instância. Este é o
gatilho previsto; em P-01 ele não tem resposta operacional, porque o par de chaves ainda não
existe.

## Diagnóstico

**PENDENTE — P-02.** Nenhum comando nesta fase: `RK` (root key) e `RC` (credencial de
recuperação) só passam a existir com a identidade local da P-02. O diagnóstico futuro mostra
o estado da chave e da hierarquia derivada (HKDF).

## Procedimento

**PENDENTE — P-02 — sem passos.** Em P-01 não há procedimento de rotação: registrar aqui
passos de um sistema que não existe seria mentir no papel.

Escopo previsto (doc 05 §5.1): rotacionar a `RK` mantendo coerente toda a cadeia derivada e
trocar a `RC`, com o kit de recuperação reemitido — sem quebrar o que já está cifrado.

Ponteiros: `docs/sdd/plan/plan.md` (P-02),
`docs/features/05-arquitetura-consolidada.md` (§5.1),
`docs/features/04-recuperacao-chaves-criptografia.md`.

Nenhum valor de chave aparece neste documento — nem exemplo, nem placeholder com formato real.
Chave é segredo e fica fora do repositório.

## Verificação

**PENDENTE — P-02.** Será: o kit reemitido restaura a `RK` em teste (DoD do doc 05 da P-02),
e a instância volta a operar com a cadeia derivada nova.
