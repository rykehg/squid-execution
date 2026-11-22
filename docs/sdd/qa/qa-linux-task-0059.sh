#!/usr/bin/env bash
# QA-0059 Linux run (TASK-0059): build + the ImportInstanceCopy family (the DEV's AC-01..AC-04
# plus the two scenarios QA-0059 added) inside the local mcr.microsoft.com/dotnet/sdk:10.0 image
# - already on this machine, nothing installed, the same 10.0.403 pin ci.yml uses on
# ubuntu-latest. sdd-test.sh needs an explicit target (the repo ships *.slnx, GAP-17) and its
# test subcommand takes no filter, so the filtered run calls dotnet test directly after the
# build with the same flags the gate uses. Every log lands next to this script. Runs as:
#   podman run --rm -v <repo>:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-linux-task-0059.sh
set -u

cd /src || { echo "LINUX-EXIT=NAO-RODADO (cd /src failed)"; exit 1; }

echo "linux uname: $(uname -srm)"
echo "linux dotnet: $(dotnet --version 2>&1)"

bash docs/sdd/_tools/sdd-test.sh build backend/Squid.slnx 2>&1 | tee docs/sdd/qa/QA-0059-linux-build.log
b=${PIPESTATUS[0]}
echo "LINUX-BUILD-EXIT=$b"
if [ "$b" -ne 0 ]; then
    echo "LINUX-IMPORTCOPY-EXIT=NAO-RODADO (build did not pass)"
    exit "$b"
fi

dotnet test backend/Squid.slnx -c Debug -m:1 --no-build --filter 'FullyQualifiedName~ImportInstanceCopy' 2>&1 | tee docs/sdd/qa/QA-0059-linux-test-importcopy.log
t1=${PIPESTATUS[0]}
echo "LINUX-IMPORTCOPY-EXIT=$t1"

if [ "$t1" -ne 0 ]; then exit 1; fi

# The whole gate on Linux too (AC-05 has a Windows command, but the project ships ci.yml on
# ubuntu-latest: a platform this QA cannot run stays unverified, never assumed green).
bash docs/sdd/_tools/sdd-test.sh test backend/Squid.slnx 2>&1 | tee docs/sdd/qa/QA-0059-linux-test-gate.log
t2=${PIPESTATUS[0]}
echo "LINUX-GATE-EXIT=$t2"
exit "$t2"
