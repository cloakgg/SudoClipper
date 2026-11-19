#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"

echo
echo "  S U D O C L I P P E R  installer"
echo

if ! command -v dotnet >/dev/null 2>&1; then
  echo "  .NET is required: https://dotnet.microsoft.com/download"
  exit 1
fi

if [ ! -f src/SudoClipper/wwwroot/index.html ]; then
  if ! command -v npm >/dev/null 2>&1; then
    echo "  The UI bundle is missing and npm was not found."
    echo "  Install Node.js, then: cd web && npm install && npm run build"
    exit 1
  fi
  echo "  Building the interface..."
  (cd web && npm install --no-audit --no-fund && npm run build)
fi

echo "  Packaging..."
dotnet pack src/SudoClipper/SudoClipper.csproj -c Release -o dist >/dev/null

echo "  Installing the sudoclipper command..."
dotnet tool uninstall -g SUDOCLIPPER >/dev/null 2>&1 || true
dotnet tool install -g SUDOCLIPPER --add-source "$(pwd)/dist" --version 1.0.0 >/dev/null

export PATH="$HOME/.dotnet/tools:$PATH"
echo "  Installed. Opening SUDOCLIPPER in your browser..."
echo
exec sudoclipper
