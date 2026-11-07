#!/usr/bin/env bash
# qa-check-task-0010.sh - Linux/WSL twin of qa-check-task-0010.ps1 (static checks only).
# Same checks, same ids, same exit contract: 0 = all executed checks green, 1 = FAIL.
#
# What it covers: AC-01 (index + status + gate pointer), AC-02 (6 files, four sections,
# PENDENTE runbooks without steps), AC-03 static half (every command-like backticked
# segment classified: executed-on-Windows list | NAO-executar | known tool mention), AC-04
# (down -v context, secrets, absolute paths), AC-05 (git delta window).
#
# NOT executed here, never assumed green on this platform:
#   - AC-03 execution cycle (podman compose/create/cp/rm): the podman engine of this WSL
#     distro is a DIFFERENT, empty engine (no infra_squid-data volume), so running the
#     lifecycle here would prove nothing about the documented stack. The Windows side runs
#     it; the Linux string forms (df -h, curl -fsS http://localhost:8080/health and
#     /ready) are executed verbatim inside WSL as separate recorded commands.
#   - the local gate (sdd-test.ps1 test): Windows PowerShell 5.1 + dotnet.
#
# Usage: bash docs/sdd/qa/qa-check-task-0010.sh [runbooks-path] [repo-root]
set -u

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
RB="${1:-}"
REPO_ROOT="${2:-}"
[ -z "$REPO_ROOT" ] && REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
[ -z "$RB" ] && RB="$REPO_ROOT/docs/runbooks"

FAILS=0
TOTAL=0

check() { # check <id> <0|1> <detail>
  TOTAL=$((TOTAL + 1))
  if [ "$2" = "1" ]; then echo "PASS $1 - $3"; else echo "FAIL $1 - $3"; FAILS=$((FAILS + 1)); fi
}
info() { echo "INFO $1 - $2"; }

# accent tokens as raw UTF-8 bytes (source file stays ASCII)
EXEC=$'EXECUT\xc3\x81VEL'                 # EXECUTAVEL
NAO=$'N\xc3\x83O-executar'                # NAO-executar
T_PEND='PENDENTE-fase'
H_SINT='## Sintoma'
H_DIAG=$'## Diagn\xc3\xb3stico'
H_PROC='## Procedimento'
H_VERI=$'## Verifica\xc3\xa7\xc3\xa3o'
T_TRECHO=$'### Trecho execut\xc3\xa1vel'
CTX_PAT=$'destructiv|N\xc3\x83O-executar|Nunca|destrui|apaga|perda de dados'   # destructive-warning context

EXPECTED="README.md disco-cheio.md restaurar-backup.md rotacionar-chave.md peer-entrega-travada.md dlq-cheia.md"
PENDENTES="rotacionar-chave.md peer-entrega-travada.md dlq-cheia.md"
RB2="disco-cheio.md restaurar-backup.md"

PLANNED_EXEC="podman system df|df -h|wsl df -h|podman volume ls|podman compose -f infra/compose.yaml down|podman compose -f infra/compose.yaml up -d|curl -fsS http://localhost:8080/health|curl.exe -fsS http://localhost:8080/health|curl -fsS http://localhost:8080/ready|curl.exe -fsS http://localhost:8080/ready|podman create --name squid-restore -v infra_squid-data:/data squid-service:dev|podman cp ./squid-grainstate.db squid-restore:/data/squid-grainstate.db|podman rm squid-restore"
MENTIONS="podman compose|df|curl|curl -f"
REF_TIME='2026-11-07 00:36:08 -0300'

echo "repo-root:    $REPO_ROOT"
echo "runbooks:     $RB"
echo "mode:         STATIC only (Linux twin)"
echo "delta window: 2026-11-07 00:36:08 (task createdAt)"
echo "platform:     $(uname -s) $(uname -r) $(uname -m)"

in_list() { # in_list <needle> <pipe-separated-list>
  local IFS='|'
  local item
  for item in $2; do [ "$item" = "$1" ] && return 0; done
  return 1
}

# ---------------------------------------------------------------- AC-02 existence (+ no extra .md)
PRESENT=""
if [ -d "$RB" ]; then
  PRESENT="$(cd "$RB" && ls -1 *.md 2>/dev/null | tr '\n' ' ')"
fi
MISSING=""
EXTRA=""
for f in $EXPECTED; do
  case " $PRESENT " in *" $f "*) : ;; *) MISSING="$MISSING,$f" ;; esac
done
for f in $PRESENT; do
  case " $EXPECTED " in *" $f "*) : ;; *) EXTRA="$EXTRA,$f" ;; esac
done
if [ -z "$MISSING" ] && [ -z "$EXTRA" ]; then
  check AC02-existence 1 "expected 6 files, found: $(echo $PRESENT | wc -w)"
else
  check AC02-existence 0 "missing=[$MISSING] extra=[$EXTRA]"
fi

# ---------------------------------------------------------------- AC-01 index of the 5 with status + gate
if [ -f "$RB/README.md" ]; then
  status_of() { # status_of <file> <token> -> 0 when the index row carries the token
    grep -F "]($1)" "$RB/README.md" 2>/dev/null | grep -qF "$2"
  }
  st_check() { # st_check <id> <file> <token> <detail>
    if status_of "$2" "$3"; then check "$1" 1 "$4"; else check "$1" 0 "$4"; fi
  }
  st_check AC01-status-disco-cheio disco-cheio.md "$EXEC" "index row for disco-cheio.md carries EXECUTAVEL (acute A)"
  st_check AC01-status-restaurar-backup restaurar-backup.md "PARCIAL" "index row for restaurar-backup.md carries PARCIAL"
  st_check AC01-status-rotacionar-chave rotacionar-chave.md "$T_PEND" "index row for rotacionar-chave.md carries PENDENTE-fase"
  st_check AC01-status-peer-entrega-travada peer-entrega-travada.md "$T_PEND" "index row for peer-entrega-travada.md carries PENDENTE-fase"
  st_check AC01-status-dlq-cheia dlq-cheia.md "$T_PEND" "index row for dlq-cheia.md carries PENDENTE-fase"
  if grep -qF 'sdd-test.ps1 test' "$RB/README.md"; then
    check AC01-gate-pointer 1 "README points at the local gate (sdd-test.ps1 test)"
  else
    check AC01-gate-pointer 0 "README has no sdd-test.ps1 test pointer"
  fi
else
  for id in status-disco-cheio status-restaurar-backup status-rotacionar-chave status-peer-entrega-travada status-dlq-cheia gate-pointer; do
    check "AC01-$id" 0 "README.md missing"
  done
fi

# ---------------------------------------------------------------- AC-02 sections + PENDENTE structure
for f in $EXPECTED; do
  if [ ! -f "$RB/$f" ]; then check "AC02-sections-${f%.md}" 0 "file missing"; continue; fi
  MISSINGSEC=""
  for h in "$H_SINT" "$H_DIAG" "$H_PROC" "$H_VERI"; do
    grep -Fxq "$h" "$RB/$f" || MISSINGSEC="$MISSINGSEC [$(echo "$h" | sed 's/[^ -~]/./g')]"
  done
  if [ -z "$MISSINGSEC" ]; then check "AC02-sections-${f%.md}" 1 "4 required sections present"
  else check "AC02-sections-${f%.md}" 0 "missing:$MISSINGSEC"; fi
done

for f in $PENDENTES; do
  if [ ! -f "$RB/$f" ]; then check "AC02-pendente-${f%.md}" 0 "file missing"; continue; fi
  numbered=$(grep -cE '^[[:space:]]*[0-9]+\.[[:space:]]' "$RB/$f")
  fences=$(grep -cF '```' "$RB/$f")
  execmark=$(grep -cF "$EXEC" "$RB/$f")
  cmdlike=$(grep -o '`[^`]*`' "$RB/$f" | grep -cE '^(podman|curl|df|wsl)([[:space:]]|$|\.)')
  pointer=0; grep -qF 'Ponteiros:' "$RB/$f" && pointer=1
  haspend=0; grep -qF 'PENDENTE' "$RB/$f" && haspend=1
  if [ "$numbered" -eq 0 ] && [ "$fences" -eq 0 ] && [ "$execmark" -eq 0 ] && [ "$cmdlike" -eq 0 ] && [ "$pointer" -eq 1 ] && [ "$haspend" -eq 1 ]; then
    check "AC02-pendente-${f%.md}" 1 "no-step proof: numbered=0 fences=0 execMarks=0 commandLike=0 pointer=1 PENDENTE=1"
  else
    check "AC02-pendente-${f%.md}" 0 "numbered=$numbered fences=$fences execMarks=$execmark commandLike=$cmdlike pointer=$pointer PENDENTE=$haspend"
  fi
done

# ---------------------------------------------------------------- AC-03 classification (static half)
OFFENDERS=""
NAO_LISTED=""
SEEN=""
for f in $RB2; do
  [ -f "$RB/$f" ] || { OFFENDERS="$OFFENDERS $f:FILE-MISSING"; continue; }
  n=0
  while IFS= read -r ln; do
    n=$((n + 1))
    nao_here=0
    case "$ln" in *"$NAO"*) nao_here=1 ;; esac
    rest="$ln"
    while [ "${rest#*\`}" != "$rest" ]; do
      seg="${rest#*\`}"; rest="${rest#*\`}"
      case "$seg" in *\`*) seg="${seg%%\`*}" ;; *) seg="" ;; esac
      [ -z "$seg" ] && continue
      echo "$seg" | grep -qE '^(podman|curl|df|wsl)([[:space:]]|$|\.)' || continue
      SEEN="$SEEN
$seg"
      if in_list "$seg" "$PLANNED_EXEC"; then continue; fi
      if [ "$nao_here" -eq 1 ]; then NAO_LISTED="$NAO_LISTED $f:$n:$seg"; continue; fi
      if in_list "$seg" "$MENTIONS"; then continue; fi
      OFFENDERS="$OFFENDERS $f:$n:$seg"
    done
  done < "$RB/$f"
done
if [ -z "${OFFENDERS// }" ]; then
  check AC03-classify 1 "every command-like segment classified (executed | NAO-executar | known mention); offenders=0"
else
  check AC03-classify 0 "offenders:$OFFENDERS"
fi
STALE=""
while IFS= read -r s; do
  [ -z "$s" ] && continue
  found=0
  for f in $RB2; do
    if [ -f "$RB/$f" ] && grep -qF -- "$s" "$RB/$f"; then found=1; break; fi
  done
  [ "$found" -eq 0 ] && STALE="$STALE |$s"
done <<EOF
$(printf '%s' "$PLANNED_EXEC" | tr '|' '\n')
EOF
if [ -z "$STALE" ]; then check AC03-stale 1 "all $(printf '%s\n' "$PLANNED_EXEC" | tr '|' '\n' | wc -l) executed strings appear in the 2 runbooks"
else check AC03-stale 0 "missing:$STALE"; fi
check AC03-nao-listed 1 "destructive on NAO-executar lines (never executed by AC-03 rule):$(echo $NAO_LISTED)"
if [ -f "$RB/restaurar-backup.md" ] && grep -qF "$T_TRECHO" "$RB/restaurar-backup.md"; then
  check AC03-trecho 1 "restaurar-backup.md declares its executable trecho (ciclo arquivo -> volume)"
else
  check AC03-trecho 0 "executable trecho heading missing in restaurar-backup.md"
fi

# ---------------------------------------------------------------- AC-04 down -v context + secrets + paths
DOWNV=""
DOWNV_BAD=""
for f in $EXPECTED; do
  [ -f "$RB/$f" ] || continue
  n=0
  while IFS= read -r ln; do
    n=$((n + 1))
    case "$ln" in
      *'down -v'*)
        if echo "$ln" | grep -qiE "$CTX_PAT"; then
          DOWNV="$DOWNV $f:$n=destructive"
        else
          DOWNV="$DOWNV $f:$n=NO-CONTEXT"; DOWNV_BAD="$DOWNV_BAD $f:$n"
        fi ;;
    esac
  done < "$RB/$f"
done
if [ -z "$DOWNV_BAD" ]; then check AC04-downv 1 "'down -v' all in destructive context:$(echo $DOWNV)"
else check AC04-downv 0 "'down -v' outside destructive context:$DOWNV_BAD"; fi

RAW=""
for f in $EXPECTED; do [ -f "$RB/$f" ] && RAW="$RAW
$(cat "$RB/$f")"; done

SECRETS=0
for spec in \
  '-i\btoken\b' '-i\bpassword\b' '-i\bpasswd\b' '-i\bsecrets?\b' '-i\bapi[_-]?key\b' \
  '-i\bcredentials?\b' '-i\bbearer\s+[A-Za-z0-9._-]{15,}' '-i\b(chave|key|token|secret|password)\s*[:=]\s*\S' \
  '\bgh[pousr]_[A-Za-z0-9]{30,}' '\bAKIA[0-9A-Z]{16}\b' '-----BEGIN [A-Z ]*PRIVATE KEY' \
  '\b[0-9a-fA-F]{40,}\b' '[A-Za-z0-9+/]{70,}={0,2}'; do
  case "$spec" in -i*) flags='-i'; pat="${spec#-i}" ;; *) flags=''; pat="$spec" ;; esac
  c=$(printf '%s\n' "$RAW" | grep -cE $flags -- "$pat" 2>/dev/null || true)
  SECRETS=$((SECRETS + ${c:-0}))
done
if [ "${SECRETS:-0}" -eq 0 ]; then check AC04-secrets 1 "secret/token pattern hits=0 patterns=13"
else check AC04-secrets 0 "secret/token pattern hits=$SECRETS"; fi

PATHS=0
for pat in '(^|[^[:alnum:]])[A-Za-z]:[/\\]' '(^|[^[:alnum:]:])\\\\[A-Za-z0-9._-]+\\[A-Za-z0-9]' '/Users/[A-Za-z0-9]' '/home/[A-Za-z0-9]+/' '/mnt/c/Users/'; do
  c=$(printf '%s\n' "$RAW" | grep -cE "$pat" 2>/dev/null || true)
  PATHS=$((PATHS + ${c:-0}))
done
if [ "${PATHS:-0}" -eq 0 ]; then check AC04-paths 1 "personal absolute path hits=0 patterns=5"
else check AC04-paths 0 "personal absolute path hits=$PATHS"; fi

# ---------------------------------------------------------------- AC-05 delta of the change
GIT="git -C $REPO_ROOT -c safe.directory=$REPO_ROOT"
REF_EPOCH="$(date -d "$REF_TIME" +%s 2>/dev/null || echo 0)"
ST="$($GIT status --porcelain 2>/dev/null)"
ST_RC=$?
if [ "$ST_RC" -ne 0 ]; then
  check AC05-delta 0 "git status failed (exit $ST_RC)"
else
  OUTSIDE=""
  PRODUCT=""
  FLOW=""
  PRE=0
  while IFS= read -r line; do
    [ -z "$line" ] && continue
    path="${line#???}"
    path="${path//\"/}"
    full="$REPO_ROOT/$path"
    if [ -d "$full" ]; then targets="$(cd "$REPO_ROOT" && find "$path" -type f 2>/dev/null)"
    elif [ -f "$full" ]; then targets="$path"
    else targets="$path"; fi
    for t in $targets; do
      tf="$REPO_ROOT/$t"
      m=0
      [ -f "$tf" ] && m="$(stat -c %Y "$tf" 2>/dev/null || echo 0)"
      [ "$m" -eq 0 ] && m="$REF_EPOCH"
      if [ "$m" -lt "$REF_EPOCH" ]; then PRE=$((PRE + 1)); continue; fi
      case "$t" in
        docs/runbooks/*) PRODUCT="$PRODUCT $t" ;;
        docs/sdd/*)      FLOW="$FLOW $t" ;;
        *)               OUTSIDE="$OUTSIDE $t" ;;
      esac
    done
  done <<EOF
$ST
EOF
  if [ -z "${OUTSIDE// }" ]; then check AC05-delta 1 "files touched after the window outside docs/runbooks and docs/sdd = 0"
  else check AC05-delta 0 "outside:$OUTSIDE"; fi
  info AC05-delta-product "task product delta:$(echo $PRODUCT | tr ' ' '\n' | sort -u | tr '\n' ' ')"
  info AC05-delta-flow "flow artifacts under docs/sdd (allowed, listed):$(echo $FLOW | tr ' ' '\n' | sort -u | tr '\n' ' ')"
  info AC05-delta-pre "pre-existing entries from TASK-0003..0009 outside the window (not findings): $PRE"
fi

# ---------------------------------------------------------------- platform limits, stated as such
info AC03-execution "NOT executed on this platform: podman lifecycle (WSL podman is a different, empty engine - no infra_squid-data) and the local gate (needs Windows PowerShell 5.1 + dotnet). The Linux string forms df -h and curl -fsS are executed verbatim in WSL as separate recorded commands. Never assumed green."
info AC03-static-note "classification here is byte-identical to the Windows checker: every command-like segment must be in the executed list, on a NAO-executar line, or a known tool mention."

echo "SUMMARY total=$TOTAL fails=$FAILS"
if [ "$FAILS" -eq 0 ]; then
  echo "RESULT: PASS - all executed checks green"
  exit 0
fi
echo "RESULT: FAIL - $FAILS of $TOTAL checks failed"
exit 1
