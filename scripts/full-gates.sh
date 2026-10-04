#!/usr/bin/env bash
set -euo pipefail

artifacts="${1:-artifacts}"
coverage="$(pwd)/$artifacts/coverage"

rm -rf "$coverage"
mkdir -p "$coverage"

for project in \
  LibTab/LibTab.csproj \
  LibTab.Tests/LibTab.Tests.csproj \
  LibTab.PropertyTests/LibTab.PropertyTests.csproj \
  LibTab.Specs/LibTab.Specs.csproj \
  LibTab.DifferentialTests/LibTab.DifferentialTests.csproj \
  LibTab.Fuzz/LibTab.Fuzz.csproj
do
  dotnet restore "$project" -v minimal
done

dotnet build LibTab/LibTab.csproj --no-restore /p:TreatWarningsAsErrors=true
dotnet build LibTab.Tests/LibTab.Tests.csproj --no-restore /p:TreatWarningsAsErrors=true
dotnet build LibTab.PropertyTests/LibTab.PropertyTests.csproj --no-restore /p:TreatWarningsAsErrors=true
dotnet build LibTab.Specs/LibTab.Specs.csproj --no-restore /p:TreatWarningsAsErrors=true
dotnet build LibTab.DifferentialTests/LibTab.DifferentialTests.csproj --no-restore /p:TreatWarningsAsErrors=true
dotnet build LibTab.Fuzz/LibTab.Fuzz.csproj --no-restore /p:TreatWarningsAsErrors=true

for project in \
  LibTab.Tests/LibTab.Tests.csproj \
  LibTab.PropertyTests/LibTab.PropertyTests.csproj \
  LibTab.Specs/LibTab.Specs.csproj \
  LibTab.DifferentialTests/LibTab.DifferentialTests.csproj
do
  name="$(basename "$(dirname "$project")")"
  dotnet test "$project" --no-build --no-restore \
    /p:CollectCoverage=true \
    /p:CoverletOutput="$coverage/$name/" \
    /p:CoverletOutputFormat=cobertura
done

python3 scripts/check_coverage.py "$coverage" 95 85
python3 scripts/check_crap.py "$coverage" 30

dotnet run --project LibTab.Fuzz/LibTab.Fuzz.csproj --no-build -- smoke fuzz/corpus
bash tools/run_scene_checks.sh
bash scripts/verify_tests_fail.sh
bash scripts/check_c_equivalence.sh

# Only reports written after this moment count. Picking the newest on disk meant an
# interrupted run - which still leaves a directory and a zero-scored report behind -
# became the input to the gate, and a killed Stryker was indistinguishable from a
# finished one.
stryker_started="$(date +%s)"
dotnet stryker --config-file stryker-config.json --break-at 70

mutation_report="$(
  find StrykerOutput -path '*/reports/mutation-report.json' -newermt "@$stryker_started" \
    -printf '%T@ %p\n' |
    sort -n |
    tail -n 1 |
    cut -d ' ' -f 2-
)"

if [[ -z "$mutation_report" ]]; then
  echo "stryker wrote no mutation report after $stryker_started" >&2
  exit 1
fi

python3 scripts/check_mutation.py "$mutation_report" 70
