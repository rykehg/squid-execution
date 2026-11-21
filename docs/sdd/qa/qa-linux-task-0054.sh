#!/usr/bin/env bash
# QA-0054 Linux run (TASK-0054): the project's own sdd-test.sh twin executed INSIDE the local
# mcr.microsoft.com/dotnet/sdk:10.0 image (already on this machine - no image installed - and
# the exact 10.0.403 pin ci.yml uses on ubuntu-latest). The twin only finds *.sln while the
# repo ships *.slnx (pre-existing GAP-17, ci.yml:3), so the solution path is passed explicitly
# - the documented way to use it. Builds first, then runs the FULL suite; every log lands next
# to this script. This file runs as: podman run ... bash docs/sdd/qa/qa-linux-task-0054.sh
set -u

cd /src || { echo "LINUX-BUILD-EXIT=NAO-RODADO (cd /src failed)"; exit 1; }

echo "linux uname: $(uname -srm)"
echo "linux dotnet: $(dotnet --version 2>&1)"

bash docs/sdd/_tools/sdd-test.sh build backend/Squid.slnx 2>&1 | tee docs/sdd/qa/QA-0054-linux-build.log
b=${PIPESTATUS[0]}
echo "LINUX-BUILD-EXIT=$b"
if [ "$b" -ne 0 ]; then
    echo "LINUX-TEST-EXIT=NAO-RODADO (build did not pass)"
    exit "$b"
fi

bash docs/sdd/_tools/sdd-test.sh test backend/Squid.slnx 2>&1 | tee docs/sdd/qa/QA-0054-linux-test.log
t=${PIPESTATUS[0]}
echo "LINUX-TEST-EXIT=$t"
exit "$t"
