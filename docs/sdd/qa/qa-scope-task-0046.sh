#!/usr/bin/env bash
# QA-0046 scope scan (sh twin of qa-scope-task-0046.ps1): the same three proofs from git -
# DEV commit inside the touches, working tree clean outside docs/sdd/qa + the ODEV task-file
# WIP, no mutation residue, HEAD-blob identity of the four mutated files, and exactly four
# .cs files mtime-newer than the QA baseline. Run from any shell: the script cds to the repo
# root itself. ASCII-only on purpose.
# Usage: bash docs/sdd/qa/qa-scope-task-0046.sh   (expected: exit 0, fora-dos-touches=0)
set -u

here="$(cd "$(dirname "$0")" && pwd)"
cd "$(cd "$here/../../.." && pwd)" || exit 1

out='docs/sdd/qa/QA-0046-scope-sh.log'
baseline='docs/sdd/qa/QA-0046-git-baseline.txt'
taskfile='docs/sdd/tasks/TASK-0046-peer-grain-fila-e-dlq.md'
mutated='backend/src/Squid.Grains/Peers/PeerGrain.cs
backend/src/Squid.Service/PeerDeliveryPump.cs
backend/src/Squid.Service/OutboundPipeline.cs
backend/src/Squid.PeerClient/PeerScenarioServer.cs'

: > "$out"
fails=0

say() { printf '%s\n' "$1" | tee -a "$out"; }

in_touches() {
    case "$1" in
        backend/src/Squid.Grains/Peers/* | \
        backend/src/Squid.PeerClient/* | \
        backend/src/Squid.Service/* | \
        backend/src/Squid.ReadModels/* | \
        backend/tests/Squid.Tests/*) return 0 ;;
        *) return 1 ;;
    esac
}

say "HEAD=$(git rev-parse HEAD)"
say "branch=$(git rev-parse --abbrev-ref HEAD)"

# 1. The DEV commit: every file inside the five glob sets of the touches.
commit_files=0
outside_commit=0
while IFS= read -r f; do
    [ -z "$f" ] && continue
    commit_files=$((commit_files + 1))
    if ! in_touches "$f"; then
        say "COMMIT-OUTSIDE $f"
        outside_commit=$((outside_commit + 1))
        fails=$((fails + 1))
    fi
done < <(git show --name-only --format= HEAD)
say "commit-files=$commit_files commit-fora-dos-touches=$outside_commit"

# 2. Working tree after this QA: qa artifacts + the ODEV's own task-file WIP are the only
#    entries allowed outside the touches; backend itself must be empty in git status.
qa=0
wip=0
outside_wt=0
while IFS= read -r e; do
    [ -z "$e" ] && continue
    p=$(printf '%s' "$e" | sed -E 's/^[ MADRCU?!]{1,2}[[:space:]]*//')
    case "$p" in
        docs/sdd/qa/*) qa=$((qa + 1)) ;;
        "$taskfile") wip=$((wip + 1)) ;;
        *)
            if ! in_touches "$p"; then
                say "WORKTREE-OUTSIDE $p"
                outside_wt=$((outside_wt + 1))
                fails=$((fails + 1))
            fi
            ;;
    esac
done < <(git status --porcelain -uall)
say "git-status qa-artifacts=$qa wip-odev=$wip fora-dos-touches=$outside_wt"

backend_entries=$(git status --porcelain -uall -- backend | grep -c . || true)
say "git-backend-entries=$backend_entries"
if [ "$backend_entries" -ne 0 ]; then
    git status --porcelain -uall -- backend | sed 's/^/BACKEND /' | tee -a "$out"
    fails=$((fails + 1))
fi

# 3. No mutation token survived the 14 restores.
residue=$(grep -rn 'QA-0046 T' backend --include='*.cs' 2>/dev/null | grep -Ev '/(bin|obj)/' | grep -c . || true)
say "residue=$residue"
if [ "$residue" -ne 0 ]; then
    grep -rn 'QA-0046 T' backend --include='*.cs' 2>/dev/null | grep -Ev '/(bin|obj)/' | sed 's/^/RESIDUE /' | tee -a "$out"
    fails=$((fails + 1))
fi

# 4. Every mutated product file is byte-identical to its HEAD blob (S-16).
blob_ok=0
blob_total=0
while IFS= read -r f; do
    [ -z "$f" ] && continue
    blob_total=$((blob_total + 1))
    work=$(git hash-object "$f")
    head_blob=$(git rev-parse "HEAD:$f")
    if [ "$work" = "$head_blob" ]; then
        say "blob $f match=TRUE"
        blob_ok=$((blob_ok + 1))
    else
        say "blob $f match=FALSE"
        fails=$((fails + 1))
    fi
done <<EOF
$mutated
EOF
say "blob-identity=$blob_ok of $blob_total"

# 5. Exactly the four mutated files carry an mtime newer than the QA baseline (S-23).
mtime_list=$(find backend -name '*.cs' -newer "$baseline" ! -path '*/bin/*' ! -path '*/obj/*' 2>/dev/null | sort)
mtime_count=0
while IFS= read -r f; do
    [ -z "$f" ] && continue
    mtime_count=$((mtime_count + 1))
    say "MTIME $f"
done <<< "$mtime_list"
say "mtime-count=$mtime_count (esperado 4)"
if [ "$mtime_count" -ne 4 ]; then
    fails=$((fails + 1))
fi

outside_total=$((outside_commit + outside_wt))
say "fora-dos-touches=$outside_total"
if [ "$outside_total" -ne 0 ]; then
    fails=$((fails + 1))
fi
say "SUMMARY commit-files=$commit_files qa-artifacts=$qa backend-entries=$backend_entries residue=$residue blob=$blob_ok/$blob_total mtime=$mtime_count fails=$fails"

if [ "$fails" -gt 0 ]; then exit 1; fi
exit 0
