# Documento 04 — Recuperação Social de Chaves e Resiliência Criptográfica

> **Natureza deste documento:** Extensão complementar aos documentos [`02-arquitetura-poc.md`](02-arquitetura-poc.md) e [`03-reaproveitamento-orders-orleans.md`](03-reaproveitamento-orders-orleans.md). Detalha como a instância sobrevive à perda total do servidor **e** da chave, combinando cópia offline, *Shamir's Secret Sharing* (SSS) federado e escrow com segundo fator.
>
> **Escopo:** chave **da instância** (identidade + backup). Chaves **de usuário** estão fora de escopo — ver §11.8.
>
> **Versão 2 (revisão consolidada).** A v1 propunha SSS 3-de-5 entre guardiões e nada mais. Esta versão corrige os achados da revisão técnica e adiciona a arquitetura em camadas.

---

## Changelog v1 → v2

| # | Mudança | Motivo |
|---|---------|--------|
| 1 | **Hierarquia de chaves** (`Root Key` + HKDF) substitui a expressão vaga "chave privada de backup/E2EE" | ChaCha20 exige chave simétrica; X25519 é de acordo; Ed25519 é de assinatura. Eram três segredos tratados como um |
| 2 | **Credencial de Recuperação (RC)** pré-comprometida no setup | A v1 não explicava como autenticar o pedido de resgate sem uma chave — ovo-e-galinha |
| 3 | **Shares verificáveis** (commitment da root + manifesto por share) | SSS puro transforma share corrompida em chave-lixo silenciosa |
| 4 | **Heartbeat + prova de posse + isenção de TTL** | Falha silenciosa por expurgo: a chave de recuperação seria apagada pelo varredor de shards |
| 5 | **Arquitetura em camadas 0–3** | SSS como única rota era frágil e irrealizável com 1–2 nós |
| 6 | **Backup/restore sem rede** como caminho primário da POC | No início há no máximo 2 servidores; 3-de-5 é impossível |
| 7 | **Guardião = nó self-hosted, não celular de usuário** | Churn, ausência de humano no loop, cópias não-enumeráveis |
| 8 | **Invariantes de guardião** (independência, anti-Sybil, sem auto-guardiania) | Contagem de shares ≠ contagem de domínios de falha |
| 9 | **`keyEpoch` + re-share periódico** | Shares de gerações diferentes combinadas produzem lixo |
| 10 | **Correção de contradição** `KeyRecoveryCoordinatorGrain` × `RestoreCoordinatorGrain` | §3.3/3.4 da v1 e a tabela do §4 atribuíam o mesmo trabalho a grains diferentes |

---

## 1. O Problema

### 1.1 O calcanhar de Aquiles

O backup é cifrado **na origem**. A disponibilidade dos dados não garante a sobrevivência da instância: se a **Root Key** morrer junto com o servidor, os fragmentos Reed-Solomon espalhados pela federação são matematicamente inúteis. Backup criptografado sem recuperação de chave é um *sunk cost* elegante.

### 1.2 O que exatamente está sendo recuperado

Esta é a correção mais importante em relação à v1. A instância **não** tem "uma chave privada". Tem uma raiz e derivações:

```
Root Key (RK)  ← 32 bytes de CSPRNG, gerados na criação da instância
   │
   ├── HKDF(info="fed/ed25519-sign")     → seed Ed25519   → identidade do ator / HTTP Signatures
   ├── HKDF(info="fed/x25519-static")    → chave X25519   → acordo de chaves no nível da instância
   ├── HKDF(info="fed/backup-kek")       → KEK           → embrulha as Data Keys dos backups
   └── HKDF(info="fed/guardian-at-rest") → chave local   → cifra shares que ESTA instância guarda p/ terceiros
```

| Segredo | Origem | Uso | Recuperável? |
|---------|--------|-----|--------------|
| `RK` | CSPRNG | raiz de tudo | **Sim — é o único alvo do SSS** |
| Ed25519 privada | HKDF de `RK` | assinatura AP | Sim, por derivação |
| X25519 privada | HKDF de `RK` | acordo de chaves | Sim, por derivação |
| KEK de backup | HKDF de `RK` | embrulha Data Keys | Sim, por derivação |
| Data Key (`DK`) | CSPRNG por backup | cifra o blob | Fica embrulhada no artefato |
| Credencial de Recuperação (`RC`) | **CSPRNG, independente** | autenticar o resgate | **Não deriva de `RK`** ⚠️ |

> ⚠️ **`RC` não pode derivar de `RK`** — se derivasse, seria inutilizável exatamente no cenário em que `RK` não existe. É um par de chaves independente, gerado no setup, cuja parte privada vive **apenas** no kit de recuperação e cuja parte pública é registrada em cada guardião.

**Regra de projeto:** *um único segredo para recuperar.* O SSS fragmenta `RK`. Tudo o mais é derivado. Sem essa hierarquia, "recuperar a chave" não define o que se obtém de volta — e a v1 herdava essa ambiguidade do doc 02 §3.

### 1.3 Envelope encryption (por que os backups não são cifrados direto pela `RK`)

Cada backup usa uma `DK` aleatória; a `DK` é embrulhada pela KEK (derivada de `RK`). O artefato leva `wrappedDK` + `keyEpoch` + `nonce` + ciphertext.

* **Rotação barata:** trocar `RK` = re-embrulhar as `DK`s (dezenas de bytes), sem re-cifrar os blobs.
* **Revogação viável:** uma chave comprometida rotaciona sem invalidar o histórico.
* **Sem isso:** descobrir uma `RK` vazada obrigaria a re-fazer todos os backups da história.

---

## 2. A Solução em Camadas

A v1 tinha uma única rota (SSS entre guardiões). Além de frágil, era **irrealizável no lançamento** com 1–2 servidores. A recuperação passa a ser uma escada, onde cada degrau tem papel distinto:

| Camada | O que é | Papel | Quem controla | Existe na fase |
|--------|---------|-------|---------------|----------------|
| **0** | Kit offline: `RK` cifrada com Argon2id + mnemônico de 24 palavras | **Primária e obrigatória** | O responsável | **0** |
| **1** | SSS da `RK` entre guardiões independentes | **Fail-safe** (perda total da 0) | Conjunto de guardiões | 1–2 |
| **2** | Escrow com 2FA + janela de espera | **Conveniência** do caso comum | Terceiro + você | 2 |
| **3** | Threshold KMS / OPRF | Future-proof | Múltiplas partes | 3 |

**Princípio que amarra tudo:** *cada caminho deve ser independente, e nenhum caminho isolado pode produzir `RK`.* A camada 2 **nunca** devolve o segredo sozinha — ela só acelera a liberação das camadas 0 e 1.

Ordem de prioridade que o responsável deve seguir: **salvar a chave primeiro** (camada 0), amigos/guardiões são *fail-safe*, escrow é conveniência. A camada 0 é o controle de segurança; as demais são controles anti-perda-total.

---

## 3. Camada 0 — Kit de Recuperação Offline (obrigatória)

### 3.1 Conteúdo do kit

| Item | Formato | Observação |
|------|---------|------------|
| `RK` cifrada | Arquivo (`age`/AES-GCM) + QR | Cifrada com passphrase via **Argon2id** |
| Mnemônico de 24 palavras | Papel / metal | Fallback para digitação manual, se o arquivo se perder |
| `RC` (credencial de recuperação) | Mesmo cofre | Ver §4.5 |
| Instruções de restore | 1 página: comando exato | Sem isso, o kit não é operável sob estresse |

### 3.2 Regras de operação

1. **Nunca cifra e chave no mesmo lugar.** `backup.tar.gz` e `backup.key` na mesma pasta/bucket/conta não é criptografia, é ofuscação. Chave no cofre físico, blob no bucket — ou o inverso.
2. **Separação geográfica** entre kit e backup.
3. **Restore drill automatizado.** Um backup nunca testado é uma hipótese. Ver §8.4.

---

## 4. Camada 1 — Guardiões (Shamir's Secret Sharing)

### 4.1 Parâmetros (N, M)

A `RK` é dividida em $N$ shares, exigindo $M$ para reconstruir. Nenhuma share isolada tem valor criptográfico.

**O que cresce com o quê:**

| Movimento | Segurança | Disponibilidade | Custo |
|-----------|-----------|-----------------|-------|
| Aumentar $M$ (mesmo $N$) | ↑ | ↓ | — |
| Aumentar $N$ mantendo $N-M$ (ex.: 3/5 → 6/10) | **↑** | **↑** | Mais guardiões para recrutar |

Aumentar $N$ e $M$ juntos, mantendo a tolerância de perdas, melhora **as duas dimensões**: o atacante precisa comprometer mais detentores distintos, e você tolera mais perdas. O limitador não é matemática — é quantas pessoas/instâncias independentes e confiáveis você realmente tem.

* **POC / fase 1:** 3-de-5 é o piso.
* **Alvo real:** 5-de-9 ou 6-de-10.
* **Fase 0:** `threshold=1` (sem guardiões) — ver §8.

### 4.2 Acoplamento com o Reed-Solomon

A recuperação completa exige **$M$ shares da chave *e* $K$ shards dos dados**. São dois $(N,M)$ independentes que precisam ser desenhados juntos:

* **Guardiões ≡ hospedeiros de shard** → falha correlacionada: uma perda derruba chave **e** dado no mesmo conjunto.
* **Conjuntos disjuntos** → falhas independentes, mas a indisponibilidade multiplica: $P(\geq M \text{ guardiões}) \times P(\geq K \text{ hosts})$.

**Decisão:** **sobreposição parcial com estratos distintos.** Nem espelhar 1:1, nem separar totalmente. O guardião de chave deve ser escolhido por **longevidade e confiança**; o hospedeiro de shard, por **capacidade de disco**. São critérios diferentes e por isso as listas tendem a divergir naturalmente.

### 4.3 Invariantes de guardião (validação de código, não recomendação)

| Invariante | Regra | Por quê |
|-----------|-------|---------|
| **Independência de domínio** | `guardian.domain != protected.domain` | Auto-guardiania é circular e inútil |
| **Anti-Sybil** | Contar **domínios/operadores distintos**, não shares | Duas pessoas na mesma instância = 1 guardião |
| **Diversidade de estrato** | Mínimo de 2 estratos distintos (região / provedor / tipo de operador) | Queda de região Oracle, suspensão de conta |
| **Sem contas do protegido** | Guardião não pode ser um ator hospedado na instância protegida | Se a instância morreu, o guardião morreu junto |

> ⚠️ **Risco específico do Oracle Always Free:** as duas VMs do free tier ficam na mesma conta e tipicamente na mesma região. Suspensão de tenancy ou queda de região apaga ambas. Um guardião no Oracle free tier do *próprio* admin não é um guardião.

### 4.4 Guardiões coletivos (aninhamento)

Um guardião pode ser um **coletivo** — uma comunidade cujos moderadores dividem internamente a share recebida:

```
RK ──SSS(3-de-5)──► [G1] [G2] [G3-grupo] [G4] [G5]
                                    │
                                    └──SSS(4-de-7)──► moderadores da comunidade
```

A recuperação vira uma árvore: recompõe-se a share interna, depois a externa. Nenhum moderador isolado tem nada.

**Custos e ressalvas:**

* **Governança, não criptografia:** quando o conjunto de moderadores muda, alguém precisa lembrar de **re-fragmentar**. Sem isso, a share interna se degrada em silêncio.
* **Recomendação para a POC:** manter `holderType: person | instance | group` no modelo de dados, mas implementar apenas `person` e `instance`. Grupo é fase 2.

### 4.5 Credencial de Recuperação (`RC`) — resolvendo o ovo-e-galinha

**O problema da v1:** a instância A morreu, não tem chave para assinar o pedido, e a autenticação via DNS TXT é **circular** no threat model que o próprio documento nomeia ("sequestrador de domínio") — quem tomou o domínio controla o DNS.

**Solução:** no **setup**, gerar um par `RC` (Ed25519 para assinar + X25519 para receber). A parte pública é registrada **em cada guardião, junto com a share**. A parte privada vai para o kit (camada 0) e **não** fica no servidor.

Fluxo de resgate:

1. Nova instância assina o `KeyRecoveryRequest` com `RC`.
2. O guardião verifica a assinatura contra a `RC` pública registrada no setup — **sem** depender de DNS, e-mail ou da chave perdida.
3. O guardião cifra a resposta para a `RC` X25519 pública.
4. Aprovação humana + DNS TXT passam a ser **defesa em profundidade**, não o único sinal.

> **Custo aceito:** perder `RC` significa perder a camada 1 (restam 0 e 2). Por isso ela vive no mesmo kit da camada 0 e pode ser duplicada em guardiões de confiança distintos.

### 4.6 Shares verificáveis (as duas falhas silenciosas)

Há **duas** falhas silenciosas distintas, e elas exigem mecanismos diferentes:

#### Falha A — share corrompida produz chave-lixo

Com SSS puro, um guardião malicioso ou corrompido devolve uma share inválida e a reconstrução produz **lixo que parece uma chave**. Você só descobre ao falhar a descriptografia — no meio do restore.

| Nível | Mecanismo | Detecta | Atribui culpa | Complexidade |
|-------|-----------|---------|---------------|--------------|
| **1 (obrigatório)** | `H(RK)` (commitment da root) gravado no setup | ✅ reconstrução inválida | ❌ | Trivial |
| **2 (recomendado)** | Manifesto de `H(share_i)` por guardião, criado no setup | ✅ | ✅ identifica o mentiroso | Baixa |
| **3 (ideal teórico)** | Feldman/Pedersen VSS | ✅ | ✅ (matematicamente) | Alta — exige campo primo em vez de GF(256) |

**Decisão para a POC:** níveis 1 + 2. `H(RK)` sobre 256 bits aleatórios não revela nada (não é alvo de brute force nem de dicionário), e o manifesto permite **seguir com as shares boas** em vez de abortar. VSS fica registrado como evolução (§11).

> Correção da v1: "resolver com hash" está correto, mas hash **sozinho** só detecta. Para continuar a recuperação com $N > M$, é o manifesto por share que importa.

#### Falha B — share apagada ou expirada

O varredor de TTL do doc 02 (expurgo de shards antigos) é um risco **catastrófico** aqui: ele apagaria a chave de recuperação de um par.

| Mecanismo | Função |
|-----------|--------|
| **`retention = permanent`** na share | Excluída do sweep de TTL por construção |
| **Teste unitário do sweep** | Asserção de que `KeyGuardianGrain` nunca é tocado |
| **Heartbeat periódico** | Guardião prova que ainda tem a share |
| **Redundancy score no painel** | "3/5 ativos, 1 perdido" — alarme antes do desastre |

**Prova de posse sem revelar a share:** o coordenador envia um `nonce`; o guardião responde `HMAC-SHA256(share, nonce)`. Prova que ainda tem o material, sem expor nada. ⚠️ O canal precisa ser autenticado e o endpoint protegido contra uso como oráculo.

> Correção da v1: heartbeat resolve a Falha B, **não** a Falha A. São mecanismos complementares.

### 4.7 Rotação e re-share

| Operação | Gatilho | Efeito |
|----------|---------|--------|
| **Re-share** (proactive secret sharing) | Anual, ou perda de guardião | Novo polinômio, novas shares, revogação das antigas. **`RK` não muda** |
| **Rotação de `RK`** | Comprometimento suspeitado | Nova `RK` → novo `keyEpoch` → re-embrulhar as `DK`s |
| **Revogação de guardião** | Guardião sai / é comprometido | Remove share da geração e re-fragmenta |

**`keyEpoch` é obrigatório** na chave do `KeyGuardianGrain`: shares de gerações diferentes combinadas produzem lixo. Sem versionamento, um re-share parcial corrompe a recuperação de forma indistinguível de ataque.

### 4.8 Backup recursivo das shares (o loop do guardião)

**O problema:** a share é persistida no grain storage; se o backup do guardião é um snapshot do banco, **o backup do guardião contém a share de terceiros** — e esse backup é fragmentado entre peers que o protegido nunca escolheu como guardiões.

**A força do seu esquema é a do elo mais fraco dos seus guardiões.** Um guardião que não roda o backup do doc 02 não é um bom guardião.

**Solução — fechar o loop criptograficamente:**

```
Chave at-rest da share = HKDF(guardião.RK, info="fed/guardian-at-rest")
```

1. A share é **cifrada** com essa chave antes de tocar o storage.
2. O ciphertext **entra** no backup do guardião (preserva disponibilidade).
3. Recuperar a `RK` do guardião restaura, de uma só vez, **a identidade e as obrigações de guardião dele**.
4. Um peer que hospeda o shard do backup do guardião não consegue abrir nada (backup já é cifrado na origem — proteção dupla).

**Trade-off aceito e registrado:** um comprometimento da `RK` do guardião + acesso aos backups dele expõe as shares que ele guarda. Isso dá ao atacante **1 share por instância protegida** — insuficiente para $M \geq 3$. O limiar absorve a perda. Alternativa (excluir shares do backup) melhora a confidencialidade e piora a disponibilidade, criando a falha B. **Decisão: incluir, cifrado.** Deve haver uma política explícita e auditável informando aos admins que suas instâncias hospedam material de terceiros.

---

## 5. Camada 2 — Escrow com Segundo Fator (conveniência)

### 5.1 O que e-mail/SMS podem e não podem fazer

> **Princípio:** o SSS protege a `RK` com segurança **informacional** ($M-1$ shares não revelam nada). E-mail/SMS converte isso em "quem controlar o provedor recupera a chave". SIM swap, takeover de e-mail, insider do provedor e intimação judicial passariam a valer contra a `RK`.

Além disso, para este projeto especificamente: **usar Gmail como raiz de confiança recentraliza** exatamente o que a rede federada quer descentralizar.

**Usos legítimos:**

| Uso | Mecanismo | Papel |
|-----|-----------|-------|
| Autorizar a liberação dos guardiões | Código por e-mail + aprovação manual do admin do guardião | Fator de **política** |
| Disparar e cancelar janelas de espera | Alerta para todos os guardiões + contato do admin | **Alarme** |
| Autenticar o operador no painel | TOTP/passkey | Autenticação normal |

**Uso proibido:** ser a única fechadura. **E-mail/SMS são alarme, não fechadura.**

### 5.2 Janela de espera (o controle mais eficaz e mais barato)

Toda recuperação bem-sucedida por camada 2 abre uma janela de **72 horas** com:

* Notificação para **todos** os guardiões e para os contatos de emergência do admin.
* Link de cancelamento de uso único.
* Registro auditável (quem pediu, de qual IP, qual `keyEpoch`).

Se o pedido não foi do admin real, ele cancela. Isso mata a maioria dos ataques de takeover a custo quase zero.

### 5.3 Fase de implementação

| Padrão | O que o escrow guarda | Quando |
|--------|----------------------|--------|
| **(a) Autorização apenas** | Nada — só aprova a liberação dos guardiões | **POC** |
| **(b) 2-de-2 com a passphrase do usuário** | Metade de um SSS 2-de-2 | Fase 2 |
| **(c) Threshold OPRF / KMS** | Parte de um protocolo criptográfico | Fase 3 |

O padrão (b) tem um custo importante: o escrow vira alvo de alto valor, guardando uma metade para **todos** os usuários. Só faz sentido com Argon2id de custo alto e um escrow realmente independente da instância — na POC o escrow seria a própria instância, que morre no desastre.

---

## 6. Camada 3 — Futuro (não implementar na POC)

Threshold KMS / OPRF: o serviço de escrow participa de uma **decifração threshold** e não consegue, sozinho, obter a `RK` nem com o banco de dados inteiro. É o único caminho para "recupero pela minha conta" sem transformar a conta em ponto único de comprometimento. Registrado como evolução.

---

## 7. Arquitetura e Fluxo de Execução (Orleans)

O processo é orquestrado pelo runtime do Orleans e trafegado via extensões do ActivityPub. **Regra herdada do doc 03:** o grain **orquestra**; o Worker **computa**. Nunca rodar SSS, cifra ou Reed-Solomon no scheduler do silo.

### 7.1 Setup e distribuição

1. **Seleção de guardiões:** o admin escolhe $N$ instâncias, com validação dos invariantes de §4.3.
2. **Geração:** no setup, gerar `RK` e `RC`.
3. **Fragmentação:** Worker de segurança executa o SSS sobre a `RK` → $N$ shares + manifesto `H(share_i)`.
4. **Orquestração:** `KeyRecoveryCoordinatorGrain` recebe as shares e delega o envio ao `PeerGrain` de cada guardião.
5. **Transporte:** atividade customizada `Offer` com objeto `KeyRecoveryShare`, cifrada para a **X25519 pública do guardião** (não a Ed25519 de assinatura).
6. **Confirmação:** o guardião responde com `H(share_i)`; o coordenador valida contra o manifesto e marca a distribuição como concluída. **Distribuição sem confirmação não conta como redundância.**

### 7.2 Armazenamento no guardião

1. A atividade chega ao `InboxGrain` da instância B.
2. É roteada ao `KeyGuardianGrain`, chave `(peerUri, keyEpoch)`.
3. **Estado:** share cifrada com `HKDF(B.RK, "fed/guardian-at-rest")`, `H(share)`, timestamp, `keyEpoch`, status de aprovação.
4. **Invariante:** o grain retém passivamente o byte array; não interpreta nem valida a lógica do fragmento. `retention = permanent` — **imune ao TTL sweep**.

### 7.3 O desastre e o resgate

A instância A foi destruída. O admin provisiona uma VM limpa sob o mesmo domínio e restaura o kit.

1. **Gatilho:** o admin aciona "Recuperar de Guardiões" no painel limpo.
2. **Pedido autenticado:** `KeyRecoveryCoordinatorGrain` emite `KeyRecoveryRequest` assinado com `RC`.
3. **Verificação anti-spoofing (defesa em profundidade):**
   * `RC` pública registrada no setup → verificação criptográfica, **sem** depender de DNS.
   * Aprovação manual do admin do guardião na UI.
   * DNS TXT como terceiro sinal (opcional, nunca o único).
4. **Devolução:** `KeyRecoveryResponse` com a share cifrada para a X25519 pública de `RC`.
5. **Verificação:** o coordenador checa cada share contra o manifesto antes de usar.

### 7.4 Reconstrução e cascata

1. `KeyRecoveryCoordinatorGrain` acumula shares verificadas (estado `2/3`, `3/3`).
2. Ao atingir $M$, o Worker reconstrói `RK` **em memória**, com buffers zerados, **fora** do processo do silo e sem egresso de rede.
3. **Verificação:** `H(RK_reconstruída) == H(RK)` do setup. Falhou → descarta as shares suspeitas e tenta outra combinação.
4. **Persistência:** `RK` vai para o storage seguro local; nunca para um backup automático.
5. **Cascata:** `KeyRecoveryCoordinatorGrain` sinaliza sucesso → `RestoreCoordinatorGrain` assume os **dados** (download dos $K$ shards Reed-Solomon → descriptografia com a KEK derivada → seed do banco).
6. **Re-share:** após recuperação bem-sucedida, disparar re-share imediato (as shares antigas foram expostas ao processo de resgate).

> **Correção da v1 (§3.3/3.4 × §4):** há dois coordenadores com escopos disjuntos — `KeyRecoveryCoordinatorGrain` cuida do **material criptográfico**; `RestoreCoordinatorGrain` cuida dos **dados**. A v1 atribuía a coleta de fragmentos a ambos.

---

## 8. Backup e Restauração Sem a Rede (fase 0)

No início existirão **no máximo dois servidores**. Um esquema 3-de-5 não é apenas difícil — é impossível. Portanto a recuperação **sem rede** é o caminho primário, e a federação é evolução.

### 8.1 A propriedade que torna isso fácil

O backup é cifrado **na origem**. Isso significa que **o destino não precisa ser confiável**:

* Bucket S3-compatible qualquer
* `rsync` para um host qualquer
* HD físico
* Release no GitHub

**Consequência honesta para o doc 02:** o Mutual Sharding **não é necessário para segurança**, apenas para *custo* e para a tese de solidariedade de infraestrutura. O diferencial é "não depender de S3 pago e não depender de boa vontade de provedor" — **não** "seria impossível de outra forma". Registrar isso protege o projeto de uma crítica fácil.

### 8.2 Desenho plugável

```
BackupCoordinatorGrain → IBackupTarget
                          ├── LocalDiskTarget      ← fase 0 (POC)
                          ├── S3CompatibleTarget   ← fase 1 (off-site)
                          └── PeerShardingTarget   ← fase 2 (o diferencial)
```

Mesmo padrão do `IGrainStorage` (doc 03): o coordenador não sabe onde o blob foi parar. Isso torna o início de 1 nó trivial e a federação um plugin posterior.

### 8.3 Fases

| Fase | Nós | Caminho de recuperação | SSS | Entregável |
|------|-----|------------------------|-----|------------|
| **0** | 1 | Kit offline + backup local cifrado + restore drill | `threshold=1` | Instância recuperável por comando |
| **1** | 2 | Acima + cópia off-site fora do perímetro Oracle | Protocolo validado com `PeerClient` falso | Distribuição e resgate ensaiados sem guardião real |
| **2** | 3+ | Guardiões reais 3-de-5, heartbeat, re-share anual | Completo | Rede de recuperação operante |

### 8.4 Restore drill

```
criar instância → backup → destruir → restaurar → comparar hashes
```

Rodando em CI a cada merge. O doc 02 tem fluxo de recuperação mas **nenhum teste de restore** — e um backup nunca testado é uma hipótese.

### 8.5 Redundancy score

Painel e log devem expor a redundância efetiva:

```
Redundância: 1/3 — SEM recuperação de desastre
Redundância: 3/5 guardiões ativos, 1 perdido (heartbeat), 2 degradados
```

É o equivalente ao "o RAID está degradado". O objetivo é **descobrir a perda antes de precisar**, o que é o que justifica o heartbeat de §4.6.

---

## 9. Impacto na Estrutura de Grains (update doc 03)

| Grain | Chave | Estado persistido | Responsabilidade |
|-------|-------|-------------------|------------------|
| `KeyRecoveryCoordinatorGrain` | `instanceId` | Shares verificadas recebidas (`2/3`), manifesto `H(share_i)`, `keyEpoch`, status | Emite pedidos com `RC`, junta e verifica as peças, dispara reconstrução |
| `KeyGuardianGrain` | `(peerUri, keyEpoch)` | Share cifrada, `H(share)`, `RC` pública do protegido, status de aprovação, `retention=permanent` | Guarda o fragmento de terceiros, responde heartbeat, gerencia aprovação de resgate |
| `BackupCoordinatorGrain` | `instanceId` | `IBackupTarget` ativo, último `seq` exportado, `keyEpoch` | Orquestra snapshot → Worker → distribuição (doc 02/03) |
| `RestoreCoordinatorGrain` | `instanceId` | Progresso do download de shards | **Somente dados** (Reed-Solomon), nunca material de chave |

> **Nota de consistência (doc 03, decisão #3):** o estado dos grains usa **SQLite** na POC e Oracle ATP depois, via `IGrainStorage`. A v1 dizia "Oracle ATP" diretamente na tabela — corrigido.

---

## 10. Decisões Consolidadas

| # | Decisão | Justificativa | Reversível? |
|---|---------|---------------|-------------|
| 1 | Hierarquia `RK` + HKDF; SSS sobre a `RK` apenas | Um segredo para recuperar; resolve a ambiguidade simétrica/assimétrica | Difícil |
| 2 | `RC` independente, registrada no setup | Elimina o ovo-e-galinha do resgate e a circularidade do DNS | Sim |
| 3 | Envelope encryption (`DK` por backup) | Permite rotação e revogação sem re-cifrar tudo | Sim |
| 4 | 3-de-5 no piso; 5-de-9/6-de-10 como alvo | Crescer N e M juntos melhora segurança **e** disponibilidade | Sim |
| 5 | Guardião = **nó self-hosted**, nunca celular de usuário | Churn, ausência de humano no loop, cópias não-enumeráveis | Sim |
| 6 | Shares verificáveis por `H(RK)` + manifesto de `H(share_i)` | Detecta **e** atribui; permite seguir com as shares boas | Sim |
| 7 | Heartbeat com prova de posse + isenção de TTL | Combate a falha silenciosa por expurgo | Sim |
| 8 | Shares cifradas com `HKDF(guardião.RK)` e incluídas no backup dele | Fecha o loop de disponibilidade; limiar absorve o risco | Sim |
| 9 | Camada 2 = **autorização**, nunca material de chave; janela de 72h | E-mail/SMS são alarme, não fechadura | Sim |
| 10 | `IBackupTarget` plugável + fases 0/1/2 | Permite operar com 1 nó desde o dia 1 | Sim |
| 11 | Paridade de estratos entre guardião e hospedeiro de shard | Evita acoplar falhas sem separar totalmente | Sim |
| 12 | Re-share após recuperação bem-sucedida | As shares antigas foram expostas durante o resgate | Sim |

---

## 11. GAPs e Dúvidas em Aberto

| # | GAP | Impacto | Encaminhamento |
|---|-----|---------|----------------|
| 1 | **Biblioteca SSS em .NET** | Alto | Avaliar `SecretSharingDotNet` vs implementação própria; decidir GF(256) vs campo primo (impacta se VSS será viável) |
| 2 | **VSS em .NET** | Médio | Só após decidir o campo. Feldman/Pedersen melhora a atribuição de culpa |
| 3 | **Vocabulário JSON-LD** de `KeyRecoveryShare` / `KeyRecoveryRequest` / `KeyRecoveryResponse` | Alto | Modelar com `@context` próprio; outras implementações vão ignorar — decidir se é aceitável |
| 4 | **Segregação de identidade** — a nova instância tem `Ed25519` nova ou derivada da `RK` recuperada? | **Alto** | Se derivada: continuidade de identidade (seguidores permanecem) mas comprometimento do atacante é permanente. Se nova: precisa re-atestação com os peers. **Decisão pendente e bloqueante** |
| 5 | **Interop RSA** — Mastodon usa RSA-SHA256 historicamente | Alto | Suportar RSA na assinatura mesmo adotando Ed25519 internamente |
| 6 | **Prova de posse com guardião offline** | Médio | Heartbeat exige que o guardião responda. Definir tolerância: quantos heartbeats perdidos = guardião morto |
| 7 | **Re-share automático vs manual** | Médio | Automático exige `RC` de cada guardião; manual exige disciplina humana. Provavelmente híbrido |
| 8 | **Chaves de usuário (E2EE)** — fora de escopo | **Alto** | Após uma catástrofe, a instância volta mas as DMs continuam ilegíveis: as chaves estão no device. "Reconstruir perfeitamente" (doc 02 §3) é parcialmente falso. Decidir: SSS entre contatos do usuário, frase de recuperação, ou declarar perda definitiva |
| 9 | **Política de retenção de shares de terceiros** | Médio | §4.8 inclui shares no backup do guardião. Precisa de aviso explícito e auditável ao admin |
| 10 | **Custo do Argon2id no mobile** | Baixo | Só relevante quando o usuário também tiver camada 0 |
| 11 | **MLS para DMs em grupo** | Baixo | Assunto separado do backup |
| 12 | **Threshold com 1 nó** | Baixo | Garantir que `threshold=1` funcione sem quebrar o código de verificação (sem manifesto, sem heartbeat) |
| 13 | **Janela de 72h** | Baixo | Definir se é configurável e o que impede o admin de baixar para 0 |

### 11.1 Dúvida registrada — guardiões em SQLite de usuários

Avaliado e **rejeitado como guardião de instância** (ver §4.3). Resumo do raciocínio, para não ser reaberto sem contexto novo:

| Problema | Detalhe |
|----------|---------|
| Durabilidade | App de rede social perde a maior parte dos usuários em meses; SQLite do sandbox morre em desinstalação, reset ou pressão de armazenamento |
| Cópias não-enumeráveis | Backup do iCloud/Google copia o arquivo silenciosamente → recentraliza em Apple/Google e **quebra a revogação** |
| Sem humano no loop | Não existe admin acordado às 3h: o app teria que liberar a share automaticamente, destruindo a defesa anti-spoofing |
| Coordenação | iOS suspende background; todos os $M$ guardiões precisariam abrir o app na janela de resgate |
| Superfície | Servir shares num binário de app permite coleta em escala por um SDK malicioso |

**O que foi aceito da ideia original:**

* **Multi-device do próprio usuário** — dividir a root *do usuário* entre os *seus* dispositivos. Melhor uso possível de "SQLite local como guardião".
* **Contato opt-in como guardião, mas no nó self-hosted dele** — preserva durabilidade, humano no loop e consistência com a federação.
* **Shards de dados em device: rejeitado** — upload, bateria e plano de dados; e um app iOS não pode servir requisição em background.

**Regra registrada:** *armazene onde a durabilidade é alta; libere onde existe um humano.*

---

## 12. Síntese

A v1 acertou o diagnóstico (sem a chave, o backup distribuído é inútil) e a ferramenta (SSS federado). Faltavam quatro coisas, todas agora fechadas:

1. **Definir o segredo** — uma `RK`, com tudo derivado por HKDF.
2. **Autenticar o resgate sem a chave perdida** — `RC` pré-comprometida no setup.
3. **Detectar as duas falhas silenciosas** — hash/manifesto para share inválida, heartbeat + isenção de TTL para share expurgada.
4. **Funcionar com 1 servidor** — camada 0 obrigatória e `IBackupTarget` plugável antes de qualquer federação.

E uma mudança de postura: a recuperação é uma **probabilidade**, não uma garantia. O trabalho de engenharia é tornar a falha conjunta improvável e **observável** — daí o heartbeat, o redundancy score e o re-share periódico.