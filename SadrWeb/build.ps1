# Builds the SPA, copies it into the API's wwwroot, then builds the API.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host '==> web (React + Vite)'
Push-Location "$here/web"
if (-not (Test-Path node_modules)) { npm ci }
npm run build
if ($LASTEXITCODE -ne 0) { throw 'npm run build failed' }
Pop-Location

Write-Host '==> copying SPA into api/wwwroot'
Remove-Item "$here/api/wwwroot" -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item "$here/web/dist" "$here/api/wwwroot" -Recurse

Write-Host '==> api (ASP.NET Core)'
Push-Location "$here/api"
dotnet build -c Release
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }
Pop-Location

Write-Host "`ndone. run:  cd $here/api; dotnet run -c Release --urls http://0.0.0.0:5199"
