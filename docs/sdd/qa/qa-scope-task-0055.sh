#!/usr/bin/env bash
# QA-0055 scope scan, Linux twin: the same git-truth checks as qa-scope-task-0055.ps1
# (TASK-0055 AC-07) - one commit over develop, every committed file inside the touches set,
# zero new NuGet package, no residue under backend, the 8 mutated files byte-identical to
# their HEAD blobs and the only .cs newer than the HEAD commit. Read-only: it never writes to
# the product tree. Runs inside the local sdk image (git included) or any Linux with git:
#   podman run --rm -v <repo>:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-scope-task-0055.sh
set -u

cd "$(cd "$(dirname "$0")" && pwd)/../../.." || { echo "SCOPE cd failed"; exit 1; }

# The container runs as root over a bind mount owned by another uid: git then refuses the
# repository as "dubious ownership". Whitelist exactly this path for this process only
# (env-scoped config, nothing written anywhere) and abort honestly if git still refuses.
export GIT_CONFIG_COUNT=1
export GIT_CONFIG_KEY_0=safe.directory
export GIT_CONFIG_VALUE_0="$(pwd)"
if ! git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    echo "SCOPE: git nao reconhece o repositorio em $(pwd) (safe.directory) - abortando"
    exit 1
fi

fails=0
head=$(git rev-parse --short HEAD)
commits=$(git rev-list --count develop..HEAD)
echo "HEAD=$head commits-over-develop=$commits (esperado 1 commit bdf1ccf)"
[ "$commits" -eq 1 ] || fails=$((fails + 1))

touches='^(backend/src/Squid\.Grains/|backend/src/Squid\.Worker/|backend/src/Squid\.Api/Admin/|backend/src/Squid\.Service/|backend/tests/Squid\.Tests/Backup/)'
files=$(git diff --name-only develop...HEAD)
file_count=$(printf '%s\n' "$files" | grep -c .)
outside=$(printf '%s\n' "$files" | grep -Ev "$touches" | grep -c .)
echo "commit-files=$file_count outside-touches=$outside"
[ "$outside" -eq 0 ] || { fails=$((fails + 1)); printf '%s\n' "$files" | grep -Ev "$touches"; }

pkg_files=$(printf '%s\n' "$files" | grep -Ec '\.(csproj|props)$')
pkg_added=$(git diff develop...HEAD | grep -Ec '^\+.*<(PackageReference|PackageVersion)[[:space:]]' )
echo "pkg-files-in-commit=$pkg_files pkg-ref-added-lines=$pkg_added (esperado 0/0)"
[ "$pkg_files" -eq 0 ] && [ "$pkg_added" -eq 0 ] || fails=$((fails + 1))

residue=$(git status --porcelain -- backend | grep -c .)
untracked=$(git status --porcelain -uall -- backend | grep -c '^\?\?')
echo "backend-tracked-status=$residue backend-untracked=$untracked (esperado 0/0)"
[ "$residue" -eq 0 ] && [ "$untracked" -eq 0 ] || fails=$((fails + 1))

mutated=(
    backend/src/Squid.Worker/Backup/BackupRunner.cs
    backend/src/Squid.Worker/Backup/BackupOptions.cs
    backend/src/Squid.Worker/Backup/BackupScheduler.cs
    backend/src/Squid.Api/Admin/AdminEndpoints.cs
    backend/src/Squid.Api/Admin/BackupAlerter.cs
    backend/src/Squid.Grains/Backup/BackupCoordinatorGrain.cs
    backend/src/Squid.Worker/Backup/BackupPipeline.cs
    backend/src/Squid.Worker/Backup/LocalDiskTarget.cs
)
blob_ok=0
for f in "${mutated[@]}"; do
    work=$(git hash-object "$f")
    idx=$(git rev-parse "HEAD:$f")
    if [ "$work" = "$idx" ]; then
        blob_ok=$((blob_ok + 1))
    else
        echo "BLOB-MISMATCH file=$f work=$work head=$idx"
    fi
done
echo "blobs-identical=$blob_ok/${#mutated[@]} (esperado ${#mutated[@]}/${#mutated[@]})"
[ "$blob_ok" -eq "${#mutated[@]}" ] || fails=$((fails + 1))

commit_epoch=$(git log -1 --format=%ct)
newer_count=$(find backend -name '*.cs' -newermt "@$commit_epoch" -not -path '*/obj/*' -not -path '*/bin/*' | grep -c .)
expected_count=${#mutated[@]}
delta=$(comm -3 \
    <(find backend -name '*.cs' -newermt "@$commit_epoch" -not -path '*/obj/*' -not -path '*/bin/*' | sed 's|\\|/|g' | sort) \
    <(printf '%s\n' "${mutated[@]}" | sort) | grep -c .)
echo "mtime-newer-than-commit=$newer_count expected=$expected_count delta=$delta"
[ "$delta" -eq 0 ] || fails=$((fails + 1))

docs_entries=$(git status --porcelain -- docs | grep -c .)
echo "docs-status=$docs_entries entries (WIP do ODEV + artefatos da QA; informativo)"

echo "SUMMARY head=$head commit-files=$file_count outside-touches=$outside pkg-files=$pkg_files pkg-added=$pkg_added residue-backend=$residue untracked-backend=$untracked blobs=$blob_ok/${#mutated[@]} mtime-delta=$delta fails=$fails"
[ "$fails" -eq 0 ] || exit 1
exit 0
