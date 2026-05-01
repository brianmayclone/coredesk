param(
    [string]$Version = (Get-Content -LiteralPath "VERSION" -Raw).Trim(),
    [string[]]$Runtimes = @("win-x64", "win-arm64"),
    [string]$Configuration = "Release",
    [string]$InnoSetupCompiler = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$setupDir = Join-Path $repoRoot "artifacts\setup"
$manifestPath = Join-Path $setupDir "coredesk-update.json"

Remove-Item -LiteralPath $setupDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $setupDir | Out-Null

if (-not (Test-Path -LiteralPath $InnoSetupCompiler)) {
    throw "Inno Setup compiler not found at '$InnoSetupCompiler'. Install Inno Setup 6 or pass -InnoSetupCompiler."
}

function Get-ReleaseArchitecture {
    param([string]$Runtime)

    switch ($Runtime) {
        "win-x64" {
            return [pscustomobject]@{
                Runtime = $Runtime
                Platform = "x64"
                SetupArchitecture = "x64"
                InnoArchitecture = "x64"
            }
        }
        "win-arm64" {
            return [pscustomobject]@{
                Runtime = $Runtime
                Platform = "ARM64"
                SetupArchitecture = "arm64"
                InnoArchitecture = "arm64"
            }
        }
        default {
            throw "Unsupported release runtime '$Runtime'. Supported runtimes: win-x64, win-arm64."
        }
    }
}

$installers = @()
foreach ($runtime in $Runtimes) {
    $architecture = Get-ReleaseArchitecture -Runtime $runtime
    $publishDir = Join-Path $repoRoot "artifacts\publish\$runtime"

    Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

    dotnet publish (Join-Path $repoRoot "src\CoreDesk.App\CoreDesk.App.csproj") `
        -c $Configuration `
        -r $architecture.Runtime `
        -p:Platform=$($architecture.Platform) `
        -p:Version=$Version `
        -p:SelfContained=true `
        -p:PublishSingleFile=false `
        -o $publishDir

    $env:COREDESK_VERSION = $Version
    $env:COREDESK_PUBLISH_DIR = $publishDir
    $env:COREDESK_SETUP_DIR = $setupDir
    $env:COREDESK_SETUP_ARCH = $architecture.SetupArchitecture
    $expectedInstallerPath = Join-Path $setupDir "CoreDesk-Setup-$Version-$($architecture.SetupArchitecture).exe"
    $outputBaseFileName = [System.IO.Path]::GetFileNameWithoutExtension($expectedInstallerPath)
    & $InnoSetupCompiler `
        "/DAppVersion=$Version" `
        "/DPublishDir=$publishDir" `
        "/DOutputDir=$setupDir" `
        "/DAppArchitecture=$($architecture.InnoArchitecture)" `
        "/O$setupDir" `
        "/F$outputBaseFileName" `
        (Join-Path $repoRoot "installer\CoreDesk.iss")

    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup failed for $($architecture.Runtime) with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $expectedInstallerPath)) {
        $producedFiles = Get-ChildItem -LiteralPath $setupDir -Filter "*.exe" -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty Name
        throw "Installer for $($architecture.Runtime) was not produced at '$expectedInstallerPath'. Produced setup files: $($producedFiles -join ', ')"
    }

    $installer = Get-Item -LiteralPath $expectedInstallerPath
    $sha256 = (Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256).Hash
    $installers += [pscustomobject]@{
        architecture = $architecture.SetupArchitecture
        runtime = $architecture.Runtime
        installerUri = "https://github.com/brianmayclone/coredesk/releases/download/v$Version/$($installer.Name)"
        installerSha256 = $sha256
        installerSizeBytes = $installer.Length
    }

    Write-Host "Built $($installer.FullName)"
}

$primaryInstaller = $installers | Where-Object { $_.architecture -eq "x64" } | Select-Object -First 1
if (-not $primaryInstaller) {
    $primaryInstaller = $installers | Select-Object -First 1
}

$manifest = [ordered]@{
    version = $Version
    installerUri = $primaryInstaller.installerUri
    installerSha256 = $primaryInstaller.installerSha256
    installerSizeBytes = $primaryInstaller.installerSizeBytes
    installers = $installers
    releaseNotes = "CoreDesk $Version"
}

$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath
Write-Host "Manifest $manifestPath"
