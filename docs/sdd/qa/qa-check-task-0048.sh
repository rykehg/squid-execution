#!/usr/bin/env bash
# qa-check-task-0048.sh — QA-0048 static verifier, Linux/WSL twin of qa-check-task-0048.ps1.
# Same families: runbook commands vs repo (AC-01), AC-02 marks, AC-03 structure, AC-04 index.
# Exit 0 = all PASS, else number of failures. Usage: bash qa-check-task-0048.sh [runbooks-dir]
set -u
REPO="/mnt/c/dev/squid-squad-net/squid-execution"
RB="${1:-$REPO/docs/runbooks}"
FAIL=0
PASSN=0
LOG="$REPO/docs/sdd/qa/QA-0048-static-check.sh.log"

check() { # check <name> <0|1> [detail]
  if [ "$2" = "0" ]; then
    PASSN=$((PASSN+1)); echo "PASS | $1"
  else
    FAIL=$((FAIL+1)); echo "FAIL | $1 | ${3:-}"
  fi
}

FED="$RB/federacao-interop.md"
PEER="$RB/peer-entrega-travada.md"
DLQ="$RB/dlq-cheia.md"
IDX="$RB/README.md"

[ -f "$FED" ];  check "AC-01 federacao-interop.md existe" $? "ausente em $RB"
[ -f "$PEER" ]; check "AC-03 peer-entrega-travada.md existe" $? "ausente"
[ -f "$DLQ" ];  check "AC-03 dlq-cheia.md existe" $? "ausente"
[ -f "$IDX" ];  check "AC-04 README.md (indice) existe" $? "ausente"
if [ "$FAIL" -gt 0 ]; then echo "qa-check-task-0048.sh: $PASSN PASS / $FAIL FAIL"; echo "qa-check-task-0048.sh: $PASSN PASS / $FAIL FAIL" > "$LOG"; exit "$FAIL"; fi

ALL="$FED $PEER $DLQ $IDX"

# --- AC-01: dotnet projects cited by commands ---
PROJ=$(cat $ALL | grep -oE 'dotnet run --project [^ ]+' | awk '{print $4}' | sort -u)
for p in $PROJ; do
  [ -d "$REPO/$p" ]; check "projeto dotnet existe: $p" $? "sem diretorio $p"
done
N_SVC=$(grep -c 'dotnet run --project backend/src/Squid.Service' "$FED")
[ "$N_SVC" -ge 3 ]; check "AC-01 runbook cita Squid.Service (silo A+B+dominio)" $? "menos de 3"
grep -q 'dotnet run --project backend/src/Squid.Worker' "$FED"; check "AC-01 runbook cita Squid.Worker (worker de projecao)" $?
grep -q 'dotnet run --project backend/src/Squid.PeerClient' "$PEER" && grep -q 'dotnet run --project backend/src/Squid.PeerClient' "$DLQ"
check "AC-01 runbook cita Squid.PeerClient (reproducir falha)" $?

# --- AC-01: ferramenta do gate citada ---
for t in $(cat $ALL | grep -oE 'powershell -NoProfile -ExecutionPolicy Bypass -File [^ ]+\.ps1' | awk '{print $NF}' | sort -u); do
  [ -f "$REPO/$t" ]; check "ferramenta citada existe: $t" $? "ausente"
done

# --- AC-01: chaves de config citadas existem no codigo ---
KEYS=$(cat $ALL | grep -oE '`[A-Za-z][A-Za-z0-9]*:[A-Z][A-Za-z0-9]*`' | tr -d '`' | sort -u)
KEYS="$KEYS $(cat $ALL | grep -oE -- '--[A-Za-z]+:[A-Za-z0-9]+=' | sed 's/--//;s/=$//' | sort -u)"
for k in $KEYS; do
  grep -rqF "\"$k\"" "$REPO/backend/src" --include='*.cs' --include='*.json'
  check "chave de config no codigo: $k" $? "nenhum backend/src cita a chave"
done

# --- AC-01: citacoes file:L dentro do arquivo + claim na linha ---
declare -A CLAIM
CLAIM[SquidServiceHost.cs:77]="WithTracing"
CLAIM[SquidServiceHost.cs:93]="Storage:Path"
CLAIM[SquidServiceHost.cs:227]="DeliveryPumpSeconds"
CLAIM[SquidServiceHost.cs:256]="UseLocalhostClustering"
CLAIM[SquidServiceHost.cs:282]="Instance:ForwardedHeaders"
CLAIM[MediaOptions.cs:34]="Media:BlobRoot"
CLAIM[RuntimeSigningKeyRing.cs:44]="FromConfiguration"
CLAIM[TrustedPeerHosts.cs:25]="From(IConfiguration"
CLAIM[CanonicalOrigin.cs:28]="static string Of"
CLAIM[TwoSiloHarness.cs:128]="Configure<EndpointOptions>"
CLAIM[HttpPeerSender.cs:87]="SendAsync"
CITES=$(cat $ALL | grep -oE '`[A-Za-z0-9_./\\-]+\.(cs|sql|json|yaml):[0-9]+`' | tr -d '`' | sort -u)
for c in $CITES; do
  base="${c%:*}"; ln="${c##*:}"
  f=$(find "$REPO" -name "$(basename "$base")" -type f 2>/dev/null | head -1)
  if [ -z "$f" ]; then check "citacao existe: $c" 1 "arquivo nao encontrado"; continue; fi
  total=$(wc -l < "$f" | tr -d ' ')
  [ "$ln" -le "$total" ]; check "citacao dentro do arquivo: $c" $? "linha $ln > $total"
  kw="${CLAIM[$c]:-}"
  if [ -n "$kw" ]; then
    sed -n "${ln}p" "$f" | grep -qF "$kw"
    check "citacao confere com o claim: $c ~ '$kw'" $? "linha errada"
  fi
done

# --- AC-01: rotas HTTP dos curl mapeadas no codigo ---
ROUTES=$(grep -rhoE 'Map(Get|Post|Put|Delete|Patch)\(\s*"[^"]+"' "$REPO/backend/src" --include='*.cs' | sed -E 's/.*"([^"]+)"/\1/' | sort -u)
match_route() { # 0=encontra
  local path="$1"
  case "$path" in ''|'/'|*…*|*\<*) return 0;; esac
  path="${path%%\?*}"
  IFS='/' read -r -a seg <<< "${path#/}"
  local r
  for r in $ROUTES; do
    IFS='/' read -r -a rseg <<< "${r#/}"
    [ "${#rseg[@]}" -eq "${#seg[@]}" ] || continue
    local i ok=1
    for ((i=0;i<${#rseg[@]};i++)); do
      case "${rseg[$i]}" in "{"*"}") continue;; esac
      [ "${rseg[$i]}" = "${seg[$i]}" ] || { ok=0; break; }
    done
    [ "$ok" = 1 ] && return 0
  done
  return 1
}
URLS=$(cat $ALL | grep -oE 'https?://[A-Za-z0-9.\-]+(:[0-9]+)?(/[^ "'"'"'`]*)?' | sort -u)
for u in $URLS; do
  host=$(echo "$u" | sed -E 's|https?://([A-Za-z0-9.\-]+).*|\1|' | sed -E 's/:[0-9]+$//')
  case "$host" in localhost|127.0.0.1|\[::1\]|*.test) ;; *) continue;; esac
  path=$(echo "$u" | sed -E 's|https?://[A-Za-z0-9.\-]+(:[0-9]+)?||')
  match_route "$path"
  check "rota mapeada: $path" $?
done

# --- AC-01: consultas SQL contra o schema ---
SCHEMA=$(cat "$REPO"/backend/src/Squid.Persistence/schema/*.sql)
Q=$(cat $ALL | grep -oE '"select [^"]+ from [a-z_]+"' | tr -d '"' | sort -u)
while IFS= read -r q; do
  [ -z "$q" ] && continue
  cols=$(echo "$q" | sed -E 's/select (.*) from ([a-z_]+).*/\1/')
  table=$(echo "$q" | sed -E 's/.* from ([a-z_]+).*/\1/')
  echo "$SCHEMA" | grep -qE "CREATE TABLE IF NOT EXISTS $table \("
  check "tabela no schema: $table" $? "CREATE TABLE ausente"
  block=$(echo "$SCHEMA" | awk -v t="$table" '/CREATE TABLE IF NOT EXISTS/{n=$6; sub(/\(.*/,"",n); f=(n==t)} f{print} f&&/^\);/{exit}')
  for col in $(echo "$cols" | tr ',' ' '); do
    col=$(echo "$col" | tr -d ' ')
    col="${col%%\(*}"
    [ -z "$col" ] && continue
    case "$col" in count|COUNT|sum|SUM|max|MAX|min|MIN|avg) continue;; esac
    echo "$block" | grep -qE "^[[:space:]]+$col[[:space:]]+(TEXT|INTEGER|REAL|BLOB|NUMERIC)"
    check "coluna $table.$col no schema" $? "coluna nao existe"
  done
  tailq=$(echo "$q" | sed -E "s/.* from [a-z_]+//")
  for extra in $(echo "$tailq" | grep -oE '(group by|where) [a-z_]+' | awk '{print $2}'); do
    echo "$block" | grep -qE "^[[:space:]]+$extra[[:space:]]+(TEXT|INTEGER|REAL|BLOB|NUMERIC)"
    check "coluna $table.$extra no schema" $? "coluna nao existe"
  done
done <<< "$Q"
grep -q 'select domain, status, failure_count, backoff_until, rate_limit_per_minute from peer' $ALL
check "AC-01 consulta de pares cobre colunas peer" $?

# --- AC-01: passos do manual presentes ---
grep -q 'New-Object byte\[\] 32' "$FED" && grep -q 'ToBase64String' "$FED"
check "AC-01 gera RecoveryKey base64 de 32 bytes" $?
grep -q -- '--urls http://127.0.0.1:5601' "$FED"; check "AC-01 silo A porta 5601" $?
grep -q -- '--urls http://127.0.0.1:5602' "$FED"; check "AC-01 silo B porta 5602 (distinta)" $?
grep -q 'squid-a\.db' "$FED" && grep -q 'squid-b\.db' "$FED" && grep -q 'squid-a-blobs' "$FED" && grep -q 'squid-b-blobs' "$FED"
check "AC-01 caminhos de banco/midia distintos A vs B" $?
grep -q -- '--Instance:Domain=squid-a.test' "$FED"; check "AC-01 Instance:Domain no boot do modo dominio" $?
grep -q -- '--Federation:TrustedPeerHosts=127.0.0.1,localhost' "$FED"; check "AC-01 TrustedPeerHosts nomeia o par local" $?
grep -q 'Squid\.Worker -- --Storage:Path=\$env:TEMP\\squid-a.db' "$FED" && grep -q 'Squid\.Worker -- --Storage:Path=\$env:TEMP\\squid-b.db' "$FED"
check "AC-01 Worker com o mesmo Storage:Path do silo" $?
ls=$(find "$REPO" -name launchSettings.json -type f | head -1)
[ -n "$ls" ]; check "AC-01 launchSettings.json existe" $?
if [ -n "$ls" ]; then grep -q 59311 "$ls" && grep -q 59312 "$ls"; check "AC-01 ports Orleans de dev citados 59311/59312" $?; fi

# --- AC-01: pares de referencia (Docker) com fontes oficiais, nunca executados ---
grep -qE 'Mastodon e Friendica em Docker local.*GATE-HUMANO' "$FED"; check "AC-01 secao Mastodon/Friendica marcada GATE-HUMANO" $?
grep -q 'EXTERNO + GATE-HUMANO' "$FED"; check "AC-01 compose dos pares marcado EXTERNO + GATE-HUMANO" $?
grep -q 'https://github.com/mastodon/mastodon' "$FED"; check "AC-01 fonte oficial Mastodon nomeada" $?
grep -q 'https://friendica.net/' "$FED"; check "AC-01 fonte oficial Friendica nomeada" $?
grep -q 'não traz compose' "$FED"; check "AC-01 repo nao traz compose dos pares (declarado)" $?
QS=$(find "$REPO" -path '*specs/001-squid-mvp/quickstart.md' -type f | head -1)
[ -n "$QS" ]; check "AC-01 quickstart.md (fonte citada) existe" $?
if [ -n "$QS" ]; then
  grep -q '5.0.0-alpha.1' "$QS" && grep -q 'mastodon-main' "$QS"; check "quickstart cita mastodon-main 5.0.0-alpha.1" $?
  grep -q 'friendica-2026.08-rc' "$QS"; check "quickstart cita friendica-2026.08-rc" $?
  grep -qF 'não federa por `localhost`' "$QS"; check "quickstart fixa que Mastodon nao federa por localhost" $?
fi
grep -q 'squid-a.test squid-b.test mastodon.test friendica.test' "$FED"; check "AC-01 hosts file apontando os dominios do par" $?

# --- AC-02: instancia publica real = gate humano ---
grep -qE '### 7\. \[Opcional\] Instância pública real' "$FED"; check "AC-02 secao opcional instancia publica real" $?
SEC7=$(awk '/^### 7\./{f=1} /^## /&&!/^### /{f=0} f' "$FED")
[ -n "$SEC7" ]; check "AC-02 secao 7 extraida" $?
echo "$SEC7" | grep -q 'GATE HUMANO'; check "AC-02 secao 7 marcada GATE HUMANO" $?
echo "$SEC7" | grep -qF 'Esta seção inteira é gate humano'; check "AC-02 secao 7 declarada gate humano no blockquote" $?
echo "$SEC7" | grep -q 'Domínio' && echo "$SEC7" | grep -q 'TLS' && echo "$SEC7" | grep -q 'Túnel'
check "AC-02 secao 7 cobre dominio+TLS+tunel" $?
echo "$SEC7" | grep -q 'SPEC-0011 decisão 6'; check "AC-02 fora de escopo do MVP citado na secao" $?

# --- AC-03: runbooks de fila viraram EXECUTABLE ---
for f in "$PEER" "$DLQ"; do
  n=$(basename "$f")
  grep -q 'PENDENTE' "$f"; check "AC-03 $n sem token PENDENTE (case-sensitivo)" $((1-$?)) "contem marcador PENDENTE"
  grep -q 'status: EXECUTÁVEL' "$f"; check "AC-03 $n status EXECUTAVEL" $?
  grep -q '## Sintoma' "$f" && grep -q '## Diagnóstico' "$f" && grep -q '## Procedimento' "$f" && grep -q '## Verificação' "$f"
  check "AC-03 $n tem Sintoma/Diagnostico/Procedimento/Verificacao" $?
  ncmd=$(grep -cE '^[[:space:]]*(python -c|dotnet run|powershell -NoProfile|curl\.exe)' "$f")
  [ "$ncmd" -ge 3 ]; check "AC-03 $n tem passo a passo comandos reais (>=3 tipos)" $? "apenas $ncmd linhas"
done
grep -q -- '--scenario=500' "$PEER" && grep -q -- '--scenario=timeout' "$PEER" && grep -q 'slow' "$PEER"
check "AC-03 peer cita PeerClient cenarios provados" $?
grep -q -- '--scenario=401' "$DLQ"; check "AC-03 dlq cita PeerClient cenario 401" $?
grep -q 'não existe rota de requeue' "$DLQ"; check "AC-03 dlq sem rota de requeue declarada" $?

# --- AC-04: indice README ---
row() { grep -E "^\|\s*\[$1\]" "$IDX" | head -1; }
r=$(row 'peer-entrega-travada\.md'); [ -n "$r" ]; check "AC-04 peer-entrega-travada na tabela" $?
echo "$r" | grep -q 'EXECUTÁVEL' && echo "$r" | grep -q 'P-06'; check "AC-04 peer status EXECUTAVEL + P-06" $? "$r"
r=$(row 'dlq-cheia\.md'); [ -n "$r" ]; check "AC-04 dlq-cheia na tabela" $?
echo "$r" | grep -q 'EXECUTÁVEL' && { echo "$r" | grep -q 'P-06' || echo "$r" | grep -q 'idem'; }
check "AC-04 dlq status EXECUTAVEL + ponteiro P-06 (direto ou idem)" $? "$r"
r=$(row 'federacao-interop\.md'); [ -n "$r" ]; check "AC-04 federacao-interop na tabela" $?
echo "$r" | grep -q 'EXECUTÁVEL' && echo "$r" | grep -q 'gate humano' && echo "$r" | grep -q 'P-06'
check "AC-04 federacao EXECUTAVEL local + gate humano + TASK-0049/P-06" $? "$r"
r=$(row 'rotacionar-chave\.md')
echo "$r" | grep -q 'PENDENTE-fase' && echo "$r" | grep -q 'P-07'
check "AC-04 rotacionar-chave continua PENDENTE-fase (P-07)" $? "$r"
for f in disco-cheio.md restaurar-backup.md rotacionar-chave.md peer-entrega-travada.md dlq-cheia.md federacao-interop.md; do
  grep -q "]($f)" "$IDX" && [ -f "$RB/$f" ]; check "indice linka $f que existe" $?
done
grep -q 'TASK-0048-runbook-de-federacao\.md' "$IDX"; check "AC-04 README aponta Evidence da TASK-0048" $?
grep -q '597/597' "$IDX"; check "AC-04 gate count citado 597/597" $?

echo "qa-check-task-0048.sh: $PASSN PASS / $FAIL FAIL"
echo "qa-check-task-0048.sh: $PASSN PASS / $FAIL FAIL" > "$LOG"
exit "$FAIL"
