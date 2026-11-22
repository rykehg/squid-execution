<!--
templateVersion: 1.0.0
-->
Specification: the resolved reading of a requirement, precise enough to cut into tasks and to test against.
Write responsibility: ODEV.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-20 21:50:58

# SPEC-0012 — Backup cifrado, restore drill, rotação de chave e painel de redundância

- requirement: REQ-0001 (FR-045, FR-046, SC-009 + S-39/S-42 do backlog)
- phase: P-07
- dependsOn: SPEC-0007 (chaves `RK`/`RC`, kit, Argon2id/HKDF), SPEC-0009 (projeções, ledger de
  cota — restaurados pelo restore), SPEC-0011 (`Squid:RecoveryKey`, duplo embrulho `0009`),
  SPEC-0005 (gate/CI onde os drills correm)

Item do Sprint 6 do doc 05 (`#### Sprint 6 — Backup e recuperação`): backup cifrado **na origem**
com destinos plugáveis, restore drill bloqueante no CI, rotação de chave `RK`/`RC` (S-39, dono
decidido pelo humano 2026-11-12) e painel de redundância. Não reentrevista o que o doc 05
§5.1/§5.4/§8/§12.2, o data-model §2.10 (`BackupEnvelope`/`KeyEpoch`), o contrato
`contracts/backup-recovery.md` (§1..§7) e as pesquisas R-12/R-20 já decidem.

Decisões humanas desta abertura (entrevista 2026-11-20, 2 rodadas, Henrique):

1. **Escopo = Sprint 6 inteiro com ambos os targets** (14:07:55): `LocalDiskTarget` **e**
   `S3CompatibleTarget` off-site na mesma fase, com drill `restore-offsite` no CI — o DoD do
   Sprint 6 ("off-site restaura sem o disco local") fecha na P-07.
2. **S-42 fecha junto com a rotação** (14:07:55): o import de kit ganha a cópia `0009` na mesma
   fase em que a chave é rotacionada — task própria, logo após a rotação.
3. **Tela de redundância = opção A** (confirmada pelo humano no turno entre as rodadas; hora não
   amostrada): endpoint JSON + **página HTML fina servida pelo próprio backend**, sem stack de
   front — o front de verdade é a P-08 e substitui só a vista, o contrato do endpoint não muda.
4. **S3 sem pacote novo** (21:07:26, default aceito): client S3 (`PUT/GET/LIST/DELETE` + SigV4)
   escrito à mão, testado contra um **fake S3 em processo** que valida a assinatura de verdade;
   endpoint real (`Backup:S3:*`) só por config para MinIO/bucket na produção — nenhuma
   dependência a aprovar.
5. **Gatilho do backup** (21:07:26, default aceito): agendador no serviço
   (`Backup:IntervalMinutes`, default 1440) **+** `POST /api/admin/backup/run` do operador;
   `backup.last_success_at` alerta quando > 24 h (contrato §6).
6. **Rotação = só material de instância** (21:07:26, default aceito): nova `Squid:RecoveryKey` →
   `keyEpoch++`, reembrulho das cópias `0009` e das `DK`s de backup, `RC` trocada, kit reemitido
   com as épocas antigas no keyring (R-20: backup antigo **nunca** é reescrito); **rotação de
   chave de ator fica fora** → `S-46` no backlog.

## Behaviour

### A. Núcleo do backup cifrado (FR-045, contrato §2/§3)

- `BackupCoordinatorGrain` orquestra por `instanceId`: lê o último seq consolidado, pede o
  snapshot ao agregado — **o Worker serializa, nunca o scheduler**.
- Pipeline do Worker, nesta ordem **obrigatória**: serializa → comprime → cifra
  (ChaCha20-Poly1305, `DK` aleatória **por backup**, embrulhada por `HKDF(RK, "fed/backup-kek")`)
  → calcula `contentHash` (SHA-256 do **ciphertext**) → `PutAsync` em cada target → **relê o
  header e confere o hash** → grava `verifiedAt` → registra o `BackupEnvelope`.
- Nunca enviar antes de calcular hash; nunca marcar `verifiedAt` sem reler. Hash divergente →
  estado `Corrupt`, e um envelope `Corrupt` **nunca** é usado em restore.
- `LocalDiskTarget` opera sobre o volume nomeado configurado (`Backup:Local:Path`).
- `IBackupTarget` é o contrato do destino (§1): o destino recebe **somente** ciphertext e
  metadados não sensíveis — nenhuma chave transita; falha de destino nunca corrompe o backup
  local; `ListAsync` é a única operação de descoberta do que já foi enviado (sem estado paralelo
  no coordenador).
- `keyEpoch` é obrigatória em todo envelope (invariante do data-model §2.10); `format` versiona
  o payload e restore recusa formato desconhecido com erro explícito.

### B. Destinos plugáveis (FR-046, contrato §1)

- `LocalDiskTarget` (MVP) e `S3CompatibleTarget` (MVP, off-site fora do perímetro do provedor)
  implementam o mesmo contrato; `PeerShardingTarget` é fase 2 (R-12).
- `S3CompatibleTarget` fala S3 com client **escrito à mão** (`PUT`/`GET`/`LIST`/`DELETE` +
  assinatura SigV4) — zero pacote NuGet novo; em teste roda contra um **fake S3 em processo**
  que valida a assinatura; em produção usa `Backup:S3:*` (endpoint, bucket, credencial) vindo
  só de configuração/segredo externo (contrato §7 — a credencial nunca entra no estado do grain
  nem em log).
- Falha do target off-site (500/timeout) degrada para o target local: o backup local segue
  íntegro e o envelope registra o que foi verificado em cada destino.

### C. Agendador, gatilho e observabilidade (contrato §6)

- Agendador no serviço com `Backup:IntervalMinutes` (default **1440**); roda sem intervenção.
- `POST /api/admin/backup/run` (operador) dispara um backup imediato e devolve o envelope.
- `GET /api/admin/backup/status` (operador) devolve `backup.last_success_at`,
  `backup.envelopes_count`, `backup.bytes_total`, `backup.verify_failures`,
  `restore.last_drill_at`, `restore.last_drill_result`.
- `backup.last_success_at` > 24 h → alerta (log/métrica); `backup.verify_failures` → alerta
  imediato (pode indicar corrupção).

### D. Restore e drills bloqueantes (SC-009, contrato §4/§5)

- Restore segue o fluxo do contrato §4: kit/`RK` → `keyEpoch` disponível → baixa envelope mais
  recente + incrementais em ordem de `seqTo` → decifra e valida hash de cada payload → aplica
  migrações de `format` (falha explícita se incompatível) → reaplica ledger de cota e projeções →
  compara contagens e hashes por agregado → `/ready` verde.
- **Cobertura** (esclarecida em 2026-11-22 após o flag `[DIVERGENCIA]` da TASK-0056): o que
  volta é **log de atividade + projeções + ledger de cota** — contas, sessões, documentos de
  contêiner/membro, estado de ator e linhas de objeto **não vêm do backup**; depois de um
  restore abre-se sessão nova (runbook Verificação 4, executado). Cobertura total = `S-49`
  (decisão de escopo do humano).
- Restaurar **nunca** recria chave a partir de nada além da `RK` (R-01); nunca assistido por
  e-mail/SMS.
- Cinco drills correm **no gate** (`Category=RestoreDrill`), e o pipeline falha quando qualquer
  um diverge:
  | Drill | Critério |
  | --- | --- |
  | `restore-roundtrip` | criar dados → backup → apagar → restore → **100% dos hashes idênticos** (SC-009) |
  | `restore-interrupted` | backup morto no meio → nenhum envelope corrompido aceito; retoma de `seqTo` íntegro |
  | `restore-schema-old` | envelope com `format` anterior → migração aplicada; incompatível → falha explícita |
  | `restore-wrong-key` | `keyEpoch` sem chave no kit → falha clara, nada destruído |
  | `backup-incremental` | backup 1, N escritas, backup 2 → só o delta |
- O sexto drill do contrato, `restore-offsite` (backup local + off-site → apagar o local →
  restore do off-site → estado completo, `/ready` verde), entra na task do target S3.
- Runbook `docs/runbooks/restaurar-backup.md` de `PARCIAL` para **EXECUTÁVEL**: todo passo com
  comando real, sem `PENDENTE`.

### E. Rotação de chave `RK`/`RC` — fecha S-39 (doc 05 §5.1, R-20)

- Escopo **só de material de instância** (decisão 6): rotacionar gera nova `Squid:RecoveryKey`
  → `keyEpoch++` → reembrulho das cópias `0009` e das `DK`s de backup para a nova cadeia →
  `RC` trocada → **kit reemitido** com as épocas antigas no keyring.
- Material antigo permanece legível: um envelope da época antiga restaura com o kit reemitido
  (R-20 — nenhum backup antigo é reescrito; reescrever exigiria confiança total no destino, o
  que a cifra na origem existe para não exigir).
- Kit antigo (RC antiga) deixa de verificar; o kit reemitido restaura a `RK` em teste e a
  instância volta a operar com a cadeia derivada nova (verificação do runbook).
- Cadeia derivada permanece coerente com a nova `RK`: `HKDF("fed/ed25519-sign")`,
  `HKDF("fed/rsa-wrap")`, `HKDF("fed/backup-kek")` derivam do mesmo material novo.
- Gatilho: rota de operador `POST /api/admin/recovery/rotate` (sessão + `Instance:Operators`);
  resposta entrega o kit reemitido **uma única vez** — material de chave nunca em log, nunca
  persistido em claro, nunca servido de novo.
- **Rotação de chave de ator fica fora** (decisão 6) → `S-46` no backlog.
- Runbook `docs/runbooks/rotacionar-chave.md` de `PENDENTE-fase` para **EXECUTÁVEL**, sem
  nenhum valor de chave no documento.

### F. Import de kit com cópia de instância — fecha S-42

- `POST /api/recovery/import`, quando `Squid:RecoveryKey` existe, embrulha **também** a cópia
  `0009` da RSA do ator importado (mesma regra do registro da SPEC-0011 §B): a identidade
  restaurada sai federando **sem** precisar de novo registro.
- Sem `Squid:RecoveryKey` no boot: comportamento **byte-idêntico** ao atual (sem cópia, sem
  erro) — mesmo limite do registro (atores antigos à config não federam, aceito).
- Cópia nunca em claro, nunca logada, nunca servida; import idempotente (importar o mesmo kit
  de novo não duplica linha nem muda resposta).

### G. Tela de redundância — opção A (decisão 3)

- `GET /api/admin/backup/status` ganha `redundancy.score` calculado como **targets com o
  envelope mais recente verificado ÷ targets configurados**, no formato do doc 05 §8
  (`"Redundância: 1/3 — sem recuperação de desastre"`; sufixo `— sem recuperação de desastre`
  quando nenhum target off-site tem o envelope mais recente verificado).
- `GET /admin/backup` serve uma **página HTML fina** (servida pelo backend, sem build de front,
  sem pacote novo) que exibe: último `verifiedAt`, envelopes, bytes, `verify_failures`,
  `redundancy.score` e `restore.last_drill_*` — leitura do endpoint JSON acima.
- Autenticação igual às rotas admin existentes: sem sessão `401`, sessão fora de
  `Instance:Operators` `403`. A P-08 substitui esta vista; o endpoint JSON permanece.

## Inputs and outputs

| Direction | Name | Shape | Notes |
| --- | --- | --- | --- |
| in | config `Backup:IntervalMinutes` | int | default 1440; agendador |
| in | config `Backup:InstanceId` | string | default `default`; chave do `instanceId` do contrato §3 (rota + agendador usam o mesmo helper) — enumerada aqui por nota MINOR da QA-0055, 2026-11-21 |
| in | config `Backup:Local:Path` | string | volume do `LocalDiskTarget` |
| in | config `Backup:S3:Endpoint/Region/Bucket/AccessKey/SecretKey` | string | só por config/segredo externo (contrato §7) |
| in | `POST /api/admin/backup/run` | vazio | operador; dispara backup e devolve o envelope |
| in | `POST /api/admin/recovery/rotate` | vazio | operador; devolve o kit reemitido uma única vez |
| in | `GET /api/admin/backup/status` | — | operador |
| in | `GET /admin/backup` | — | operador; HTML |
| in | `POST /api/recovery/import` | kit + senha (já existente) | passa a embrulhar cópia `0009` quando há `Squid:RecoveryKey` |
| out | envelope | JSON do contrato §2 | `contentHash` do ciphertext; `keyEpoch` obrigatória |
| out | status | JSON do contrato §6 + `redundancy.score` + `restore.last_drill_*` | — |
| out | drills | testes `Category=RestoreDrill` no gate | falha de qualquer drill derruba o pipeline |
| out | erro | falha explícita, nunca silenciosa | formato desconhecido, epoch sem chave, hash divergente |

## Rules and edge cases

- Ordem cifrar → hash → enviar → verificar é fixa: pular uma etapa é defeito de QA, não escolha.
- Hash divergente após download → `Corrupt`, nunca usado; `verify_failures` conta e alerta.
- Nenhum envelope sem `keyEpoch` válida; restore aceita qualquer época com chave disponível
  (R-20); rotação **nunca** reescreve backup antigo.
- Destino vê só ciphertext + envelope; credencial de destino nunca em estado/grain/log;
  deleção de envelope só em rotação/expurgo, registrada, nunca automática por falha de rede.
- `ListAsync` é a única descoberta do enviado — estado paralelo no coordenador é defeito.
- Drill divergente → pipeline falha (divergência silenciosa é o defeito que o princípio VI
  proíbe); drill não executado é `unverified`, nunca assumido verde.
- Rotas admin exigem sessão de `Instance:Operators`: sem sessão `401`, fora da lista `403`
  (regra das rotas admin desde a P-03).
- Rotação: resposta com o kit é **one-shot**; material de chave fora de log/rota/persistência;
  kit antigo deixa de verificar.
- Import idempotente: mesmo kit de novo → mesma resposta, zero linha duplicada.
- Erro nunca expõe stack trace, caminho interno ou nome de tabela (FR-053).

## Non-functional

- security: cifra na origem (ChaCha20-Poly1305, `DK` por backup, `HKDF("fed/backup-kek")`);
  destino não confiável por desenho (FR-045); `RC` em claro no storage segue a emenda S-27;
  runbooks sem nenhum valor de chave (nem exemplo com formato real).
- observability: métricas/estados do contrato §6 — `last_success_at` alerta > 24 h,
  `verify_failures` alerta imediato, `redundancy.score` e `restore.last_drill_*` públicos no
  status e na página.
- reliability: falha de target nunca derruba o processo nem corrompe o backup local; backup
  interrompido retoma de `seqTo` íntegro.
- performance: nenhuma meta numérica nova; compressão antes da cifra; incremental por `seqTo`.
- platform: migration `0010_*` liberada para teste/dev/CI (gate 2026-11-09), produção segue
  gate humano; **zero pacote NuGet novo** (S3 à mão, cifra e SigV4 do framework); drills no
  gate local e na CI.

## Out of scope (explicitly)

- `PeerShardingTarget`, SSS/guardiões, Reed-Solomon — fase 2+ (R-12, I-04).
- **Rotação de chave de ator** — decisão 6, vira `S-46` no backlog.
- **Cobertura do envelope além do contrato §4** — o envelope cobre log de atividade, projeções e
  ledger de cota (o que os drills provam); blobs de mídia, contas, sessões, documentos de
  contêiner/membro, estado de ator e linhas de objeto **não vêm do backup** (restauram por
  re-registro/reabertura, runbook Verificação 4) — cobertura total é `S-49` no backlog,
  decisão de escopo do humano — `GAP-55`.
- Front Nuxt/SSR, preview social, share sheet — P-08 inteiro; aqui só a página fina do backend.
- Expurgo/retenção automática de envelopes (política de idade) — rotação/expurgo manual registrado.
- Restauração assistida por e-mail/SMS — proibida pelo contrato §4.
- Instância pública real (TLS/túnel/DNS) — gate humano à parte.

## Gaps carried from the requirement

| Gap | Status | Resolution or assumption in force |
| --- | --- | --- |
| GAP-52 | ASSUMED | S3 testado contra fake S3 em processo com validação SigV4 real; client escrito à mão (zero pacote); endpoint real por config — default aceito pelo humano 2026-11-20 21:07:26 |
| GAP-53 | ASSUMED | gatilho = agendador `Backup:IntervalMinutes` (1440) + `POST /api/admin/backup/run`; alerta de `last_success_at` > 24 h — default aceito 2026-11-20 21:07:26 |
| GAP-54 | RESOLVED (HUMAN) | rotação só de material de instância (`keyEpoch++`, reembrulho `0009`+`DK`s, `RC` nova, kit com keyring); rotação de ator fora → `S-46` — decisão 6, 2026-11-20 21:07:26 |
| GAP-55 | ASSUMED | cobertura do envelope = a do contrato §4, provada pelos drills: **log de atividade + projeções + ledger de cota**; **fora** da cobertura: blobs de mídia e o estado direto de contas/sessões/contêineres/membros/atores/objetos (restaura-se por re-registro/reabertura — runbook Verificação 4, executado na TASK-0056); redação minha anterior ("estado íntegro") corrigida em 2026-11-22 — se o humano quiser cobertura total, task nova (**S-49**) |
| S-39 | RESOLVED (CUT) | rotação `RK`/`RC` + reemissão do kit + teste de cadeia → **TASK-0058**; runbook `rotacionar-chave` EXECUTÁVEL |
| S-42 | RESOLVED (CUT) | cópia `0009` no import de kit → **TASK-0059** |

## Tasks

| Task | Title | Parallel-safe | Status |
| --- | --- | --- | --- |
| TASK-0054 | Núcleo do backup cifrado: migration `0010`, envelope, `IBackupTarget` + `LocalDiskTarget`, cifra na origem | no — base da fase | TASK_READY |
| TASK-0055 | Coordinator + agendador + rotas `backup/run` e `backup/status` | no — serial, blockedBy 0054 | TASK_READY |
| TASK-0056 | Restore + 5 drills no gate (`Category=RestoreDrill`) + runbook `restaurar-backup` | no — serial, blockedBy 0055 | TASK_READY |
| TASK-0057 | `S3CompatibleTarget` (client à mão + fake S3) + drill `restore-offsite` | no — serial, blockedBy 0056 | TASK_READY |
| TASK-0058 | Rotação `RK`/`RC` da instância + kit reemitido + runbook `rotacionar-chave` (fecha S-39) | no — serial, blockedBy 0054+0056 | TASK_READY |
| TASK-0059 | Import de kit com cópia `0009` (fecha S-42) | no — serial, blockedBy 0058 | TASK_READY |
| TASK-0060 | Tela de redundância: `redundancy.score` + página `/admin/backup` (opção A) | no — serial, blockedBy 0057 | TASK_READY |
| TASK-0049 | Interop real Mastodon/Friendica + facet de fecho do DoD (SC-016) | no — última, gate humano | TASK_READY (**re-paiada da P-06 — decisão humana 2026-11-19 23:01:34**) |
