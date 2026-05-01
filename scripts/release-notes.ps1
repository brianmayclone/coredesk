param(
    [string]$Version,
    [string]$OutputPath = "artifacts\release-notes.md"
)

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
$previousTag = git describe --tags --abbrev=0 2>$null
if ($LASTEXITCODE -eq 0 -and $previousTag) {
    $changes = @(git log "$previousTag..HEAD" --pretty=format:"- %s")
} else {
    $changes = @(git log --pretty=format:"- %s" -n 50)
}

if (-not $changes) {
    $changes = @("- Maintenance build")
}

$notes = @(
    "# CoreDesk $Version",
    ""
) + $changes + @(
    "",
    "Installers:",
    "- CoreDesk-Setup-$Version-x64.exe",
    "- CoreDesk-Setup-$Version-arm64.exe",
    "Update manifest: coredesk-update.json"
)

Set-Content -LiteralPath $OutputPath -Value ($notes -join [Environment]::NewLine)
Get-Item -LiteralPath $OutputPath
