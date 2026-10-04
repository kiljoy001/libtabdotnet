#!/usr/bin/env bash
set -euo pipefail

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

dotnet test LibTab.Tests/LibTab.Tests.csproj --no-build --no-restore
dotnet test LibTab.PropertyTests/LibTab.PropertyTests.csproj --no-build --no-restore
dotnet test LibTab.Specs/LibTab.Specs.csproj --no-build --no-restore
dotnet test LibTab.DifferentialTests/LibTab.DifferentialTests.csproj --no-build --no-restore

dotnet build LibTab.Fuzz/LibTab.Fuzz.csproj --no-restore
dotnet run --project LibTab.Fuzz/LibTab.Fuzz.csproj --no-build -- smoke fuzz/corpus
