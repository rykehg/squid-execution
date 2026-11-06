#!/usr/bin/env bash
# sdd-state — the only writer of the SDD canonical task state (Linux / macOS / WSL).
#
# Same commands, guards and exit codes as sdd-state.ps1. Under WSL pass the Windows path in
# /mnt form: /mnt/c/Users/<you>/Repos/<repo>/docs/sdd/tasks/TASK-0002-*.md
#
# Usage:
#   sdd-state.sh append-log      <task> <role> < lines.txt
#   sdd-state.sh append-evidence <task> <role> <command> <exit> <artifact>
#   sdd-state.sh set             <task> <field> <value>
#   sdd-state.sh check           <task>
#
# Exit codes: 0 ok · 1 usage · 2 state/file problem · 3 refused by a guard.
set -u

die() { printf 'sdd-state: %s\n' "$2" >&2; exit "$1"; }

cmd=${1:-}
task=${2:-}

[ -n "$cmd" ] || die 1 "usage: sdd-state.sh <append-log|append-evidence|set|check> <task> [...]"
[ -n "$task" ] || die 1 "task file missing"
[ -f "$task" ] || die 2 "task file not found: $task"

stamp=$(date '+%Y-%m-%d %H:%M:%S') || die 2 "cannot read the clock"

commit() { mv -f "$1" "$task" || die 2 "atomic write failed"; }

# any C0 control character is refused: tab (09) and form feed (0C) are what an unescaped
# backtick produces in PowerShell, and a CRLF file must not trip this check
no_ctrl() { # $1 = value, $2 = what
  if printf '%s' "$1" | tr -d '\r' | LC_ALL=C grep -q '[[:cntrl:]]'; then
    die 3 "$2 contains a control character (tab=09, form feed=0C). Pass the text via stdin and drop the backtick escape."
  fi
}

no_pipe() { # $1 = value, $2 = what
  no_ctrl "$1" "$2"
  case $1 in *'|'*) die 3 "$2 contains '|', which would break the evidence table" ;; esac
}

yaml_bounds() { # prints "<start> <end>", 1-based inclusive, of the yaml block body
  awk '
    /^```yaml[[:space:]]*$/ && start==0 { start=NR+1; next }
    start>0 && /^```[[:space:]]*$/ { print start" "(NR-1); found=1; exit }
    END { if(!found) exit 1 }
  ' "$task" || die 2 "no \`\`\`yaml ... \`\`\` control block found"
}

section_line() { awk -v h="$1" '$0==h {print NR; exit}' "$task"; }

case $cmd in

append-log)
  role=${3:-}
  [ -n "$role" ] || die 1 "append-log needs a role (odev|dev|qa|humano)"
  case $role in *[!a-z_-]*) die 1 "role must be a lowercase slug, got '$role'" ;; esac

  body=$(cat) || die 1 "cannot read stdin"
  [ -n "$body" ] || die 1 "append-log needs text on stdin"
  no_ctrl "$body" "log text"

  ev=$(section_line '## Evidence')
  log=$(section_line '## Log')
  if [ -n "$ev" ] && [ -n "$log" ] && [ "$log" -gt "$ev" ]; then
    die 2 "## Log sits after ## Evidence; fix the section order by hand first"
  fi

  tmp="$task.sddtmp"; tmpb="$task.sddlines"
  printf '%s\n' "$body" | awk -v stamp="$stamp" -v role="$role" '
    { line=$0; sub(/\r$/,"",line); if (line ~ /^[ \t]*$/) next; printf "- %s | %s | %s\n", stamp, role, line }
  ' > "$tmpb"
  [ -s "$tmpb" ] || { rm -f "$tmpb"; die 1 "append-log got no non-empty line"; }
  no_ctrl "$(cat "$tmpb")" "generated log lines"

  awk -v blockfile="$tmpb" '
    { lines[NR]=$0 }
    END {
      ev=0
      for (i=1;i<=NR;i++) if (lines[i]=="## Evidence") { ev=i; break }
      if (ev==0) {
        for (i=1;i<=NR;i++) print lines[i]
        while ((getline l < blockfile) > 0) print l
        close(blockfile); exit
      }
      last=0
      for (i=1;i<ev;i++) if (lines[i] ~ /[^ \t]/) last=i
      for (i=1;i<=last;i++) print lines[i]
      while ((getline l < blockfile) > 0) print l
      close(blockfile)
      print ""
      for (i=ev;i<=NR;i++) print lines[i]
    }
  ' "$task" > "$tmp" || { rm -f "$tmp" "$tmpb"; die 2 "failed to insert log lines"; }
  commit "$tmp"; rm -f "$tmpb"
  echo "append-log: $(wc -l < "$task" | tr -d ' ') lines now in $(basename "$task")"
  ;;

append-evidence)
  role=${3:-}; command_=${4:-}; code=${5:-}; artifact=${6:-}
  [ -n "$role" ] && [ -n "$command_" ] && [ -n "$code" ] && [ -n "$artifact" ] \
    || die 1 "append-evidence needs: <role> <command> <exit> <artifact>"
  case $role in *[!a-z_-]*) die 1 "role must be a lowercase slug, got '$role'" ;; esac
  case $code in -[0-9]*|[0-9]*|NAO_COLETADO|n/a) : ;; *) die 1 "exit must be an integer or NAO_COLETADO, got '$code'" ;; esac
  no_pipe "$command_" "command"
  no_pipe "$artifact" "artifact"

  ev=$(section_line '## Evidence')
  [ -n "$ev" ] || die 2 "no ## Evidence section"

  tmp="$task.sddtmp"
  awk -v ev="$ev" -v row="| $stamp | $role | $command_ | $code | $artifact |" '
    { lines[NR]=$0 }
    END {
      last=0
      for (i=ev+1;i<=NR;i++) if (lines[i] ~ /^[ \t]*\|/) last=i
      if (last==0) { print "sdd-state: ## Evidence has no table row" > "/dev/stderr"; exit 2 }
      for (i=1;i<=last;i++) print lines[i]
      print row
      for (i=last+1;i<=NR;i++) print lines[i]
    }
  ' "$task" > "$tmp" || { rm -f "$tmp"; die 2 "failed to append evidence row"; }
  commit "$tmp"
  echo "append-evidence: 1 row -> $(basename "$task")"
  ;;

set)
  field=${3:-}; value=${4-}
  [ -n "$field" ] || die 1 "set needs <field> <value>"
  no_ctrl "$value" "value of $field"

  bounds=$(yaml_bounds) || exit 2
  s=${bounds% *}; e=${bounds#* }

  hits=$(awk -v s="$s" -v e="$e" -v f="$field" '
    NR>=s && NR<=e && $0 ~ ("^[ \t]*" f "[ \t]*:") { n++ } END { print n+0 }' "$task")
  [ "$hits" -eq 0 ] && die 2 "field '$field' not found inside the yaml block"
  [ "$hits" -gt 1 ] && die 2 "field '$field' appears $hits times inside the yaml block; use a longer unique name"

  tmp="$task.sddtmp"
  awk -v s="$s" -v e="$e" -v f="$field" -v v="$value" '
    NR>=s && NR<=e && done!=1 && $0 ~ ("^[ \t]*" f "[ \t]*:") {
      match($0, "^[ \t]*")
      print substr($0, 1, RLENGTH) f ": " v
      done=1
      next
    }
    { print }
  ' "$task" > "$tmp" || { rm -f "$tmp"; die 2 "failed to set $field"; }
  commit "$tmp"
  echo "set: $field = $value"
  ;;

check)
  fails=0
  note() { printf 'check: FAIL - %s\n' "$1"; fails=$((fails+1)); }

  if bounds=$(yaml_bounds 2>/dev/null); then
    s=${bounds% *}; e=${bounds#* }
    for k in runId taskId status sequence updatedAt nextAction; do
      n=$(awk -v s="$s" -v e="$e" -v k="$k" 'NR>=s && NR<=e && $0 ~ ("^[ \t]*" k ":") { n++ } END { print n+0 }' "$task")
      if [ "$n" -eq 0 ]; then note "missing required field: $k"; fi
      if [ "$n" -gt 1 ]; then note "duplicated field inside yaml block: $k ($n)"; fi
    done
  else
    note "no \`\`\`yaml control block"
  fi

  logl=$(section_line '## Log'); evl=$(section_line '## Evidence')
  [ -z "$logl" ] && note "no ## Log section"
  [ -z "$evl" ] && note "no ## Evidence section"
  if [ -n "$logl" ] && [ -n "$evl" ] && [ "$logl" -gt "$evl" ]; then note "## Log comes after ## Evidence"; fi

  # control characters anywhere, ignoring CR so a CRLF file does not trip it
  if tr -d '\r' < "$task" | LC_ALL=C grep -q '[[:cntrl:]]'; then
    ln=$(tr -d '\r' < "$task" | LC_ALL=C grep -n '[[:cntrl:]]' | head -1 | cut -d: -f1)
    note "control character at line $ln"
  fi

  entries=0
  if [ -n "$logl" ]; then
    logend=${evl:-$(( $(wc -l < "$task") + 1 ))}
    out=$(awk -v s="$logl" -v e="$logend" '
      NR>s && NR<e && /^- / {
        if ($0 ~ /^- ([0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9] [0-9][0-9]:[0-9][0-9]:[0-9][0-9]|NAO_COLETADO) \| [a-z_-]+ \| [^ ]/) n++
        else print "malformed log line " NR
      }
      END { print "COUNT " n+0 }
    ' "$task")
    printf '%s\n' "$out" | grep '^malformed' | while read -r l; do printf 'check: FAIL - %s\n' "$l"; done
    m=$(printf '%s\n' "$out" | grep -c '^malformed' || true)
    fails=$((fails + m))
    entries=$(printf '%s\n' "$out" | awk '/^COUNT/ {print $2}')
  fi

  if [ -n "$evl" ]; then
    after=$(awk -v e="$evl" 'NR>e && /^- ([0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9] [0-9][0-9]:[0-9][0-9]:[0-9][0-9]|NAO_COLETADO) \| [a-z_-]+ \|/ { print NR }' "$task" | head -1)
    [ -n "$after" ] && note "log entry after ## Evidence at line $after"
    bad=$(awk -v e="$evl" -F"|" 'NR>e && /^[ \t]*\|/ && NF!=7 { print NR }' "$task" | head -3)
    [ -n "$bad" ] && note "evidence row with wrong column count at line $(echo "$bad" | head -1)"
  fi

  rows=$(awk -v e="${evl:-0}" 'NR>e && /^[ \t]*\|/ { n++ } END { print n+0 }' "$task")
  if [ "$fails" -eq 0 ]; then
    echo "check: OK - log entries ${entries:-0}, table rows $rows"
    exit 0
  fi
  exit 3
  ;;

*)
  die 1 "unknown command: $cmd"
  ;;
esac