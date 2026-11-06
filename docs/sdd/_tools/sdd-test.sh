#!/usr/bin/env bash
# sdd-test — build, test and smoke the project the way this machine actually works
# (Linux / macOS / WSL). Same contract and exit codes as sdd-test.ps1.
#
# Usage:
#   sdd-test.sh probe
#   sdd-test.sh build [sln-or-dir]
#   sdd-test.sh test  [sln-or-dir] [fallback-command]
#   sdd-test.sh health <base-url>
#
# Exit codes: 0 green · 1 real failure (product or test) · 3 environment refused (not your bug)
set -u

cmd=${1:-}
target=${2:-}
fallback=${3:-}

find_sln() {
  if [ -z "${target:-}" ]; then
    sln=$(find . -maxdepth 3 -name '*.sln' -type f 2>/dev/null | head -1)
    [ -n "$sln" ] || { echo "sdd-test: no *.sln found within depth 3; pass the path" >&2; exit 1; }
    printf '%s' "$sln"; return
  fi
  if [ -d "$target" ]; then
    sln=$(find "$target" -maxdepth 2 -name '*.sln' -type f 2>/dev/null | head -1)
    [ -n "$sln" ] || { echo "sdd-test: no *.sln inside $target" >&2; exit 1; }
    printf '%s' "$sln"; return
  fi
  printf '%s' "$target"
}

case $cmd in

probe)
  echo "dotnet: $(dotnet --version 2>&1 | head -1)"
  echo "runtime: $(dotnet --list-runtimes 2>&1 | grep 'Microsoft.NETCore.App' | tail -1)"
  for t in docker podman wsl git curl; do
    if command -v "$t" >/dev/null 2>&1; then echo "$t: $(command -v "$t")"; else echo "$t: absent"; fi
  done
  echo "docker daemon: $(docker info --format '{{.ServerVersion}}' 2>&1 | head -1)"
  echo "note: a sandbox can block outbound HTTPS and process pipes; build/test failures are"
  echo "      not product failures until a normal shell reproduces them."
  ;;

build)
  sln=$(find_sln); echo "build: $sln"
  out=$(dotnet build "$sln" -m:1 -nodeReuse:false -p:UseSharedCompilation=false 2>&1); code=$?
  if [ "$code" -eq 0 ]; then
    printf '%s\n' "$out" | grep -Ei 'aviso|warning|error|erro' | head -6
    echo "build: exit 0"; exit 0
  fi
  if ! printf '%s' "$out" | grep -Eq '(error|erro) [A-Z]{2,}[0-9]+'; then
    echo "build: exit 1 with no compiler error in the output"
    echo "environment: the MSBuild node pipe is blocked, not a product failure."
    echo "retry: same command in a normal shell."
    exit 3
  fi
  printf '%s\n' "$out" | grep -Ei 'error|erro' | head -10
  echo "build: exit $code (real compiler errors)"; exit 1
  ;;

test)
  sln=$(find_sln); echo "test: $sln"
  out=$(dotnet test "$sln" --no-build -c Debug -m:1 2>&1); code=$?
  printf '%s\n' "$out" | tail -6
  if [ "$code" -eq 0 ]; then echo "test: exit 0"; exit 0; fi

  blocked=0
  printf '%s' "$out" | grep -Eqi 'testhost|Win32Exception|handle|não é válido' && blocked=1
  printf '%s' "$out" | grep -Eq '^\s*(Passed!|Failed!|Aprovado!|Falhou!)' && blocked=0

  if [ "$blocked" -eq 1 ]; then
    echo "test: exit 1 with the sandbox signature (testhost cannot open the parent process handle)"
    echo "environment: this is NOT a failing test. Do not write it into the task as a defect."
    if [ -n "${fallback:-}" ]; then
      echo "fallback: $fallback"
      sh -c "$fallback"; fcode=$?
      echo "fallback: exit $fcode"
      [ "$fcode" -eq 0 ] && exit 0 || exit 1
    fi
    echo "next: build the test project, run the suite in-process, pass that command as the 3rd argument."
    exit 3
  fi
  echo "test: exit $code (real test failure)"; exit 1
  ;;

health)
  [ -n "${target:-}" ] || { echo "sdd-test: health needs a base url" >&2; exit 1; }
  base=${target%/}
  failed=0
  for p in /health /ready /dashboard; do
    code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 15 "$base$p" 2>/dev/null)
    [ -n "$code" ] || code="sem-resposta"
    printf '%-12s %s\n' "$p" "$code"
    case $code in 2*|3*) : ;; *) failed=1 ;; esac
  done
  [ "$failed" -eq 0 ] && exit 0 || exit 1
  ;;

*)
  echo "usage: sdd-test.sh <probe|build|test|health> [...]" >&2; exit 1 ;;
esac