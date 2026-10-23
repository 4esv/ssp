#!/usr/bin/env bash
# Run every `ssp ...` line in a fenced sh block of README.md and docs/cli.md with the built CLI.
# A line fails the check when its exit code is not 0. A line that ends with `# rc=N` must exit with N.
# A line that ends with `\` continues on the next line.
#
# Usage: scripts/check-doc-commands.sh
# Needs: dotnet build -c Release (the script reads src/Ssp.Cli/bin/Release/net10.0/ssp.dll).
set -u
root="$(cd "$(dirname "$0")/.." && pwd)"
dll="$root/src/Ssp.Cli/bin/Release/net10.0/ssp.dll"
files=(README.md docs/cli.md)

if [ ! -f "$dll" ]; then
  echo "FAIL: $dll does not exist. Run: dotnet build -c Release" >&2
  exit 1
fi

# NOTE: The commands run in a scratch folder, so output files do not land in the repo.
# The folder links to the folders that the examples read.
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
for d in circuits models src; do ln -s "$root/$d" "$work/$d"; done
cd "$work" || exit 1

failed=0
count=0
for f in "${files[@]}"; do
  if [ ! -f "$root/$f" ]; then
    echo "FAIL: $f does not exist"
    failed=$((failed + 1))
    continue
  fi
  cmds="$(awk '
    /^```sh[[:space:]]*$/ { inblock = 1; next }
    /^```/ { inblock = 0; pending = ""; next }
    inblock {
      line = $0
      if (pending != "") { line = pending " " line; pending = "" }
      if (line ~ /\\$/) { sub(/\\$/, "", line); pending = line; next }
      if (line ~ /^ssp /) print line
    }' "$root/$f")"
  while IFS= read -r line; do
    [ -z "$line" ] && continue
    want=0
    if [[ "$line" =~ \#[[:space:]]*rc=([0-9]+)[[:space:]]*$ ]]; then
      want="${BASH_REMATCH[1]}"
      line="${line%%#*}"
    fi
    cmd="dotnet \"$dll\"${line#ssp}"
    count=$((count + 1))
    out="$(eval "$cmd" 2>&1)"
    rc=$?
    if [ "$rc" -eq "$want" ]; then
      echo "ok   rc=$rc  $f: $line"
    else
      echo "FAIL rc=$rc (want $want)  $f: $line"
      echo "$out" | head -5 | sed 's/^/       /'
      failed=$((failed + 1))
    fi
  done <<< "$cmds"
done

echo "$count commands run, $failed failed"
[ "$failed" -eq 0 ] && [ "$count" -gt 0 ]
