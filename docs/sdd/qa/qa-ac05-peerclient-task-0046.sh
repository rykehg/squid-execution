#!/usr/bin/env bash
# QA-0046 AC-05 probe (sh twin of qa-ac05-peerclient-task-0046.ps1): the same five checks from
# a POSIX shell - the CLI --help exposes the 500/timeout/slow tokens, and the three scenarios
# really answer 500 / never answer (curl exit 28) / answer only after --delay=1500. Throwaway
# loopback servers, every wait bounded with timeout(1) or a deadline loop, every process tree
# killed in the same run (trap on EXIT). Needs dotnet on PATH: this twin runs under Git Bash
# on this machine (WSL has no dotnet at all - see the QA doc's unverified list).
# ASCII-only on purpose.
# Usage: bash docs/sdd/qa/qa-ac05-peerclient-task-0046.sh   (expected: exit 0, SUMMARY fails=0)
set -u
export MSBUILDDISABLENODEREUSE=1

here="$(cd "$(dirname "$0")" && pwd)"
cd "$(cd "$here/../../.." && pwd)" || exit 1
proj="$PWD/backend/src/Squid.PeerClient"
log='docs/sdd/qa/QA-0046-ac05-cli-sh.log'
tmpdir="$(mktemp -d)"
start_epoch=$(date +%s)

: > "$log"
checks=0
fails=0
server_pid=''
server_out="$tmpdir/server.out"

say() { printf '%s\n' "$1" | tee -a "$log"; }

check() { # name result(0=ok) detail
    checks=$((checks + 1))
    if [ "$2" -eq 0 ]; then
        say "CHECK $1 OK - $3"
    else
        fails=$((fails + 1))
        say "CHECK $1 FAIL - $3"
    fi
}

kill_tree() { # pid
    [ -z "$1" ] && return 0
    MSYS2_ARG_CONV_EXCL='*' taskkill /T /F /PID "$1" >/dev/null 2>&1 || true
}

cleanup() {
    kill_tree "$server_pid"
    MSYS2_ARG_CONV_EXCL='*' taskkill /F /IM Squid.PeerClient.exe >/dev/null 2>&1 || true
    rm -rf "$tmpdir"
}
trap cleanup EXIT

start_server() { # scenario args...; sets server_pid and port
    : > "$server_out"
    dotnet run --project "$proj" -- "$@" >"$server_out" 2>&1 &
    server_pid=$!
    port=''
    deadline=$(( $(date +%s) + 90 ))
    while [ "$(date +%s)" -lt "$deadline" ]; do
        port=$(grep -o 'listening on http://127\.0\.0\.1:[0-9]*' "$server_out" 2>/dev/null |
            head -n 1 | grep -o '[0-9]*$')
        [ -n "$port" ] && return 0
        kill -0 "$server_pid" 2>/dev/null || return 1
        sleep 0.25
    done
    return 1
}

post() { # port max_time_seconds; sets post_code post_exit post_ms
    local now
    now=$(date +%s%3N)
    post_code=$(curl -s -o /dev/null -w '%{http_code}' --max-time "$2" -X POST \
        -H 'Content-Type: application/activity+json' --data '{}' \
        "http://127.0.0.1:$1/users/alice/inbox")
    post_exit=$?
    post_ms=$(( $(date +%s%3N) - now ))
}

say "probe: task=QA-0046 ac=AC-05 shell=git-bash project=$proj"

# 1. The CLI itself exposes the three scenario tokens.
help_out="$tmpdir/help.out"
timeout 120 dotnet run --project "$proj" -- --help >"$help_out" 2>&1
help_exit=$?
usage=$(tr -s '[:space:]' ' ' <"$help_out")
if [ "$help_exit" -eq 0 ]; then check HELP-EXIT 0 "dotnet run -- --help exit=$help_exit"
else check HELP-EXIT 1 "dotnet run -- --help exit=$help_exit (124 = timeout)"; fi
tokens_ok=0
printf '%s' "$usage" | grep -q '500' && printf '%s' "$usage" | grep -q 'timeout' &&
    printf '%s' "$usage" | grep -q 'slow' && tokens_ok=1
if [ "$tokens_ok" -eq 1 ]; then check HELP-TOKENS 0 "usage declara os cenarios 500/timeout/slow: $usage"
else check HELP-TOKENS 1 "usage sem os tres tokens: $usage"; fi

# 2. Scenario 500 - the peer answers HTTP 500 to the inbox POST.
if start_server '--scenario=500'; then
    post "$port" 10
    if [ "$post_exit" -eq 0 ] && [ "$post_code" = '500' ]; then
        check SCENARIO-500 0 "porta $port: http=$post_code curl-exit=$post_exit elapsed=${post_ms}ms"
    else
        check SCENARIO-500 1 "porta $port: http=$post_code curl-exit=$post_exit elapsed=${post_ms}ms"
    fi
else
    check SCENARIO-500 1 'o servidor 500 nao publicou a porta em 90s'
fi
kill_tree "$server_pid"; server_pid=''

# 3. Scenario timeout - the peer takes the request and never answers; curl gives up bounded.
if start_server '--scenario=timeout'; then
    post "$port" 2
    if [ "$post_exit" -eq 28 ] && [ "$post_code" = '000' ]; then
        check SCENARIO-TIMEOUT 0 "porta $port: curl-exit=$post_exit (28 = tempo esgotado sem resposta) http=$post_code elapsed=${post_ms}ms"
    else
        check SCENARIO-TIMEOUT 1 "porta $port: curl-exit=$post_exit http=$post_code elapsed=${post_ms}ms"
    fi
else
    check SCENARIO-TIMEOUT 1 'o servidor timeout nao publicou a porta em 90s'
fi
kill_tree "$server_pid"; server_pid=''

# 4. Scenario slow - the answer comes AFTER the delay the CLI itself was given.
if start_server '--scenario=slow' '--delay=1500'; then
    post "$port" 10
    if [ "$post_exit" -eq 0 ] && [ "$post_code" = '202' ] && [ "$post_ms" -ge 1200 ]; then
        check SCENARIO-SLOW 0 "porta $port: http=$post_code elapsed=${post_ms}ms (esperado >= 1200 pelo --delay=1500)"
    else
        check SCENARIO-SLOW 1 "porta $port: http=$post_code curl-exit=$post_exit elapsed=${post_ms}ms"
    fi
else
    check SCENARIO-SLOW 1 'o servidor slow nao publicou a porta em 90s'
fi
kill_tree "$server_pid"; server_pid=''

elapsed=$(( $(date +%s) - start_epoch ))
say "CLEANUP temporarios=${tmpdir} removidos no exit elapsed=${elapsed}s"
say "SUMMARY checks=$checks fails=$fails"

if [ "$fails" -gt 0 ]; then exit 1; fi
exit 0
