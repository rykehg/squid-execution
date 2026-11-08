#!/usr/bin/env bash
# qa-check-task-0012.sh - Linux/WSL twin of qa-check-task-0012.ps1 (same check ids, same exit contract).
# 0 = all executed checks green, 1 = at least one FAIL.
#
# Covers (static content scans + git delta): AC-01 (CSPRNG source, masking, RC not derived from
# RK, no logging in the module, no consumer outside it), AC-02 (no catch/ex.ToString, AesGcmBox
# really keys the box), AC-03 (Argon2id + pinned cost), AC-04 (no clear RootKey member in
# KeyState), AC-05 (ADR-25 version placement + touches classification).
#
# NOT executed here, never assumed green on this platform:
#   - the runtime xunit scenarios and the gate (sdd-test.ps1): they need dotnet, and the only
#     WSL distro on this machine (podman-machine-default) has no dotnet SDK. Executed + recorded
#     on the Windows side; the Linux build/test path stays UNVERIFIED (no approval to install one).
#
# Usage: bash docs/sdd/qa/qa-check-task-0012.sh [repo-root]
set -u

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="${1:-}"
[ -z "$REPO_ROOT" ] && REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

FAILS=0
TOTAL=0

check() { # check <id> <0|1> <detail>
  TOTAL=$((TOTAL + 1))
  if [ "$2" = "1" ]; then echo "PASS $1 - $3"; else echo "FAIL $1 - $3"; FAILS=$((FAILS + 1)); fi
}
info() { echo "INFO $1 - $2"; }

# grep a pattern over every *.cs of a directory; echoes the hit count
hits() { # hits <dir> <extended-pattern>
  if [ ! -d "$1" ]; then echo 0; return; fi
  grep -rEn --include='*.cs' -- "$2" "$1" 2>/dev/null | wc -l | tr -d ' '
}

SRC="$REPO_ROOT/backend/src/Squid.Api/Crypto"
TST="$REPO_ROOT/backend/tests/Squid.Tests/Crypto"

echo "repo-root: $REPO_ROOT"
echo "mode:      STATIC content scans + git delta (runtime tests are separate commands)"
echo "platform:  $(uname -s) $(uname -r) $(uname -m)"

# ---------------------------------------------------------------- AC-01 source invariants
if grep -q 'RandomNumberGenerator\.Fill(bytes)' "$SRC/RootKey.cs" 2>/dev/null; then
  check AC01-rk-csprng-src 1 'RootKey.Create fills the 32 bytes with the platform CSPRNG (RandomNumberGenerator.Fill)'
else
  check AC01-rk-csprng-src 0 'RootKey.Create has no RandomNumberGenerator.Fill(bytes) call'
fi
if grep -q 'masked' "$SRC/RootKey.cs" 2>/dev/null && ! grep -E 'ToString\(\)[^=]*=>[^"]*"_bytes' "$SRC/RootKey.cs" >/dev/null 2>&1; then
  check AC01-rk-masking 1 'RootKey.ToString is masked (no raw bytes in the printed form)'
else
  check AC01-rk-masking 0 'RootKey.ToString does not mask the raw bytes'
fi
RC_LINE="$(grep -E 'RecoveryCode\s*=' "$SRC/KeyState.cs" 2>/dev/null | tr '\n' ' ' | tr -s ' ')"
RC_OK=0
case "$RC_LINE" in
  *RandomNumberGenerator.GetBytes*) ;;
  *) RC_OK=1 ;;
esac
case "$RC_LINE" in
  *KeyDerivation*|*HKDF*|*Derive*) RC_OK=1 ;;
esac
if [ "$RC_OK" -eq 0 ] && [ -n "$RC_LINE" ]; then
  check AC01-rc-csprng-src 1 "RecoveryCode comes from the CSPRNG, not from the RK: $(echo "$RC_LINE" | sed 's/^ *//;s/ *$//')"
else
  check AC01-rc-csprng-src 0 "RecoveryCode line not CSPRNG-only: $RC_LINE"
fi
LOGGED="$(hits "$SRC" 'ILogger|Console\.|Log\.|Trace\.|Debug\.Write|Serilog')"
if [ "$LOGGED" = "0" ]; then
  check AC01-no-logging 1 "logging APIs in backend/src/Squid.Api/Crypto = 0"
else
  check AC01-no-logging 0 "logging APIs in backend/src/Squid.Api/Crypto = $LOGGED"
fi
CONSUMERS="$(grep -rEl --include='*.cs' 'Squid\.Api\.Crypto' "$REPO_ROOT/backend/src" 2>/dev/null | grep -v '/Squid.Api/Crypto/' | wc -l | tr -d ' ')"
if [ "$CONSUMERS" = "0" ]; then
  check AC01-no-response-surface 1 "files under backend/src outside the module referencing Squid.Api.Crypto = 0 (no response path exists yet)"
else
  check AC01-no-response-surface 0 "consumers outside the module: $(grep -rEl --include='*.cs' 'Squid\.Api\.Crypto' "$REPO_ROOT/backend/src" 2>/dev/null | grep -v '/Squid.Api/Crypto/' | tr '\n' ' ')"
fi

# ---------------------------------------------------------------- AC-02 / AC-03 source shape
CATCH=$(( $(hits "$SRC" '\bcatch\b') + $(hits "$TST" '\bcatch\b') ))
if [ "$CATCH" = "0" ]; then check AC02-no-catch 1 'catch occurrences in Crypto src + tests = 0'
else check AC02-no-catch 0 "catch occurrences in Crypto src + tests = $CATCH"; fi
EXTO=$(( $(hits "$SRC" 'ex\.ToString|exception\.ToString') + $(hits "$TST" 'ex\.ToString|exception\.ToString') ))
if [ "$EXTO" = "0" ]; then check AC02-no-ex-tostring 1 'exception-to-string occurrences in Crypto src + tests = 0'
else check AC02-no-ex-tostring 0 "exception-to-string occurrences = $EXTO"; fi
AEAD="$(hits "$SRC" 'new AesGcm')"
if [ "$AEAD" = "2" ]; then check AC02-aead-box 1 "AesGcmBox really encrypts/authenticates: 'new AesGcm' hits = 2 (Seal + Open)"
else check AC02-aead-box 0 "'new AesGcm' hits = $AEAD (must be 2)"; fi

if grep -q 'Argon2Parameters\.Argon2id' "$SRC/RootKeyCipher.cs" 2>/dev/null; then
  check AC03-argon2id-src 1 'RootKeyCipher derives with Argon2id'
else
  check AC03-argon2id-src 0 'RootKeyCipher has no Argon2id marker'
fi
if grep -Eq 'Argon2Iterations\s*=\s*3' "$SRC/RootKeyCipher.cs" 2>/dev/null &&
   grep -Eq 'Argon2MemoryKib\s*=\s*4096' "$SRC/RootKeyCipher.cs" 2>/dev/null &&
   grep -Eq 'Argon2Parallelism\s*=\s*1' "$SRC/RootKeyCipher.cs" 2>/dev/null; then
  check AC03-argon2-cost-pinned 1 'Argon2id cost pinned in code (4096 KiB / 3 iterations / 1 lane)'
else
  check AC03-argon2-cost-pinned 0 'Argon2id cost constants missing or changed'
fi

# ---------------------------------------------------------------- AC-04 state shape
if grep -Eq 'public[[:space:]]+(required[[:space:]]+)?RootKey\b' "$SRC/KeyState.cs" 2>/dev/null; then
  check AC04-no-clear-rk-member 0 'KeyState declares a clear RootKey member'
else
  check AC04-no-clear-rk-member 1 'KeyState declares no clear RootKey member (encrypted/wrapped/public/RC members only)'
fi

# ---------------------------------------------------------------- AC-05 ADR-25 + touches
CSPROJ_COUNT="$(find "$REPO_ROOT/backend" -name '*.csproj' -type f 2>/dev/null | wc -l | tr -d ' ')"
VER_ATTR="$(find "$REPO_ROOT/backend" -name '*.csproj' -type f -exec grep -Eh 'Version[[:space:]]*=[[:space:]]*"' {} + 2>/dev/null | wc -l | tr -d ' ')"
if [ "$VER_ATTR" = "0" ]; then check AC05-no-version-attr 1 "Version=\" attributes in $CSPROJ_COUNT csproj = 0"
else check AC05-no-version-attr 0 "Version=\" attributes in csproj = $VER_ATTR"; fi
PKGVER="$(find "$REPO_ROOT/backend" -name '*.csproj' -type f -exec grep -h '<PackageVersion' {} + 2>/dev/null | wc -l | tr -d ' ')"
if [ "$PKGVER" = "0" ]; then check AC05-packageversion-only-props 1 'PackageVersion entries outside Directory.Packages.props = 0'
else check AC05-packageversion-only-props 0 "PackageVersion entries outside props = $PKGVER"; fi

BC_VER="$(grep -Ec '<PackageVersion Include="BouncyCastle\.Cryptography" Version="[^"]+"[[:space:]]*/>' "$REPO_ROOT/backend/Directory.Packages.props" 2>/dev/null || true)"
BC_REF="$(find "$REPO_ROOT/backend" -name '*.csproj' -type f -exec grep -Hh '<PackageReference Include="BouncyCastle\.Cryptography"' {} + 2>/dev/null | wc -l | tr -d ' ')"
BC_REF_VER="$(find "$REPO_ROOT/backend" -name '*.csproj' -type f -exec grep -h '<PackageReference Include="BouncyCastle\.Cryptography"' {} + 2>/dev/null | grep -Ec 'Version[[:space:]]*=' || true)"
BC_REF_FILE="$(find "$REPO_ROOT/backend" -name '*.csproj' -type f -exec grep -l '<PackageReference Include="BouncyCastle\.Cryptography"' {} + 2>/dev/null | xargs -r -n1 basename | tr '\n' ',')"
if [ "$BC_VER" = "1" ]; then check AC05-bouncycastle-single-version 1 "BouncyCastle PackageVersion lines in Directory.Packages.props = 1"
else check AC05-bouncycastle-single-version 0 "BouncyCastle PackageVersion lines in props = $BC_VER"; fi
if [ "$BC_REF" = "1" ] && [ "$BC_REF_VER" = "0" ] && [ "$BC_REF_FILE" = "Squid.Api.csproj," ]; then
  check AC05-bouncycastle-ref-no-version 1 "BouncyCastle PackageReference = 1 in [Squid.Api.csproj] with Version= on line = 0"
else
  check AC05-bouncycastle-ref-no-version 0 "refs=$BC_REF versioned=$BC_REF_VER files=[$BC_REF_FILE]"
fi

GIT="git -C $REPO_ROOT -c safe.directory=$REPO_ROOT"
ST="$($GIT status --porcelain 2>/dev/null)"
ST_RC=$?
if [ "$ST_RC" -ne 0 ]; then
  check AC05-touches 0 "git status failed (exit $ST_RC)"
else
  OUTSIDE=""; MINE=""; PEERF=""; FLOW=0
  while IFS= read -r line; do
    [ -z "$line" ] && continue
    path="${line#???}"; path="${path//\"/}"
    case "$path" in
      backend/Directory.Packages.props|backend/src/Squid.Api/Squid.Api.csproj|backend/src/Squid.Api/Crypto|backend/src/Squid.Api/Crypto/*|backend/tests/Squid.Tests/Crypto|backend/tests/Squid.Tests/Crypto/*)
        MINE="$MINE $path" ;;
      backend/src/Squid.Grains/Actors|backend/src/Squid.Grains/Actors/*|backend/tests/Squid.Tests/Actors|backend/tests/Squid.Tests/Actors/*)
        PEERF="$PEERF $path" ;;
      backend/*)
        OUTSIDE="$OUTSIDE $path" ;;
      docs/sdd/*)
        FLOW=$((FLOW + 1)) ;;
      *)
        OUTSIDE="$OUTSIDE $path" ;;
    esac
  done <<EOF
$ST
EOF
  if [ -z "${OUTSIDE// }" ]; then
    check AC05-touches 1 "backend/product entries outside the 4 touches (peer TASK-0013 excluded) = 0"
  else
    check AC05-touches 0 "outside:$OUTSIDE"
  fi
  info AC05-touches-mine "TASK-0012 product delta:$(echo $MINE)"
  info AC05-touches-peer "disjoint TASK-0013 paths (not QA-0012's, never modified here):$(echo $PEERF)"
  info AC05-touches-flow "flow artifacts under docs/sdd (not product): $FLOW entries"
fi

# ---------------------------------------------------------------- what this script never claims
info AC05-runtime 'executed as separate recorded commands, never claimed here: sdd-test.ps1 test (gate), -TestFilter FullyQualifiedName~Squid.Tests.Crypto, -TestFilter FullyQualifiedName~DomainHasNoInfrastructureTests'
info platform-limit 'the Linux gate is UNVERIFIED: the only WSL distro (podman-machine-default) has no dotnet; installing one needs a gate approval that was not granted.'

echo "SUMMARY total=$TOTAL fails=$FAILS"
if [ "$FAILS" -eq 0 ]; then
  echo "RESULT: PASS - all executed checks green"
  exit 0
fi
echo "RESULT: FAIL - $FAILS of $TOTAL checks failed"
exit 1
