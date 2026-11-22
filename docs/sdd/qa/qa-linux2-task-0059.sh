#!/usr/bin/env bash
# QA-0059 Linux classification run: sdd-test.sh only tees its own tail -6 (pre-existing S-12),
# so this run captures the FULL dotnet test output of the suite on Linux - which tests fail and
# why - to classify the 51 reds against the pre-existing baseline QA-0054 already classified
# (SkiaSharp libfontconfig missing in the container image, backlog I-10/S-44). The build comes
# first: the Windows gate rebuilds the same mount between container runs.
# Runs as: podman run --rm -v <repo>:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-linux2-task-0059.sh
set -u

cd /src || { echo "LINUX-FULL-EXIT=NAO-RODADO"; exit 1; }

ts=$(date +%Y%m%d-%H%M%S)
echo "linux classify run: $ts  uname: $(uname -srm)  dotnet: $(dotnet --version 2>&1)"

bash docs/sdd/_tools/sdd-test.sh build backend/Squid.slnx > "docs/sdd/qa/QA-0059-linux2-build.log" 2>&1
b=$?
echo "LINUX-BUILD-EXIT=$b"
if [ "$b" -ne 0 ]; then
    tail -30 "docs/sdd/qa/QA-0059-linux2-build.log"
    echo "LINUX-FULL-EXIT=NAO-RODADO (build did not pass)"
    exit "$b"
fi

dotnet test backend/Squid.slnx --no-build -c Debug -m:1 2>&1 |
    tee "docs/sdd/runs/test-runs/$ts-test-linux-full.log"
f=${PIPESTATUS[0]}
echo "LINUX-FULL-EXIT=$f"
echo "LINUX-FULL-LOG=docs/sdd/runs/test-runs/$ts-test-linux-full.log"

# The family under test must be green here regardless of the pre-existing 51.
dotnet test backend/Squid.slnx --no-build -c Debug -m:1 --filter 'FullyQualifiedName~ImportInstanceCopy' 2>&1 |
    tee docs/sdd/qa/QA-0059-linux2-test-importcopy.log
t=${PIPESTATUS[0]}
echo "LINUX-IMPORTCOPY-EXIT=$t"
exit "$t"
