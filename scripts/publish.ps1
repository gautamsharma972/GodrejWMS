# Publishes the Godrej WMS application to .\publish\<target>.
#
#   .\scripts\publish.ps1 ui                 framework-dependent, portable (needs the .NET 10 ASP.NET Core runtime on the server)
#   .\scripts\publish.ps1 ui win-x64         self-contained for that runtime (no .NET install needed on the server)
#   .\scripts\publish.ps1 ui-linux           framework-dependent, fixed to Linux x64 (needs the .NET 10 ASP.NET Core runtime on the server)
#   .\scripts\publish.ps1 api                same, for GodrejWMS.PickersApi (the JWT REST API behind the Pickers mobile app)
#   .\scripts\publish.ps1 api-linux          same, fixed to Linux x64
#
# Targets: ui, ui-linux    the Blazor web application (hosts the business logic in-process)
#          api, api-linux  the separate JWT REST API behind the Pickers mobile app - same database,
#                           own process/port. Needs Jwt:Key set via environment variable or
#                           appsettings.Production.json on the server; it's deliberately not
#                           committed to this repo (see src/GodrejWMS.PickersApi/appsettings.json).
param(
    [string]$Target = "ui",
    [string]$Rid = ""
)
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

switch ($Target) {
    "ui"        { $project = "src/GodrejWMS.Web/GodrejWMS.Web.csproj";             $profile = "UI";      $out = "publish/ui" }
    "ui-linux"  { $project = "src/GodrejWMS.Web/GodrejWMS.Web.csproj";             $profile = "UILinux"; $out = "publish/ui-linux-x64" }
    "api"       { $project = "src/GodrejWMS.PickersApi/GodrejWMS.PickersApi.csproj"; $profile = "Api";      $out = "publish/pickers-api" }
    "api-linux" { $project = "src/GodrejWMS.PickersApi/GodrejWMS.PickersApi.csproj"; $profile = "ApiLinux"; $out = "publish/pickers-api-linux-x64" }
    default { throw "Unknown target '$Target'. Available targets: ui, ui-linux, api, api-linux" }
}

$args = @("publish", $project, "-c", "Release", "-p:PublishProfile=$profile")
if ($Rid) { $args += @("-r", $Rid, "--self-contained", "true") }

Write-Host "Publishing '$Target' to $out $(if ($Rid) { "(self-contained, $Rid)" })..."
& dotnet @args
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Write-Host ""
Write-Host "Done: $out"
switch ($Target) {
    "ui-linux"  { Write-Host "Run it with (on Linux):  cd $out; ASPNETCORE_URLS=http://0.0.0.0:5000 ./GodrejWMS.Web    (needs the .NET 10 ASP.NET Core runtime installed)" }
    "ui"        { Write-Host "Run it with:  cd $out; `$env:ASPNETCORE_URLS='http://0.0.0.0:5000'; dotnet GodrejWMS.Web.dll" }
    "api-linux" { Write-Host "Run it with (on Linux):  cd $out; ASPNETCORE_URLS=http://0.0.0.0:5001 Jwt__Key=<secret> ./GodrejWMS.PickersApi    (needs the .NET 10 ASP.NET Core runtime installed)" }
    "api"       { Write-Host "Run it with:  cd $out; `$env:ASPNETCORE_URLS='http://0.0.0.0:5001'; `$env:Jwt__Key='<secret>'; dotnet GodrejWMS.PickersApi.dll" }
}
