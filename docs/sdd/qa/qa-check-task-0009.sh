#!/usr/bin/env bash
# qa-check-task-0009.sh - Linux twin of qa-check-task-0009.ps1 (static checks only).
# Same checks, same ids, same exit contract: 0 = all executed checks green, 1 = FAIL.
# The local gate (sdd-test.ps1 build+test) is Windows PowerShell 5.1 + dotnet: it is
# printed as UNVERIFIED here, never assumed green on this platform.
#
# Usage: bash docs/sdd/qa/qa-check-task-0009.sh [ci-path] [repo-root]
set -u

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
CI_PATH="${1:-}"
REPO_ROOT="${2:-}"
[ -z "$REPO_ROOT" ] && REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
[ -z "$CI_PATH" ] && CI_PATH="$REPO_ROOT/.github/workflows/ci.yml"

FAILS=0
TOTAL=0

check() { # check <id> <0|1> <detail>
  TOTAL=$((TOTAL + 1))
  if [ "$2" = "1" ]; then
    echo "PASS $1 - $3"
  else
    echo "FAIL $1 - $3"
    FAILS=$((FAILS + 1))
  fi
}
info() { echo "INFO $1 - $2"; }

echo "repo-root: $REPO_ROOT"
echo "ci-path:   $CI_PATH"
echo "platform:  $(uname -s) $(uname -m)"

# ---------------------------------------------------------------- AC-01/02/03 via PyYAML
PYFILE="$(mktemp)"
trap 'rm -f "$PYFILE"' EXIT
cat > "$PYFILE" <<'PYEOF'
import os, re, sys
import yaml

ci, root = sys.argv[1], sys.argv[2]
state = {"fail": 0, "total": 0}

def check(cid, ok, detail):
    state["total"] += 1
    print(("PASS " if ok else "FAIL ") + cid + " - " + detail)
    if not ok:
        state["fail"] += 1

raw = open(ci, "r", encoding="utf-8").read()
try:
    doc = yaml.safe_load(raw)
except Exception as e:
    print("FAIL AC01-parse - YAML invalid: " + " ".join(str(e).split())[:200])
    sys.exit(1)
check("AC01-parse", isinstance(doc, dict), "PyYAML %s parsed %s without error" % (yaml.__version__, os.path.basename(ci)))

# YAML 1.1: a bare `on` key is read as boolean True.
on = doc.get("on", doc.get(True)) if isinstance(doc, dict) else None
push = on.get("push") if isinstance(on, dict) else None
branches = (push or {}).get("branches") if isinstance(push, dict) else None
check("AC01-push", sorted(branches or []) == ["develop", "main"], "push branches = %s (need exactly develop+main)" % (branches,))
has_pr = isinstance(on, dict) and "pull_request" in on
check("AC01-pr", has_pr, "pull_request trigger present = %s" % has_pr)
perms = doc.get("permissions") if isinstance(doc, dict) else None
check("AC01-perms", perms == {"contents": "read"}, "permissions = %s (need contents: read only)" % (perms,))

jobs = doc.get("jobs") if isinstance(doc, dict) else None
jobs = jobs if isinstance(jobs, dict) else {}
steps = []
runs_on = []
for job in jobs.values():
    if isinstance(job, dict):
        runs_on.append(str(job.get("runs-on")))
        for s in (job.get("steps") or []):
            if isinstance(s, dict):
                steps.append(s)
runs = [s.get("run") for s in steps if isinstance(s.get("run"), str)]
expected = [
    "pwsh -NoProfile -File docs/sdd/_tools/sdd-test.ps1 build",
    "pwsh -NoProfile -File docs/sdd/_tools/sdd-test.ps1 test",
]
check("AC02-commands", runs == expected, "run steps = %s (need exactly the local gate build+test)" % (runs,))
coe = len(re.findall(r"continue-on-error", raw, re.I))
check("AC02-no-coe", coe == 0, "continue-on-error occurrences = %d" % coe)
pats = [
    ("secrets-ref", r"secrets\."),
    ("github-token", r"GITHUB_TOKEN"),
    ("gh-token", r"\bgh[pousr]_[A-Za-z0-9]{10,}"),
    ("aws-key", r"\bAKIA[0-9A-Z]{16}\b"),
    ("private-key", r"-----BEGIN [A-Z ]*PRIVATE KEY"),
    ("password", r"\b(password|passwd)\b"),
    ("api-key", r"\b(api[_-]?key|apikey)\b"),
    ("token", r"\btoken\b"),
    ("credential", r"credential"),
]
hits = []
for nm, pat in pats:
    for m in re.finditer(pat, raw, re.I):
        hits.append(nm + ":L%d" % (raw[:m.start()].count("\n") + 1))
check("AC02-no-secret", len(hits) == 0, "secret-pattern hits = %d %s" % (len(hits), hits[:6]))

sd = [s for s in steps if str(s.get("uses", "")).startswith("actions/setup-dotnet")]
ver = None
if sd:
    ver = (sd[0].get("with") or {}).get("dotnet-version")
pin = isinstance(ver, str) and re.fullmatch(r"\d+\.\d+\.\d+", ver) is not None and ver.split(".")[0] == "10"
check("AC03-pin", pin, "dotnet-version = %r (exact x.y.z pin, .NET 10)" % (ver,))

cache = [s for s in steps if str(s.get("uses", "")).startswith("actions/cache")]
ckey = None
if cache:
    ckey = (cache[0].get("with") or {}).get("key")
pkg = os.path.join(root, "backend", "Directory.Packages.props")
deriv = isinstance(ckey, str) and "hashFiles('**/Directory.Packages.props')" in ckey
check("AC03-cache", deriv and os.path.isfile(pkg), "cache key = %r ; Directory.Packages.props exists = %s" % (ckey, os.path.isfile(pkg)))

print("INFO runner - runs-on = %s (ubuntu-only is the humanGate decision, not an AC)" % (runs_on,))
print("SUMMARY py-total=%d py-fails=%d" % (state["total"], state["fail"]))
sys.exit(1 if state["fail"] else 0)
PYEOF

if ! command -v python3 >/dev/null 2>&1; then
  echo "FAIL AC01-parse - python3 absent on this platform, PyYAML validation cannot run"
  FAILS=$((FAILS + 1)); TOTAL=$((TOTAL + 1))
else
  PYOUT="$(python3 "$PYFILE" "$CI_PATH" "$REPO_ROOT" 2>&1)"
  PYRC=$?
  echo "$PYOUT"
  PYPASS=$(printf '%s\n' "$PYOUT" | grep -c '^PASS ' || true)
  PYFAIL=$(printf '%s\n' "$PYOUT" | grep -c '^FAIL ' || true)
  TOTAL=$((TOTAL + PYPASS + PYFAIL))
  FAILS=$((FAILS + PYFAIL))
  if [ "$PYRC" -ne 0 ] && [ "$PYFAIL" -eq 0 ]; then
    check AC01-parse 0 "python exited $PYRC without a PASS/FAIL line"
  fi
fi

# ---------------------------------------------------------------- AC-02 local-gate identity
GATE="$REPO_ROOT/docs/sdd/_tools/sdd-test.ps1"
if [ -f "$GATE" ] && grep -q "ValidateSet('probe', 'build', 'test'" "$GATE"; then
  check AC02-localgate 1 "local gate is the SAME script docs/sdd/_tools/sdd-test.ps1 with modes build and test"
else
  check AC02-localgate 0 "gate script missing or without build/test modes at $GATE"
fi
info AC02-shell "workflow shell is pwsh -NoProfile (GAP-17, ubuntu runner); local Windows gate uses powershell 5.1 - identical script and identical build/test args"

# ---------------------------------------------------------------- AC-05 delta of the change
GIT="git -C $REPO_ROOT -c safe.directory=$REPO_ROOT"
REF_EPOCH="$(date -d '2026-11-06 23:18:15 -0300' +%s 2>/dev/null || echo 0)"
ST="$($GIT status --porcelain 2>/dev/null)"
ST_RC=$?
if [ "$ST_RC" -ne 0 ]; then
  check AC05-delta 0 "git status failed (exit $ST_RC)"
else
  NEW_OUT=""
  FLOW=""
  while IFS= read -r line; do
    [ -z "$line" ] && continue
    path="${line#???}"
    path="${path//\"/}"
    full="$REPO_ROOT/$path"
    if [ -d "$full" ]; then
      targets="$(cd "$REPO_ROOT" && find "$path" -type f)"
    elif [ -f "$full" ]; then
      targets="$path"
    else
      targets=""
    fi
    for t in $targets; do
      tf="$REPO_ROOT/$t"
      [ -f "$tf" ] || continue
      m="$(stat -c %Y "$tf" 2>/dev/null || echo 0)"
      [ "$m" -ge "$REF_EPOCH" ] || continue   # pre-existing change from an earlier task
      case "$t" in
        .github/*|docs/sdd/plan/plan.md|docs/sdd/tasks/TASK-0009*|docs/sdd/specs/SPEC-0005*|docs/sdd/qa/QA-0008*|docs/sdd/qa/qa-check-task-0009*)
          FLOW="$FLOW $t ($(date -d @$m +%H:%M:%S))" ;;
        *)
          NEW_OUT="$NEW_OUT $t ($(date -d @$m +%H:%M:%S))" ;;
      esac
    done
  done <<EOF
$ST
EOF
  if [ -z "${NEW_OUT// }" ]; then
    check AC05-delta 1 "files changed after 2026-11-06 23:18:15 outside touches = 0"
  else
    check AC05-delta 0 "files changed after 2026-11-06 23:18:15 outside touches:$NEW_OUT"
  fi
  info AC05-flow "in-window writes by ODEV/QA flow (allowed, listed):$FLOW"
fi

# ---------------------------------------------------------------- AC-06 no push
HEAD="$($GIT rev-parse HEAD 2>/dev/null)"
ODEV="$($GIT rev-parse origin/develop 2>/dev/null)"
CNT="$($GIT rev-list --left-right --count HEAD...origin/develop 2>/dev/null | tr '\t' ' ')"
SB="$($GIT status -sb 2>/dev/null | head -1)"
AHEAD=0
case "$SB" in *"[ahead "*) AHEAD=1 ;; esac
NEWC="$($GIT log --format=%H --since='2026-11-06 23:18:15' 2>/dev/null | wc -l)"
HEADD="$($GIT log -1 --format=%ci 2>/dev/null)"
if [ -z "$HEAD" ]; then
  check AC06-sync 0 "git rev-parse failed"
else
  if [ "$HEAD" = "$ODEV" ] && [ "$CNT" = "0 0" ] && [ "$AHEAD" = "0" ]; then
    check AC06-sync 1 "HEAD=${HEAD%${HEAD#???????}} origin/develop=${ODEV%${ODEV#???????}} left-right=$CNT ahead=$AHEAD"
  else
    check AC06-sync 0 "HEAD=$HEAD origin/develop=$ODEV left-right=$CNT ahead=$AHEAD status=$SB"
  fi
  if [ "$NEWC" -eq 0 ]; then
    check AC06-nopush 1 "commits since task creation = 0; HEAD commit = $HEADD (predates the task = no push)"
  else
    check AC06-nopush 0 "commits since task creation = $NEWC; HEAD commit = $HEADD"
  fi
fi

# ---------------------------------------------------------------- AC-04: not executable here
if command -v pwsh >/dev/null 2>&1 && command -v dotnet >/dev/null 2>&1; then
  info AC04 "pwsh and dotnet present - but the gate is a Windows PowerShell script; run it on Windows (see evidence)"
else
  info AC04 "UNVERIFIED on Linux - local gate needs powershell 5.1 + dotnet; pwsh=$(command -v pwsh >/dev/null 2>&1 && echo present || echo absent) dotnet=$(command -v dotnet >/dev/null 2>&1 && echo present || echo absent) in this distro. Never assumed green."
fi

echo "SUMMARY total=$TOTAL fails=$FAILS"
if [ "$FAILS" -eq 0 ]; then
  echo "RESULT: PASS - all executed checks green"
  exit 0
fi
echo "RESULT: FAIL - $FAILS of $TOTAL checks failed"
exit 1
