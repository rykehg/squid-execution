#!/usr/bin/env bash
# QA-0056 Linux run (TASK-0056): build + the 7 drills Category=RestoreDrill (AC-01..AC-06 plus
# the section 6 status drill) inside the local mcr.microsoft.com/dotnet/sdk:10.0 image - already
# on this machine, nothing installed, the same 10.0.403 pin ci.yml uses on ubuntu-latest.
# sdd-test.sh needs an explicit target (the repo ships *.slnx, GAP-17) and its test subcommand
# takes no filter, so the filtered run calls dotnet test directly after the build with the same
# flags. Every log lands next to this script. Runs as:
#   podman run --rm -v <repo>:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-linux-task-0056.sh
set -u

cd /src || { echo "LINUX-EXIT=NAO-RODADO (cd /src failed)"; exit 1; }

echo "linux uname: $(uname -srm)"
echo "linux dotnet: $(dotnet --version 2>&1)"

bash docs/sdd/_tools/sdd-test.sh build backend/Squid.slnx 2>&1 | tee docs/sdd/qa/QA-0056-linux-build.log
b=${PIPESTATUS[0]}
echo "LINUX-BUILD-EXIT=$b"
if [ "$b" -ne 0 ]; then
    echo "LINUX-TEST-EXIT=NAO-RODADO (build did not pass)"
    exit "$b"
fi

dotnet test backend/Squid.slnx -c Debug -m:1 --no-build --filter 'Category=RestoreDrill' 2>&1 | tee docs/sdd/qa/QA-0056-linux-test.log
t=${PIPESTATUS[0]}
echo "LINUX-TEST-EXIT=$t"
exit "$t"
