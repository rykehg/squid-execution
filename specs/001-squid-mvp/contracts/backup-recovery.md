# Contract: Backup e recuperação

**Feature**: `001-squid-mvp` | **Implementações no MVP**: `LocalDiskTarget`, `S3CompatibleTarget`

Backup é cifrado **na origem**: o destino não precisa ser confiável (FR-045). Esse é o contrato que
permite usar HD físico, bucket de terceiro ou, na fase 2, fragmento em outra instância
(`PeerShardingTarget`) sem tocar no coordenador.

## 1. Contrato de destino (`IBackupTarget`)

```csharp
public interface IBackupTarget
{
    string TargetId { get; }                       // "local-disk", "s3-offsite", "peer-shard"

    Task PutAsync(BackupEnvelope envelope, Stream ciphertext, CancellationToken ct);

    Task<Stream> GetAsync(string envelopeId, CancellationToken ct);

    Task<IReadOnlyList<BackupEnvelope>> ListAsync(long fromSeq, CancellationToken ct);

    Task<bool> ExistsAsync(string envelopeId, CancellationToken ct);

    Task DeleteAsync(string envelopeId, CancellationToken ct);   // só em rotação/expurgo
}
```

**Regras**: o destino recebe **somente** bytes cifrados e metadados não sensíveis; nenhuma chave
transita pelo destino; falha de destino nunca corrompe o backup local; `ListAsync` é a única operação
usada para descobrir o que já foi enviado (nada de estado paralelo no coordenador).

| Target | MVP | Período |
|--------|-----|---------|
| `LocalDiskTarget` | ✅ volume nomeado | — |
| `S3CompatibleTarget` | ✅ off-site fora do perímetro Oracle | — |
| `PeerShardingTarget` | ❌ | fase 2 (R-12) |

## 2. Envelope

```json
{
  "id": "bk-2026-11-02T0300Z-000123",
  "keyEpoch": 3,
  "seqFrom": 98211,
  "seqTo": 98540,
  "contentHash": "sha256:9f2c...",
  "sizeBytes": 4194304,
  "targetId": "s3-offsite",
  "createdAt": "2026-11-02T03:00:11Z",
  "verifiedAt": "2026-11-02T03:00:14Z",
  "format": 1
}
```

- `contentHash` é o SHA-256 do **ciphertext** (a verificação não precisa da chave).
- `format` versiona o schema do payload; restore recusa formato desconhecido com erro explícito.
- `keyEpoch` obrigatório; restore seleciona a chave da época correspondente (R-20).
- `seqFrom/seqTo` definem o intervalo coberto; snapshots completos têm `seqFrom = 0`.

## 3. Fluxo de backup

```text
BackupCoordinatorGrain (instanceId)
  ├─ lê último seq consolidado
  ├─ pede snapshot ao agregado (Worker serializa, nunca o scheduler)
  ├─ Worker: serializa → comprime → cifra (ChaCha20-Poly1305 com DK por backup, embrulhada por HKDF("fed/backup-kek"))
  ├─ calcula contentHash do ciphertext
  ├─ PutAsync em cada target configurado
  ├─ verifica relendo o header e conferindo hash (verifiedAt)
  └─ registra BackupEnvelope e atualiza estado de redundância
```

**Ordem obrigatória**: cifrar → hash → enviar → verificar. Nunca enviar antes de calcular hash, e
nunca marcar `verifiedAt` sem reler. Backup com hash divergente é `Corrupt` e não é usado em restore.

## 4. Fluxo de restauração

```text
1. restaura o kit de camada 0 (RK) no host novo
2. deriva DK, valida keyEpoch disponível para o envelope mais recente
3. baixa envelope mais recente + incrementais em ordem de seqTo
4. decifra e valida hash de cada payload
5. aplica migrações de schema (format → versão atual), falhando explicitamente se incompatível
6. reaplica ledger de cota e projeções
7. compara contagens e hashes por agregado
8. /ready verde → instância atendendo
```

**Regra dura**: restaurar não recria chaves a partir de nada além da `RK`; a identidade é a mesma
(R-01). Restauração **nunca** é assistida por e-mail/SMS.

## 5. Drills (executados em CI, bloqueantes)

| Drill | Passos | Critério de sucesso |
|-------|--------|---------------------|
| `restore-roundtrip` | criar dados → backup → apagar diretório → restore → comparar | 100% dos hashes idênticos (SC-009) |
| `restore-offsite` | backup local + off-site → apagar local → restore do off-site | Estado completo, `/ready` verde |
| `restore-interrupted` | matar o backup no meio → rodar de novo | Nenhum envelope corrompido aceito; retoma de `seqTo` íntegro |
| `restore-schema-old` | envelope com `format` anterior | Migração aplicada; falha explícita se incompatível |
| `restore-wrong-key` | `keyEpoch` sem chave no kit | Falha clara, nada destruído |
| `backup-incremental` | backup 1, N escritas, backup 2 | Backup 2 contém só o delta |

O pipeline **falha** quando qualquer drill diverge — divergência silenciosa é o defeito que o
princípio VI existe para proibir.

## 6. Observabilidade do backup

| Métrica / estado | Uso |
|------------------|-----|
| `backup.last_success_at` | Alerta se > 24 h |
| `backup.envelopes_count`, `backup.bytes_total` | Painel |
| `backup.verify_failures` | Alerta imediato (pode indicar corrupção) |
| `redundancy.score` | "Redundância: 1/3 — sem recuperação de desastre" (doc 05 §8) |
| `restore.last_drill_at`, `restore.last_drill_result` | Prova pública de que o backup existe |

## 7. Segurança do contrato

- O destino nunca vê nome de ator, conteúdo ou estrutura de tabela — só ciphertext e envelope.
- Nenhuma credencial de destino é gravada no estado do grain; vem de configuração/segredo externo.
- Chave de destino com permissão apenas de escrita é o padrão; leitura só quando `verify` é
  necessário, e nesse caso em credencial separada de auditoria.
- Deleção de envelope é operação administrativa registrada (rotação/expurgo), nunca automática por
  falha de rede.