#!/usr/bin/env bash
# QA-0054 Linux classification run: sdd-test.sh only tees its own tail -6 (pre-existing S-12),
# so this run captures the FULL dotnet test output of the suite for failure classification
# (which tests fail on Linux and why), then runs the Backup family alone to prove this task's
# AC scenarios on Linux. Build outputs are already on the mount (previous container run).
# Runs as: podman run ... bash docs/sdd/qa/qa-linux2-task-0054.sh
set -u

cd /src || { echo "LINUX-FULL-EXIT=NAO-RODADO"; exit 1; }

ts=$(date +%Y%m%d-%H%M%S)
echo "linux classify run: $ts  uname: $(uname -srm)  dotnet: $(dotnet --version 2>&1)"

dotnet test backend/Squid.slnx --no-build -c Debug -m:1 2>&1 |
    tee "docs/sdd/runs/test-runs/$ts-test-linux-full.log"
f=${PIPESTATUS[0]}
echo "LINUX-FULL-EXIT=$f"
echo "LINUX-FULL-LOG=docs/sdd/runs/test-runs/$ts-test-linux-full.log"

dotnet test backend/Squid.slnx --no-build -c Debug -m:1 --filter FullyQualifiedName~Squid.Tests.Backup 2>&1 |
    tee docs/sdd/qa/QA-0054-linux-backup-filter.log
b=${PIPESTATUS[0]}
echo "LINUX-BACKUP-FILTER-EXIT=$b"
exit "$b"
