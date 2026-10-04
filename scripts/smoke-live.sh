#!/usr/bin/env bash
# Usage: scripts/smoke-live.sh <base-url>
# Checks a deployed ssp site with curl. Exits non-zero and prints the failing URL.
set -uo pipefail

if [ $# -ne 1 ]; then
  echo "usage: $0 <base-url>" >&2
  exit 2
fi
base="${1%/}"
fail=0

bad() {
  echo "FAIL $1: $2" >&2
  fail=1
}

status() { curl -s -o /dev/null -w '%{http_code}' --max-time 20 "$1"; }

# Home page: 200 and the title.
page="$(mktemp)"
trap 'rm -f "$page"' EXIT
code="$(curl -s -o "$page" -w '%{http_code}' --max-time 20 "$base/")"
if [ "$code" != "200" ]; then
  bad "$base/" "status $code, expected 200"
elif ! grep -q '<title>ssp</title>' "$page"; then
  bad "$base/" "no <title>ssp</title>"
fi

# SPA fallback routes.
for path in /editor /gallery; do
  code="$(status "$base$path")"
  [ "$code" = "200" ] || bad "$base$path" "status $code, expected 200"
done

# First fingerprinted runtime script named in the page.
js="$(grep -oE '_framework/dotnet\.[A-Za-z0-9]{10}\.js' "$page" | head -n 1)"
if [ -z "$js" ]; then
  bad "$base/" "no fingerprinted _framework/dotnet.*.js in the page"
else
  code="$(status "$base/$js")"
  [ "$code" = "200" ] || bad "$base/$js" "status $code, expected 200"
fi

# Cross-origin isolation headers.
headers="$(curl -s -o /dev/null -D - --max-time 20 "$base/" | tr -d '\r')"
for h in Cross-Origin-Opener-Policy Cross-Origin-Embedder-Policy; do
  echo "$headers" | grep -qi "^$h:" || bad "$base/" "missing header $h"
done

if [ "$fail" -eq 0 ]; then
  echo "OK $base"
fi
exit "$fail"
