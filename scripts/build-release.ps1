param(
    [string]$Version = (Get-Content -LiteralPath "VERSION" -Raw).Trim(),
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$InnoSetupCompiler = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$publishDir = Join-Path $repoRoot "artifacts\publish\$Runtime"
$setupDir = Join-Path $repoRoot "artifacts\setup"
$manifestPath = Join-Path $setupDir "coredesk-update.json"

Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $setupDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $publishDir, $setupDir | Out-Null

dotnet publish (Join-Path $repoRoot "src\CoreDesk.App\CoreDesk.App.csproj") `
    -c $Configuration `
    -r $Runtime `
    -p:Platform=x64 `
    -p:Version=$Version `
    -p:SelfContained=true `
    -p:PublishSingleFile=false `
    -o $publishDir

if (-not (Test-Path -LiteralPath $InnoSetupCompiler)) {
    throw "Inno Setup compiler not found at '$InnoSetupCompiler'. Install Inno Setup 6 or pass -InnoSetupCompiler."
}

$env:COREDESK_VERSION = $Version
$env:COREDESK_PUBLISH_DIR = $publishDir
$env:COREDESK_SETUP_DIR = $setupDir
& $InnoSetupCompiler (Join-Path $repoRoot "installer\CoreDesk.iss")

$installer = Get-ChildItem -LiteralPath $setupDir -Filter "CoreDesk-Setup-$Version-*.exe" | Select-Object -First 1
if (-not $installer) {
    throw "Installer was not produced."
}

$sha256 = (Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256).Hash
$manifest = [ordered]@{
    version = $Version
    installerUri = "https://github.com/brianmayclone/coredesk/releases/download/v$Version/$($installer.Name)"
    installerSha256 = $sha256
    installerSizeBytes = $installer.Length
    releaseNotes = "CoreDesk $Version"
}

$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath
Write-Host "Built $($installer.FullName)"
Write-Host "Manifest $manifestPath"
