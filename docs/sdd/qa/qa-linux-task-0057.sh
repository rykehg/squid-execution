#!/usr/bin/env bash
# QA-0057 Linux run (TASK-0057): build + the Backup family (AC-01/AC-02/AC-04/AC-05) and the
# drills Category=RestoreDrill (AC-03, the restore-offsite drill among them) inside the local
# mcr.microsoft.com/dotnet/sdk:10.0 image - already on this machine, nothing installed, the same
# 10.0.403 pin ci.yml uses on ubuntu-latest. sdd-test.sh needs an explicit target (the repo
# ships *.slnx, GAP-17) and its test subcommand takes no filter, so the filtered runs call
# dotnet test directly after the build with the same flags the gate uses. Every log lands next
# to this script. Runs as:
#   podman run --rm -v <repo>:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-linux-task-0057.sh
set -u

cd /src || { echo "LINUX-EXIT=NAO-RODADO (cd /src failed)"; exit 1; }

echo "linux uname: $(uname -srm)"
echo "linux dotnet: $(dotnet --version 2>&1)"

bash docs/sdd/_tools/sdd-test.sh build backend/Squid.slnx 2>&1 | tee docs/sdd/qa/QA-0057-linux-build.log
b=${PIPESTATUS[0]}
echo "LINUX-BUILD-EXIT=$b"
if [ "$b" -ne 0 ]; then
    echo "LINUX-BACKUP-EXIT=NAO-RODADO (build did not pass)"
    echo "LINUX-DRILLS-EXIT=NAO-RODADO (build did not pass)"
    exit "$b"
fi

dotnet test backend/Squid.slnx -c Debug -m:1 --no-build --filter 'FullyQualifiedName~Squid.Tests.Backup' 2>&1 | tee docs/sdd/qa/QA-0057-linux-test-backup.log
t1=${PIPESTATUS[0]}
echo "LINUX-BACKUP-EXIT=$t1"

dotnet test backend/Squid.slnx -c Debug -m:1 --no-build --filter 'Category=RestoreDrill' 2>&1 | tee docs/sdd/qa/QA-0057-linux-test-drills.log
t2=${PIPESTATUS[0]}
echo "LINUX-DRILLS-EXIT=$t2"

if [ "$t1" -ne 0 ] || [ "$t2" -ne 0 ]; then exit 1; fi
exit 0
