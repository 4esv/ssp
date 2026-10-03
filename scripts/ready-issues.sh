#!/usr/bin/env bash
# List open issues that are ready to start.
# An issue is ready when its "Blocked by" field is "none" or lists only closed issues.
# An issue with no "Blocked by" field is not ready.
#
# Usage: scripts/ready-issues.sh [--self-test]
# Needs: gh (authenticated), jq.
#
# The field has one of two forms:
#   Blocked by: #3, #7          (one line)
#   ### Blocked by              (issue form heading; the value is the next non-empty line)
#
#   #3, #7
#
# NOTE: --self-test parses these sample bodies without network access:
#   "Goal: x\nBlocked by: none"            -> none
#   "Blocked by: #3, #7"                   -> 3 7
#   "**Blocked by:** 12"                   -> 12
#   "### Blocked by\n\n#4 #5\n\n### Size"  -> 4 5
#   "### Blocked by\n\nnone"               -> none
#   "Goal: no field"                       -> missing
set -euo pipefail

# Print "none", "missing", or the blocking issue numbers separated by spaces.
parse_blocked_by() {
  local body="$1" value=""
  value=$(printf '%s\n' "$body" | tr -d '\r' | awk '
    found_heading && NF { print; exit }
    /^#+[[:space:]]*Blocked by[[:space:]]*$/ { found_heading = 1; next }
    /Blocked by:/ { sub(/.*Blocked by:[*[:space:]]*/, ""); print; exit }
  ')
  if [[ -z "${value// /}" ]] && ! printf '%s\n' "$body" | grep -q 'Blocked by'; then
    echo missing
  elif printf '%s' "$value" | grep -qiE '^[[:space:]_*]*none'; then
    echo none
  else
    local numbers
    numbers=$(printf '%s' "$value" | grep -oE '[0-9]+' | tr '\n' ' ' | sed 's/ $//')
    echo "${numbers:-missing}"
  fi
}

self_test() {
  local failures=0
  check() {
    local got
    got=$(parse_blocked_by "$(printf '%b' "$1")")
    if [[ "$got" == "$2" ]]; then
      echo "ok   $2"
    else
      echo "FAIL expected '$2', got '$got'"
      failures=$((failures + 1))
    fi
  }
  check 'Goal: x\nBlocked by: none' 'none'
  check 'Blocked by: #3, #7' '3 7'
  check '**Blocked by:** 12' '12'
  check '### Blocked by\n\n#4 #5\n\n### Size' '4 5'
  check '### Blocked by\n\nnone' 'none'
  check 'Goal: no field' 'missing'
  return "$failures"
}

if [[ "${1:-}" == "--self-test" ]]; then
  self_test
  exit $?
fi

closed=" $(gh issue list --state closed --limit 1000 --json number --jq '.[].number' | tr '\n' ' ') "

gh issue list --state open --limit 1000 --json number,title,body \
  | jq -c '.[] | {number, title, body: (.body // "")}' \
  | while IFS= read -r issue; do
      number=$(jq -r '.number' <<<"$issue")
      title=$(jq -r '.title' <<<"$issue")
      blockers=$(parse_blocked_by "$(jq -r '.body' <<<"$issue")")
      [[ "$blockers" == "missing" ]] && continue
      ready=1
      if [[ "$blockers" != "none" ]]; then
        for n in $blockers; do
          [[ "$closed" == *" $n "* ]] || { ready=0; break; }
        done
      fi
      if [[ "$ready" == 1 ]]; then
        printf '#%s\t%s\n' "$number" "$title"
      fi
    done
