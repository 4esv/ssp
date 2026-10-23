#!/usr/bin/env bash
# Usage: scripts/check-release.sh <tag>
#        scripts/check-release.sh --self-test
# Proves a release (decision record 0008, stage 2). Exits non-zero and prints the failing check.
# 1. The tag is v plus the Version in Directory.Build.props.
# 2. dotnet pack writes ssp.<version>.nupkg.
# 3. The tool installed from that package prints "ssp <version>" as the first line of --version.
set -uo pipefail

# Prints the text of the first <Version> element on stdin.
parse_version() {
  sed -n 's|.*<Version>\([^<]*\)</Version>.*|\1|p' | head -n 1
}

if [ "${1:-}" = "--self-test" ]; then
  bad=0
  got="$(printf '<Project>\n  <PropertyGroup>\n    <Version>1.2.3-rc.1</Version>\n  </PropertyGroup>\n</Project>\n' | parse_version)"
  [ "$got" = "1.2.3-rc.1" ] || { echo "FAIL self-test: parsed '$got', expected 1.2.3-rc.1" >&2; bad=1; }
  got="$(printf '<Project/>\n' | parse_version)"
  [ -z "$got" ] || { echo "FAIL self-test: parsed '$got' from no Version" >&2; bad=1; }
  [ "$bad" -eq 0 ] && echo "OK self-test"
  exit "$bad"
fi

if [ $# -ne 1 ]; then
  echo "usage: $0 <tag> | --self-test" >&2
  exit 2
fi
tag="$1"
root="$(cd "$(dirname "$0")/.." && pwd)"

fail() {
  echo "FAIL $1" >&2
  exit 1
}

version="$(parse_version < "$root/Directory.Build.props")"
[ -n "$version" ] || fail "no <Version> in Directory.Build.props"
if [ "$tag" != "v$version" ]; then
  fail "tag $tag does not match Directory.Build.props: expected v$version"
fi

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
pack="$tmp/pack"
tool="$tmp/tool"

if ! dotnet pack "$root/src/Ssp.Cli" -c Release -o "$pack" >"$tmp/pack.log" 2>&1; then
  tail -n 20 "$tmp/pack.log" >&2
  fail "dotnet pack failed"
fi
[ -f "$pack/ssp.$version.nupkg" ] || fail "dotnet pack did not write ssp.$version.nupkg (found: $(ls "$pack" | tr '\n' ' '))"

if ! dotnet tool install --tool-path "$tool" ssp --version "$version" --add-source "$pack" >"$tmp/install.log" 2>&1; then
  tail -n 20 "$tmp/install.log" >&2
  fail "dotnet tool install failed"
fi
# The tool launcher needs DOTNET_ROOT when dotnet is a symlink (Homebrew). Derive it from the runtime list.
if [ -z "${DOTNET_ROOT:-}" ]; then
  rt="$(dotnet --list-runtimes | sed -n 's|^Microsoft\.NETCore\.App [^[]*\[\(.*\)/shared/Microsoft\.NETCore\.App\]$|\1|p' | tail -n 1)"
  [ -n "$rt" ] && export DOTNET_ROOT="$rt"
fi
first="$("$tool/ssp" --version 2>&1 | head -n 1)"
[ "$first" = "ssp $version" ] || fail "first line of 'ssp --version' is '$first', expected 'ssp $version'"

echo "OK $tag (ssp.$version.nupkg, ssp --version: $first)"
