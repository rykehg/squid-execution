#!/usr/bin/env bash
# QA-0060 Linux run (TASK-0060): inside the local mcr.microsoft.com/dotnet/sdk:10.0 image -
# already on this machine, nothing installed, the same 10.0 pin ci.yml uses on ubuntu-latest
# (the CI path itself is `pwsh sdd-test.ps1 test` and pwsh is absent in the image, so the
# Linux twin available is sdd-test.sh - declared unverified, never assumed equal). Three
# phases: the project's own runner (sdd-test.sh test, which prints only its tail -6), then the
# SAME dotnet test command with the full output tee'd to runs/test-runs/ so the reds can be
# classified name-by-name against the pre-existing baseline, then the family under test -
# which must be green regardless of the pre-existing set. Every log lands next to this script.
# Runs as:
#   podman run --rm -v <repo>:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-linux-task-0060.sh
set -u

cd /src || { echo "LINUX-EXIT=NAO-RODADO (cd /src failed)"; exit 1; }

echo "linux uname: $(uname -srm)"
echo "linux dotnet: $(dotnet --version 2>&1)"

bash docs/sdd/_tools/sdd-test.sh build backend/Squid.slnx 2>&1 | tee docs/sdd/qa/QA-0060-linux-build.log
b=${PIPESTATUS[0]}
echo "LINUX-BUILD-EXIT=$b"
if [ "$b" -ne 0 ]; then
    echo "LINUX-FAMILY-EXIT=NAO-RODADO (build did not pass)"
    exit "$b"
fi

# Phase 2: the project's own Linux runner over the whole gate.
bash docs/sdd/_tools/sdd-test.sh test backend/Squid.slnx 2>&1 | tee docs/sdd/qa/QA-0060-linux-test-gate.log
g=${PIPESTATUS[0]}
echo "LINUX-GATE-EXIT=$g"

# Phase 3: the same suite with FULL output, for name-by-name classification of the reds
# against the baseline (sdd-test.sh prints only its tail -6, pre-existing S-12).
ts=$(date +%Y%m%d-%H%M%S)
dotnet test backend/Squid.slnx --no-build -c Debug -m:1 2>&1 |
    tee "docs/sdd/runs/test-runs/$ts-test-linux-full.log"
f=${PIPESTATUS[0]}
echo "LINUX-FULL-EXIT=$f"
echo "LINUX-FULL-LOG=docs/sdd/runs/test-runs/$ts-test-linux-full.log"

# Phase 4: the family under test must be green here regardless of the pre-existing set.
dotnet test backend/Squid.slnx --no-build -c Debug -m:1 --filter 'FullyQualifiedName~RedundancyScore' 2>&1 |
    tee docs/sdd/qa/QA-0060-linux-test-redundancy.log
t=${PIPESTATUS[0]}
echo "LINUX-FAMILY-EXIT=$t"
exit "$t"
