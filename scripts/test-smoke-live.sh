#!/usr/bin/env bash
# Mutation test for smoke-live.sh. Usage: scripts/test-smoke-live.sh <published-wwwroot>
# Server 1 has no SPA fallback: the smoke check must fail on /editor.
# Server 2 has the fallback and the isolation headers: the smoke check must pass.
set -uo pipefail
root="${1:-publish/wwwroot}"
here="$(cd "$(dirname "$0")" && pwd)"
pids=()
trap 'kill "${pids[@]}" 2>/dev/null' EXIT

python3 -m http.server 8801 --directory "$root" >/dev/null 2>&1 & pids+=($!)

cat > /tmp/ssp-spa-server.py <<'PY'
import http.server, os, sys
root = sys.argv[1]
class H(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *a, **k): super().__init__(*a, directory=root, **k)
    def send_head(self):
        p = self.translate_path(self.path)
        if not os.path.exists(p):
            self.path = "/index.html"
        return super().send_head()
    def end_headers(self):
        self.send_header("Cross-Origin-Opener-Policy", "same-origin")
        self.send_header("Cross-Origin-Embedder-Policy", "require-corp")
        super().end_headers()
http.server.ThreadingHTTPServer(("127.0.0.1", 8802), H).serve_forever()
PY
python3 /tmp/ssp-spa-server.py "$root" >/dev/null 2>&1 & pids+=($!)
sleep 1

echo "== no fallback (expect failure on /editor)"
out="$("$here/smoke-live.sh" http://127.0.0.1:8801 2>&1)"; rc=$?
echo "$out"; echo "exit $rc"
if [ $rc -eq 0 ] || ! echo "$out" | grep -q 'FAIL http://127.0.0.1:8801/editor'; then
  echo "TEST FAILED: no-fallback server was not rejected on /editor"; exit 1
fi

echo "== with fallback (expect pass)"
out="$("$here/smoke-live.sh" http://127.0.0.1:8802 2>&1)"; rc=$?
echo "$out"; echo "exit $rc"
[ $rc -eq 0 ] || { echo "TEST FAILED: fallback server was rejected"; exit 1; }
echo "TESTS PASSED"
