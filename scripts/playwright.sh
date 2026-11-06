#!/usr/bin/env bash
# Publish src/Ssp.Web, serve it, and run the Playwright tests against it.
#
# Usage: scripts/playwright.sh
# Needs: dotnet, python3. The first run downloads Chromium.
# Set SSP_PLAYWRIGHT_WITH_DEPS=1 to also install system packages (Linux, needs sudo).
set -euo pipefail

cd "$(dirname "$0")/.."
port="${SSP_PORT:-5099}"
out="$(mktemp -d)"

dotnet publish src/Ssp.Web -c Release -o "$out"

# NOTE: The fonts belong to aesv.io and are not in this repository. The tests measure the real look, so a
# build without them takes the faces the site serves, into this throwaway folder only.
fonts="$out/wwwroot/substrate/fonts"
mkdir -p "$fonts"
for f in handjet-cells handjet-cells-mono; do
  for x in woff2 woff ttf; do
    [ -f "$fonts/$f.$x" ] || curl -fsS -o "$fonts/$f.$x" "https://aesv.io/fonts/$f.$x"
  done
done

python3 -m http.server "$port" --bind 127.0.0.1 --directory "$out/wwwroot" >/dev/null 2>&1 &
server=$!
trap 'kill "$server" 2>/dev/null || true; rm -rf "$out"' EXIT

for _ in $(seq 1 50); do
  curl -fs "http://127.0.0.1:$port/" >/dev/null && break
  sleep 0.2
done

SSP_BASE_URL="http://127.0.0.1:$port/" \
  dotnet test tests/Ssp.Web.Tests -c Release --filter "FullyQualifiedName~Ssp.Web.Tests.Playwright" --logger "console;verbosity=detailed"
