#Requires -Version 7
<#
.SYNOPSIS
    Renames Diva AI to a new brand name across the entire codebase.

.DESCRIPTION
    Performs a full namespace rename: C# projects, JWT strings, Docker service names,
    localStorage prefixes, and display strings. Runs in a new git branch. Idempotent.

.PARAMETER NewName
    PascalCase product name (e.g. AcmeCorp). No spaces.

.PARAMETER NewSlug
    Lowercase slug (e.g. acme). Used for localStorage prefix, Docker service names,
    API key prefix. No spaces.

.PARAMETER NewApiAudience
    JWT audience string (e.g. acme-api). Default: "$NewSlug-api".

.EXAMPLE
    ./tools/rebrand.ps1 -NewName AcmeCorp -NewSlug acme
    ./tools/rebrand.ps1 -NewName AcmeCorp -NewSlug acme -NewApiAudience acme-api
#>
param(
    [Parameter(Mandatory)][string]$NewName,
    [Parameter(Mandatory)][string]$NewSlug,
    [string]$NewApiAudience = "$NewSlug-api"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Validation ────────────────────────────────────────────────────────────────
if ($NewName -match '\s') { Write-Error "NewName must not contain spaces."; exit 1 }
if ($NewSlug -match '[^a-z0-9\-]') { Write-Error "NewSlug must be lowercase alphanumeric (hyphens allowed)."; exit 1 }
if ($NewName -ceq "Diva" -and $NewSlug -eq "diva") {
    Write-Warning "NewName and NewSlug are the same as the defaults — nothing to do."; exit 0
}

# ── Check for uncommitted changes ─────────────────────────────────────────────
$status = git status --porcelain
if ($status) {
    Write-Error "Uncommitted changes detected. Commit or stash them before rebranding."
    exit 1
}

# ── Create branch ─────────────────────────────────────────────────────────────
$branch = "rebrand/$NewSlug"
git checkout -b $branch
Write-Host "Working on branch: $branch" -ForegroundColor Cyan

# ── Replacement map ───────────────────────────────────────────────────────────
$replacements = @(
    @{ From = "Diva\.";          To = "$NewName." },          # C# namespaces
    @{ From = '"diva-local"';    To = """$NewSlug-local""" }, # JWT issuer
    @{ From = '"diva-api"';      To = """$NewApiAudience""" }, # JWT audience
    @{ From = '"Diva AI"';       To = """$NewName""" },       # display strings
    @{ From = 'Diva AI';         To = $NewName },             # unquoted display
    @{ From = 'diva_';           To = "${NewSlug}_" },        # localStorage prefix
    @{ From = 'diva-api';        To = $NewApiAudience },      # docker/yaml references
    @{ From = 'diva-data';       To = "$NewSlug-data" }       # docker volume/service
)

$extensions = @("*.cs","*.csproj","*.slnx","*.md","*.yml","*.yaml","*.txt","Dockerfile","*.json","*.ts","*.tsx","*.html")

# ── File content replacement ──────────────────────────────────────────────────
Write-Host "Replacing strings in files..." -ForegroundColor Cyan
$files = Get-ChildItem -Recurse -Include $extensions -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|node_modules|\.git)[\\/]' }

foreach ($file in $files) {
    $content = Get-Content -Raw -Path $file.FullName
    $original = $content
    foreach ($r in $replacements) {
        $content = $content -replace [regex]::Escape($r.From), $r.To
    }
    if ($content -ne $original) {
        Set-Content -Path $file.FullName -Value $content -NoNewline
        Write-Host "  Updated: $($file.FullName)" -ForegroundColor Gray
    }
}

# ── Directory and file renames ────────────────────────────────────────────────
Write-Host "Renaming directories and files..." -ForegroundColor Cyan

# Rename src/Diva.* → src/$NewName.*
Get-ChildItem -Path "src" -Directory | Where-Object { $_.Name -like "Diva.*" } | ForEach-Object {
    $newDirName = $_.Name -replace "^Diva\.", "$NewName."
    $newPath = Join-Path $_.Parent.FullName $newDirName
    if (-not (Test-Path $newPath)) {
        Rename-Item -Path $_.FullName -NewName $newDirName
        Write-Host "  Renamed dir: $($_.Name) → $newDirName" -ForegroundColor Gray
    }
}

# Rename tests/Diva.* → tests/$NewName.*
Get-ChildItem -Path "tests" -Directory | Where-Object { $_.Name -like "Diva.*" } | ForEach-Object {
    $newDirName = $_.Name -replace "^Diva\.", "$NewName."
    $newPath = Join-Path $_.Parent.FullName $newDirName
    if (-not (Test-Path $newPath)) {
        Rename-Item -Path $_.FullName -NewName $newDirName
        Write-Host "  Renamed dir: $($_.Name) → $newDirName" -ForegroundColor Gray
    }
}

# Rename solution file
$slnx = Get-ChildItem -Filter "Diva.slnx" -File | Select-Object -First 1
if ($slnx) {
    $newSlnx = "$NewName.slnx"
    Rename-Item -Path $slnx.FullName -NewName $newSlnx
    Write-Host "  Renamed: $($slnx.Name) → $newSlnx" -ForegroundColor Gray
}

# ── Update AppBranding defaults in appsettings.json ───────────────────────────
Write-Host "Updating AppBranding defaults..." -ForegroundColor Cyan
$appsettings = "src/$NewName.Host/appsettings.json"
if (Test-Path $appsettings) {
    $json = Get-Content -Raw $appsettings | ConvertFrom-Json
    if (-not $json.AppBranding) { $json | Add-Member -NotePropertyName AppBranding -NotePropertyValue ([PSCustomObject]@{}) }
    $json.AppBranding.ProductName = $NewName
    $json.AppBranding.Slug        = $NewSlug
    $json.AppBranding.ApiAudience = $NewApiAudience
    $json.AppBranding.LocalIssuer = "$NewSlug-local"
    $json | ConvertTo-Json -Depth 20 | Set-Content $appsettings
    Write-Host "  Updated AppBranding in $appsettings" -ForegroundColor Gray
}

# ── Commit ────────────────────────────────────────────────────────────────────
git add -A
git commit -m "chore: rebrand Diva -> $NewName"

Write-Host ""
Write-Host "Rebrand complete!" -ForegroundColor Green
Write-Host "Verify with:" -ForegroundColor Yellow
Write-Host "  dotnet build $NewName.slnx" -ForegroundColor White
Write-Host "  cd admin-portal && npm run build" -ForegroundColor White
Write-Host ""
Write-Host "Note: If AppBranding__Slug changes after go-live, all browser sessions" -ForegroundColor Yellow
Write-Host "      and platform API keys will be invalidated." -ForegroundColor Yellow
