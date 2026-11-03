#!/usr/bin/env bash
# Builds the SPA, copies it into the API's wwwroot, then builds the API.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"

echo "==> web (React + Vite)"
cd "$here/web"
[ -d node_modules ] || npm ci
npm run build

echo "==> copying SPA into api/wwwroot"
rm -rf "$here/api/wwwroot"
cp -r "$here/web/dist" "$here/api/wwwroot"

echo "==> api (ASP.NET Core)"
cd "$here/api"
dotnet build -c Release

echo
echo "done. run:  cd $here/api && dotnet run -c Release --urls http://0.0.0.0:5199"
