#!/usr/bin/env bash
set -euo pipefail

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

mkdir -p "$tmp/repo"
cp -a LibTab LibTab.Tests LibTab.PropertyTests LibTab.Specs LibTab.DifferentialTests LibTab.slnx Directory.Build.props .editorconfig "$tmp/repo/"

python3 - "$tmp/repo/LibTab/NdbParser.cs" <<'PY'
import pathlib
import re
import sys

path = pathlib.Path(sys.argv[1])
text = path.read_text()
replacement = """public static IReadOnlyList<NdbEntry> ParseEntries(string text)
    {
        _ = text;
        return Array.Empty<NdbEntry>();
    }"""
text, count = re.subn(
    r"public static IReadOnlyList<NdbEntry> ParseEntries\(string text\)\n    \{.*?\n    \}",
    replacement,
    text,
    count=1,
    flags=re.S,
)
if count != 1:
    raise SystemExit("failed to stub NdbParser.ParseEntries")
path.write_text(text)
PY

pushd "$tmp/repo" >/dev/null
for project in \
  LibTab.Tests/LibTab.Tests.csproj \
  LibTab.PropertyTests/LibTab.PropertyTests.csproj \
  LibTab.Specs/LibTab.Specs.csproj \
  LibTab.DifferentialTests/LibTab.DifferentialTests.csproj
do
  dotnet restore "$project" -v minimal >/dev/null
done

if dotnet test LibTab.Tests/LibTab.Tests.csproj --no-restore >/tmp/libtabdotnet-honesty.log 2>&1; then
  cat /tmp/libtabdotnet-honesty.log
  echo "tests passed against an empty ndb parser stub" >&2
  exit 1
fi
popd >/dev/null

echo "test honesty check passed: tests fail against an empty ndb parser stub"
