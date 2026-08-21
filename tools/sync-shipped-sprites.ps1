# Sync skill + class PNGs from the Il2CPP sprite dump into the shipped pack.
# Usage (from repo root):
#   powershell -File tools/sync-shipped-sprites.ps1
# Optional:
#   powershell -File tools/sync-shipped-sprites.ps1 -DumpPath "D:\path\to\sprite dump"

param(
    [string]$DumpPath = "X:\projects\SpiritVale Development - Il2CPP dump\development\sprite dump",
    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) {
    $OutDir = Join-Path $repoRoot "src\SpiritVale.Overlay.Host\Assets\Sprites"
}

if (-not (Test-Path -LiteralPath $DumpPath)) {
    throw "Sprite dump not found: $DumpPath"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$index = @{}
Get-ChildItem -LiteralPath $DumpPath -Filter *.png | ForEach-Object {
    $stem = $_.BaseName
    $cut = $stem.IndexOf("-sharedassets")
    if ($cut -gt 0) { $stem = $stem.Substring(0, $cut) }
    $k = $stem.ToLowerInvariant()
    if (-not $index.ContainsKey($k)) {
        $index[$k] = [pscustomobject]@{ Stem = $stem; Path = $_.FullName }
    }
}

$catalogPath = Join-Path $repoRoot "src\SpiritVale.Overlay.Domain\Resources\skill-catalog.json"
$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
$stems = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($s in $catalog.skills) {
    if ($s.spriteId) { [void]$stems.Add([string]$s.spriteId) }
}

foreach ($c in @(
        "class-warrior", "class-mage", "class-rogue", "class-knight", "class-summoner", "class-acolyte", "class-scout",
        "class-paladin-2", "class-berserker-2",
        "dragonknight", "revenant", "priest", "monk", "wizard", "chronomancer", "druid", "warlock",
        "assassin", "shinobi", "gunslinger", "ranger", "jester", "necromancer", "weaver"
    )) {
    [void]$stems.Add($c)
}

$copied = 0
$missing = New-Object System.Collections.Generic.List[string]
foreach ($stem in ($stems | Sort-Object)) {
    $k = $stem.ToLowerInvariant()
    if (-not $index.ContainsKey($k)) {
        $missing.Add($stem)
        continue
    }
    $src = $index[$k]
    $dest = Join-Path $OutDir ($src.Stem + ".png")
    Copy-Item -LiteralPath $src.Path -Destination $dest -Force
    $copied++
}

Write-Host "Copied $copied sprites -> $OutDir"
if ($missing.Count -gt 0) {
    Write-Host "Missing $($missing.Count):"
    $missing | ForEach-Object { Write-Host "  $_" }
    exit 1
}
