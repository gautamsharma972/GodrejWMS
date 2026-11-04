#!/usr/bin/env bash
# Publishes the Godrej WMS application to ./publish/<target>.
#
#   scripts/publish.sh ui                 framework-dependent, portable (needs the .NET 10 ASP.NET Core runtime on the server)
#   scripts/publish.sh ui linux-x64       self-contained for that runtime (no .NET install needed on the server)
#   scripts/publish.sh ui-linux           framework-dependent, fixed to Linux x64 (needs the .NET 10 ASP.NET Core runtime on the server;
#                                          gives a native ./GodrejWMS.Web apphost instead of the portable 'dotnet GodrejWMS.Web.dll' launch)
#   scripts/publish.sh api                same, for GodrejWMS.PickersApi (the JWT REST API behind the Pickers mobile app)
#   scripts/publish.sh api-linux          same, fixed to Linux x64
#
# Targets: ui, ui-linux    the Blazor web application (hosts the business logic in-process)
#          api, api-linux  the separate JWT REST API behind the Pickers mobile app - same database,
#                           own process/port. Needs Jwt:Key set via environment variable or
#                           appsettings.Production.json on the server; it's deliberately not
#                           committed to this repo (see src/GodrejWMS.PickersApi/appsettings.json).
set -euo pipefail

cd "$(dirname "$0")/.."

TARGET="${1:-ui}"
RID="${2:-}"

case "$TARGET" in
  ui)        PROJECT="src/GodrejWMS.Web/GodrejWMS.Web.csproj";       PROFILE="UI";      OUT="publish/ui" ;;
  ui-linux)  PROJECT="src/GodrejWMS.Web/GodrejWMS.Web.csproj";       PROFILE="UILinux"; OUT="publish/ui-linux-x64" ;;
  api)       PROJECT="src/GodrejWMS.PickersApi/GodrejWMS.PickersApi.csproj"; PROFILE="Api";      OUT="publish/pickers-api" ;;
  api-linux) PROJECT="src/GodrejWMS.PickersApi/GodrejWMS.PickersApi.csproj"; PROFILE="ApiLinux"; OUT="publish/pickers-api-linux-x64" ;;
  *)  echo "Unknown target '$TARGET'. Available targets: ui, ui-linux, api, api-linux" >&2; exit 1 ;;
esac

ARGS=(publish "$PROJECT" -c Release -p:PublishProfile="$PROFILE")
if [ -n "$RID" ]; then
  ARGS+=(-r "$RID" --self-contained true)
fi

echo "Publishing '$TARGET' to $OUT ${RID:+(self-contained, $RID)}..."
dotnet "${ARGS[@]}"

echo
echo "Done: $OUT"
case "$TARGET" in
  ui-linux)
    echo "Run it with:  cd $OUT && ASPNETCORE_URLS=http://0.0.0.0:5000 ./GodrejWMS.Web    (needs the .NET 10 ASP.NET Core runtime installed)"
    ;;
  ui)
    echo "Run it with:  cd $OUT && ASPNETCORE_URLS=http://0.0.0.0:5000 dotnet GodrejWMS.Web.dll"
    ;;
  api-linux)
    echo "Run it with:  cd $OUT && ASPNETCORE_URLS=http://0.0.0.0:5001 Jwt__Key=<secret> ./GodrejWMS.PickersApi    (needs the .NET 10 ASP.NET Core runtime installed)"
    ;;
  api)
    echo "Run it with:  cd $OUT && ASPNETCORE_URLS=http://0.0.0.0:5001 Jwt__Key=<secret> dotnet GodrejWMS.PickersApi.dll"
    ;;
esac
