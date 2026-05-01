param(
    [string]$VersionFile = "VERSION"
)

$path = Resolve-Path -LiteralPath $VersionFile
$raw = (Get-Content -LiteralPath $path -Raw).Trim()
if ($raw -notmatch '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)$') {
    throw "VERSION must use X.Y.Z format. Current value: '$raw'"
}

$next = "{0}.{1}.{2}" -f $Matches.major, $Matches.minor, ([int]$Matches.patch + 1)
Set-Content -LiteralPath $path -Value $next -NoNewline
$next
