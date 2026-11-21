#!/usr/bin/env bash
# QA-0054 scope scan (sh twin of qa-scope-task-0054.ps1): the same proofs from git - tested
# state is HEAD 28dcab9 on the task branch, DEV commit inside the touches, diff develop...HEAD
# adds no package line and leaves backend/Directory.Packages.props alone (zero new NuGet),
# working tree clean outside docs/sdd (QA artifacts + the ODEV's own WIP recorded in the
# baseline), no mutation residue, HEAD-blob identity of the five mutated files, and exactly
# five .cs/.sql files mtime-newer than the QA baseline. Run from any shell: the script cds to
# the repo root itself. ASCII-only on purpose.
# Usage: bash docs/sdd/qa/qa-scope-task-0054.sh   (expected: exit 0, fora-dos-touches=0)
set -u

here="$(cd "$(dirname "$0")" && pwd)"
cd "$(cd "$here/../../.." && pwd)" || exit 1

out='docs/sdd/qa/QA-0054-scope-sh.log'
baseline='docs/sdd/qa/QA-0054-git-baseline.txt'
expected_head='28dcab9aee197bd1a76a0545c4674ed0c0916c38'
expected_branch='task/TASK-0054-nucleo-backup-cifrado'
mutated='backend/src/Squid.Persistence/schema/0010_backup.sql
backend/src/Squid.Worker/Backup/LocalDiskTarget.cs
backend/src/Squid.Service/SquidServiceHost.cs
backend/src/Squid.Worker/Backup/BackupPipeline.cs
backend/src/Squid.Worker/Backup/SqliteBackupEnvelopeStore.cs'

: > "$out"
fails=0

say() { printf '%s\n' "$1" | tee -a "$out"; }

in_touches() {
    case "$1" in
        backend/src/Squid.Persistence/schema/* | \
        backend/src/Squid.Api/Crypto/* | \
        backend/src/Squid.Worker/* | \
        backend/src/Squid.Service/* | \
        backend/tests/Squid.Tests/Backup/*) return 0 ;;
        *) return 1 ;;
    esac
}

# 1. Identity of the tree: the tested state is the DEV commit on the task branch.
head=$(git rev-parse HEAD)
branch=$(git rev-parse --abbrev-ref HEAD)
say "HEAD=$head"
say "branch=$branch"
if [ "$head" != "$expected_head" ]; then
    say "HEAD-WANT $expected_head"; fails=$((fails + 1))
fi
if [ "$branch" != "$expected_branch" ]; then
    say "BRANCH-WANT $expected_branch"; fails=$((fails + 1))
fi

# 2. The DEV commit: every file inside the five glob sets of the touches.
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

# 3. Zero new NuGet: no PackageReference/PackageVersion line added by the diff, and
#    backend/Directory.Packages.props untouched.
pkg_props=$(git diff --name-only 'develop...HEAD' -- backend/Directory.Packages.props | grep -c . || true)
pkg_added=$(git diff 'develop...HEAD' | grep -Ec '^\+[^+].*(<PackageReference|<PackageVersion)' || true)
say "nuget directory-packages-props-changed=$pkg_props added-package-lines=$pkg_added"
if [ "$pkg_props" -ne 0 ]; then
    git diff --name-only 'develop...HEAD' -- backend/Directory.Packages.props | sed 's/^/NUGET-PROPS /' | tee -a "$out"
    fails=$((fails + 1))
fi
if [ "$pkg_added" -ne 0 ]; then
    git diff 'develop...HEAD' | grep -E '^\+[^+].*(<PackageReference|<PackageVersion)' | sed 's/^/NUGET-ADDED /' | tee -a "$out"
    fails=$((fails + 1))
fi

# 4. Working tree after this QA: qa artifacts + the ODEV's own docs/sdd WIP are the only
#    entries allowed outside the touches; backend itself must be empty in git status.
qa=0
wip=0
outside_wt=0
while IFS= read -r e; do
    [ -z "$e" ] && continue
    p=$(printf '%s' "$e" | sed -E 's/^[ MADRCU?!]{1,2}[[:space:]]*//')
    case "$p" in
        docs/sdd/qa/*) qa=$((qa + 1)) ;;
        docs/sdd/*)
            wip=$((wip + 1))
            say "WIP-ODEV $p"
            ;;
        *)
            if ! in_touches "$p"; then
                say "WORKTREE-OUTSIDE $p"
                outside_wt=$((outside_wt + 1))
                fails=$((fails + 1))
            fi
            ;;
    esac
done < <(git -c core.quotepath=false status --porcelain -uall)
say "git-status qa-artifacts=$qa wip-odev=$wip fora-dos-touches=$outside_wt"

backend_entries=$(git status --porcelain -uall -- backend | grep -c . || true)
say "git-backend-entries=$backend_entries"
if [ "$backend_entries" -ne 0 ]; then
    git status --porcelain -uall -- backend | sed 's/^/BACKEND /' | tee -a "$out"
    fails=$((fails + 1))
fi

# 5. No mutation token survived the restores.
residue=$(grep -rn 'QA-0054 T' backend --include='*.cs' --include='*.sql' 2>/dev/null | grep -Ev '/(bin|obj)/' | grep -c . || true)
say "residue=$residue"
if [ "$residue" -ne 0 ]; then
    grep -rn 'QA-0054 T' backend --include='*.cs' --include='*.sql' 2>/dev/null | grep -Ev '/(bin|obj)/' | sed 's/^/RESIDUE /' | tee -a "$out"
    fails=$((fails + 1))
fi

# 6. Every mutated product file is byte-identical to its HEAD blob (S-16).
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

# 7. Exactly the five mutated files carry an mtime newer than the QA baseline (S-23).
mtime_list=$(find backend \( -name '*.cs' -o -name '*.sql' \) -newer "$baseline" ! -path '*/bin/*' ! -path '*/obj/*' 2>/dev/null | sort)
mtime_count=0
while IFS= read -r f; do
    [ -z "$f" ] && continue
    mtime_count=$((mtime_count + 1))
    say "MTIME $f"
done <<< "$mtime_list"
say "mtime-count=$mtime_count (esperado 5)"
if [ "$mtime_count" -ne 5 ]; then
    fails=$((fails + 1))
fi

outside_total=$((outside_commit + outside_wt))
say "fora-dos-touches=$outside_total"
if [ "$outside_total" -ne 0 ]; then
    fails=$((fails + 1))
fi
say "SUMMARY head=${head:0:7} commit-files=$commit_files pkg-props=$pkg_props pkg-added=$pkg_added qa-artifacts=$qa wip-odev=$wip backend-entries=$backend_entries residue=$residue blob=$blob_ok/$blob_total mtime=$mtime_count fails=$fails"

if [ "$fails" -gt 0 ]; then exit 1; fi
exit 0
