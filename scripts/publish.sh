#!/usr/bin/env bash
# Publishes the Godrej WMS application to ./publish/<target>.
#
#   scripts/publish.sh ui                 framework-dependent, portable (needs the .NET 10 ASP.NET Core runtime on the server)
#   scripts/publish.sh ui linux-x64       self-contained for that runtime (no .NET install needed on the server)
#   scripts/publish.sh ui-linux           framework-dependent, fixed to Linux x64 (needs the .NET 10 ASP.NET Core runtime on the server;
#                                          gives a native ./GodrejWMS.Web apphost instead of the portable 'dotnet GodrejWMS.Web.dll' launch)
#
# Targets: ui, ui-linux  (the Blazor web application; it hosts the business logic in-process, and
#                          there is currently no separate API project to publish)
set -euo pipefail

cd "$(dirname "$0")/.."

TARGET="${1:-ui}"
RID="${2:-}"

case "$TARGET" in
  ui)       PROJECT="src/GodrejWMS.Web/GodrejWMS.Web.csproj"; PROFILE="UI";           OUT="publish/ui" ;;
  ui-linux) PROJECT="src/GodrejWMS.Web/GodrejWMS.Web.csproj"; PROFILE="UILinux"; OUT="publish/ui-linux-x64" ;;
  *)  echo "Unknown target '$TARGET'. Available targets: ui, ui-linux" >&2; exit 1 ;;
esac

ARGS=(publish "$PROJECT" -c Release -p:PublishProfile="$PROFILE")
if [ -n "$RID" ]; then
  ARGS+=(-r "$RID" --self-contained true)
fi

echo "Publishing '$TARGET' to $OUT ${RID:+(self-contained, $RID)}..."
dotnet "${ARGS[@]}"

echo
echo "Done: $OUT"
if [ "$TARGET" = "ui-linux" ]; then
  echo "Run it with:  cd $OUT && ASPNETCORE_URLS=http://0.0.0.0:5000 ./GodrejWMS.Web    (needs the .NET 10 ASP.NET Core runtime installed)"
else
  echo "Run it with:  cd $OUT && ASPNETCORE_URLS=http://0.0.0.0:5000 dotnet GodrejWMS.Web.dll"
fi
