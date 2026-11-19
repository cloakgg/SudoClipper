$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

Write-Host ""
Write-Host "  S U D O C L I P P E R  installer" -ForegroundColor Red
Write-Host ""

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "  .NET is required. Install it from https://dotnet.microsoft.com/download then run this again." -ForegroundColor Yellow
    exit 1
}

# Build the interface if it has not been built yet.
if (-not (Test-Path "src/SudoClipper/wwwroot/index.html")) {
    $node = Get-Command node -ErrorAction SilentlyContinue
    if (-not $node) {
        Write-Host "  The UI bundle is missing and Node.js was not found." -ForegroundColor Yellow
        Write-Host "  Install Node.js, then: cd web; npm install; npm run build" -ForegroundColor Yellow
        exit 1
    }
    Write-Host "  Building the interface..." -ForegroundColor DarkGray
    Push-Location web
    npm install --no-audit --no-fund
    npm run build
    Pop-Location
}

Write-Host "  Packaging..." -ForegroundColor DarkGray
dotnet pack src/SudoClipper/SudoClipper.csproj -c Release -o dist | Out-Null

Write-Host "  Installing the sudoclipper command..." -ForegroundColor DarkGray
dotnet tool uninstall -g SUDOCLIPPER 2>$null | Out-Null
dotnet tool install -g SUDOCLIPPER --add-source "$root/dist" --version 1.0.0 | Out-Null

if (-not (Get-Command sudoclipper -ErrorAction SilentlyContinue)) {
    $tools = Join-Path $env:USERPROFILE ".dotnet\tools"
    if (Test-Path $tools) { $env:PATH = "$tools;$env:PATH" }
}

Write-Host "  Installed. Opening SUDOCLIPPER in your browser..." -ForegroundColor Green
Write-Host ""
sudoclipper
