#!/usr/bin/env bash
# qa-check-task-0007.sh - Linux/WSL twin of qa-check-task-0007.ps1 (static checks only).
# The xunit suite itself is NOT executed here: this project's Linux side has no .NET SDK,
# so suite-on-Linux stays unverified and is never assumed equal to the Windows run.
# Exit: 0 = all checks green - 1 = at least one check failed
set -u
repo="$(cd "$(dirname "$0")/../../.." && pwd)"
failed=0
pass() { echo "PASS | $1 | $2"; }
fail() { echo "FAIL | $1 | $2"; failed=$((failed + 1)); }

model="$repo/specs/001-squid-mvp/data-model.md"
policy="$repo/backend/src/Squid.Domain/ContainerPolicy.cs"
ev="$repo/backend/src/Squid.Domain/IPolicyEvaluator.cs"
tests="$repo/backend/tests/Squid.Tests/Domain/PolicyEvaluatorTests.cs"

# ---- 1. GAP-12: enum names equal data-model section 2.3 (values parsed from the doc).
check_enum() { # <field> <type-name>
  local field="$1" type="$2" row vals expected actual
  row=$(grep -E "^\| \`$field\` \| enum \|" "$model" | head -n 1)
  if [ -z "$row" ]; then fail "GAP-12 data-model 2.3 row for $field" "row not found"; return; fi
  vals=$(printf '%s' "$row" | sed -n 's/.*enum | `\([^`]*\)`.*/\1/p')
  expected=$(printf '%s' "$vals" | sed 's/\\|/\n/g' | sed 's/^[[:space:]]*//;s/[[:space:]]*$//' |
    grep -v '^$' | paste -sd, -)
  actual=$(awk -v t="$type" '$0 ~ "public enum " t {f=1; next} f && /^\}/ {exit} f {print}' "$policy" |
    sed 's|//.*||' | tr ',' '\n' | tr -d ' \t\r' | grep -E '^[A-Za-z_][A-Za-z0-9_]*$' | paste -sd, -)
  if [ "$expected" = "$actual" ]; then
    pass "GAP-12 $type equals data-model 2.3" "expected=[$expected] actual=[$actual]"
  else
    fail "GAP-12 $type equals data-model 2.3" "expected=[$expected] actual=[$actual]"
  fi
}
check_enum Posting PostingPolicy
check_enum Visibility VisibilityPolicy
check_enum Join JoinPolicy

# ---- 2. GAP-11: Evaluate takes the role, never an Actor aggregate.
if grep -qE 'PolicyDecision Evaluate\(PolicyAction action, ContainerRole role, ContainerPolicy policy\)' "$ev"; then
  pass 'GAP-11 Evaluate(action, role, policy)' 'IPolicyEvaluator.cs declares Evaluate(PolicyAction, ContainerRole, ContainerPolicy)'
else
  fail 'GAP-11 Evaluate(action, role, policy)' 'signature not found'
fi
if grep -qE '\bActor[[:space:]]+[A-Za-z_]+[[:space:]]*[,)]' "$ev"; then
  fail 'GAP-11 no Actor parameter in Domain' 'an Actor-typed parameter exists'
else
  pass 'GAP-11 no Actor parameter in Domain' 'no "Actor <name>" parameter in IPolicyEvaluator.cs'
fi

# ---- 3. AC-04: 40 cells, unique, each equal to the hierarchy oracle.
cells=$(grep -oE 'InlineData\(ContainerRole\.[A-Za-z]+, PolicyAction\.[A-Za-z]+, (true|false)\)' "$tests")
count=$(printf '%s\n' "$cells" | grep -c 'InlineData' || true)
unique=$(printf '%s\n' "$cells" | sort -u | grep -c 'InlineData' || true)
[ "$count" -eq 40 ] && pass 'AC-04 exactly 40 matrix cells' "cells=$count (5 roles x 8 actions)" ||
  fail 'AC-04 exactly 40 matrix cells' "cells=$count"
[ "$unique" -eq 40 ] && pass 'AC-04 40 unique role/action cells' "unique=$unique" ||
  fail 'AC-04 40 unique role/action cells' "unique=$unique"

roles=$(printf '%s\n' "$cells" | sed -E 's/.*ContainerRole\.([A-Za-z]+).*/\1/' | sort -u | paste -sd, -)
[ "$roles" = 'Admin,Guest,Member,Moderator,Owner' ] && pass 'AC-04 the five roles' "$roles" ||
  fail 'AC-04 the five roles' "$roles"
actions=$(printf '%s\n' "$cells" | sed -E 's/.*PolicyAction\.([A-Za-z]+).*/\1/' | sort -u | paste -sd, -)
action_count=$(printf '%s' "$actions" | tr ',' '\n' | grep -c '[A-Za-z]' || true)
if printf '%s' "$actions" | grep -q 'Join' && printf '%s' "$actions" | grep -q 'Moderate' &&
  printf '%s' "$actions" | grep -q 'Admin' && [ "$action_count" -eq 8 ]; then
  pass 'AC-04 the eight actions' "$actions"
else
  fail 'AC-04 the eight actions' "$actions (count=$action_count)"
fi

# The matrix must run under the permissive policy (proved from the source, not assumed).
permissive=$(sed -n '/private static ContainerPolicy Permissive/,/version);/p' "$tests")
everyone=$(printf '%s' "$permissive" | grep -o 'PostingPolicy.Everyone' | wc -l | tr -d ' ')
if [ "$everyone" -ge 5 ] && printf '%s' "$permissive" | grep -q 'JoinPolicy.Open'; then
  pass 'AC-04 matrix runs under a permissive policy' "Everyone gates=$everyone, Join=Open present=true"
else
  fail 'AC-04 matrix runs under a permissive policy' "Everyone gates=$everyone"
fi

mismatch=0
while IFS= read -r line; do
  [ -n "$line" ] || continue
  role=$(printf '%s' "$line" | sed -E 's/.*ContainerRole\.([A-Za-z]+).*/\1/')
  action=$(printf '%s' "$line" | sed -E 's/.*PolicyAction\.([A-Za-z]+).*/\1/')
  exp=$(printf '%s' "$line" | sed -E 's/.* (true|false)\).*/\1/')
  case "$role" in Guest) r=0 ;; Member) r=1 ;; Moderator) r=2 ;; Admin) r=3 ;; Owner) r=4 ;; *) r=-1 ;; esac
  case "$action" in
    Post | Comment | React | Share | Attach)
      # permissive policy (Everyone gate => Member rank) AND data-model 2.4: Guest never writes.
      if [ "$r" -ge 1 ] && [ "$role" != 'Guest' ]; then want=true; else want=false; fi ;;
    Join) want=true ;;                              # FR-014: entering is not a write action
    Moderate) if [ "$r" -ge 2 ]; then want=true; else want=false; fi ;;  # data-model 3.2
    Admin) if [ "$r" -ge 3 ]; then want=true; else want=false; fi ;;     # data-model 3.2
    *) want=unknown ;;
  esac
  if [ "$want" != "$exp" ]; then
    mismatch=$((mismatch + 1))
    echo "       mismatch: $role/$action test=$exp oracle=$want"
  fi
done <<EOF
$cells
EOF
[ "$mismatch" -eq 0 ] && pass 'AC-04 every cell equals the hierarchy oracle' "mismatches=$mismatch" ||
  fail 'AC-04 every cell equals the hierarchy oracle' "mismatches=$mismatch"

# ---- 4. AC-06 static: Domain sources free of Orleans / JSON-LD / I/O (product files only:
# obj/ and bin/ hold SDK-generated files, a reference lives in what we write).
hits=$(grep -rnE 'Orleans|JsonLd|JSON-LD|Newtonsoft|System\.IO|HttpClient|FileStream|StreamReader|StreamWriter|Directory\.|File\.(Read|Write|Open|Create|Delete)|Path\.(Combine|GetFullPath)' \
  "$repo/backend/src/Squid.Domain" --include='*.cs' 2>/dev/null |
  grep -vE '/(obj|bin)/' | wc -l | tr -d ' ')
files=$(find "$repo/backend/src/Squid.Domain" -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' | wc -l | tr -d ' ')
[ "$hits" -eq 0 ] && pass 'AC-06 Domain sources free of Orleans/JSON-LD/I-O' "files=$files hits=$hits" ||
  fail 'AC-06 Domain sources free of Orleans/JSON-LD/I-O' "files=$files hits=$hits"
refhits=$(grep -cE '<PackageReference|<ProjectReference' "$repo/backend/src/Squid.Domain/Squid.Domain.csproj" || true)
[ "$refhits" -eq 0 ] && pass 'AC-06 Domain csproj declares no references' "hits=$refhits (XML elements, comments ignored)" ||
  fail 'AC-06 Domain csproj declares no references' "hits=$refhits"

# ---- 5. AC-01 / AC-02 static shape.
for prop in Posting Visibility Join Interaction PublisherIds Version; do
  if grep -qE "public[[:space:]]+[A-Za-z0-9_<>]+([[:space:]]*<[^>]*>)?[[:space:]]+$prop[[:space:]]*\{[[:space:]]*get;" "$policy"; then
    pass "AC-01 field $prop exists on ContainerPolicy" 'property with getter only'
  else
    fail "AC-01 field $prop exists on ContainerPolicy" 'not found'
  fi
done
grep -q 'static ContainerPolicy Group(' "$policy" && pass 'AC-01 Group preset exists' 'ContainerPolicy.Group()' ||
  fail 'AC-01 Group preset exists' 'not found'
grep -q 'static ContainerPolicy Community(' "$policy" && pass 'AC-01 Community preset exists' 'ContainerPolicy.Community()' ||
  fail 'AC-01 Community preset exists' 'not found'
if grep -q 'posting == PostingPolicy.SinglePublisher && publisherIds.Count == 0' "$policy" &&
  grep -q 'throw new ArgumentException("SinglePublisher' "$policy"; then
  pass 'AC-02 constructor rejects empty SinglePublisher' 'ContainerPolicy.cs ctor: SinglePublisher && Count==0 -> throw'
else
  fail 'AC-02 constructor rejects empty SinglePublisher' 'validation not found'
fi

# ---- 6. Gate logs: newest + DEV's claim both exit 0 with 97/97.
runfolder="$repo/docs/sdd/runs/test-runs"
newest=$(ls -1 "$runfolder"/*-test-gate.log 2>/dev/null | sort -r | head -n 1)
if [ -n "$newest" ]; then
  exitcode=$(sed -n 's/^exit:[[:space:]]*\(-\?[0-9]\+\).*/\1/p' "$newest" | head -n 1)
  total=$(grep -oE 'Aprovado:[[:space:]]+[0-9]+' "$newest" | head -n 1 | grep -oE '[0-9]+')
  if [ "$exitcode" = '0' ] && [ "$total" = '97' ]; then
    pass 'newest gate log exit 0 with 97/97' "$(basename "$newest") exit=$exitcode aprovado=$total"
  else
    fail 'newest gate log exit 0 with 97/97' "$(basename "$newest") exit=$exitcode aprovado=$total"
  fi
else
  fail 'a gate log exists' 'no *-test-gate.log found'
fi
devlog="$runfolder/20261106-174304-test-gate.log"
if [ -f "$devlog" ]; then
  exitcode=$(sed -n 's/^exit:[[:space:]]*\(-\?[0-9]\+\).*/\1/p' "$devlog" | head -n 1)
  total=$(grep -oE 'Aprovado:[[:space:]]+[0-9]+' "$devlog" | head -n 1 | grep -oE '[0-9]+')
  if [ "$exitcode" = '0' ] && [ "$total" = '97' ]; then
    pass 'DEV claimed gate log 20261106-174304 exit 0 with 97/97' "exit=$exitcode aprovado=$total"
  else
    fail 'DEV claimed gate log 20261106-174304 exit 0 with 97/97' "exit=$exitcode aprovado=$total"
  fi
else
  fail 'DEV claimed gate log exists' '20261106-174304-test-gate.log missing'
fi

if [ "$failed" -eq 0 ]; then
  echo 'RESULT: PASS - all checks green'
  exit 0
fi
echo "RESULT: FAIL - $failed check(s) failed"
exit 1
