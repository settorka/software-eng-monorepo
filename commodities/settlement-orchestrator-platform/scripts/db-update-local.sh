#!/usr/bin/env bash
set -euo pipefail

export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-.dotnet-home}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-$(pwd)/.dotnet-home/.nuget/packages}"
export ConnectionStrings__Oracle="${ConnectionStrings__Oracle:-User Id=settlement;Password=settlement;Data Source=localhost:1521/FREEPDB1}"

dotnet tool restore
dotnet tool run dotnet-ef database update \
  --project src/Settlement.Infrastructure/Settlement.Infrastructure.csproj \
  --startup-project src/Settlement.Infrastructure/Settlement.Infrastructure.csproj
