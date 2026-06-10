param(
    [string]$Configuration = "Release",
    [string[]]$RuntimeIdentifier = @("win-x64"),
    [string]$Version = "0.0.0-local",
    [string]$OutputDirectory = "",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$desktopProject = Join-Path $repoRoot "src\LibreArm.Desktop\LibreArm.Desktop.csproj"
$desktopManifest = Join-Path $repoRoot "src\LibreArm.Desktop\Package.appxmanifest"
$solution = Join-Path $repoRoot "LibreArm.slnx"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\release"
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$stagingRoot = Join-Path $OutputDirectory "staging"

$platformByRuntime = @{
    "win-x64" = "x64"
    "win-x86" = "x86"
    "win-arm64" = "ARM64"
}

function ConvertTo-AppxPackageVersion {
    param([string]$ReleaseVersion)

    $normalizedVersion = $ReleaseVersion.TrimStart("v")
    if ($normalizedVersion -notmatch '^(?<core>\d+(\.\d+){0,3})(?<suffix>-.+)?$') {
        throw "Version '$ReleaseVersion' cannot be used as an MSIX package version. Use a numeric version with an optional prerelease suffix, such as 1.0.0 or 0.1.0-beta.1."
    }

    $suffix = $Matches["suffix"]
    $parts = [System.Collections.Generic.List[string]]::new()
    $Matches["core"].Split(".") | ForEach-Object { $parts.Add($_) }

    if (-not [string]::IsNullOrWhiteSpace($suffix) -and $parts.Count -lt 4) {
        $prereleaseNumber = 0
        if ($suffix -match '\.(?<number>\d+)$') {
            $prereleaseNumber = [int]$Matches["number"]
        }

        while ($parts.Count -lt 3) {
            $parts.Add("0")
        }

        $parts.Add($prereleaseNumber.ToString())
    }

    while ($parts.Count -lt 4) {
        $parts.Add("0")
    }

    return $parts -join "."
}

foreach ($runtime in $RuntimeIdentifier) {
    if (-not $platformByRuntime.ContainsKey($runtime)) {
        throw "Unsupported runtime '$runtime'. Supported values: $($platformByRuntime.Keys -join ', ')"
    }
}

$packageVersion = ConvertTo-AppxPackageVersion $Version

if (-not $SkipTests) {
    dotnet test $solution --configuration $Configuration
}

if (Test-Path $OutputDirectory) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $OutputDirectory | Out-Null

$originalManifestBytes = [System.IO.File]::ReadAllBytes($desktopManifest)
$originalManifest = [System.Text.Encoding]::UTF8.GetString($originalManifestBytes)
$manifestXml = $originalManifest.TrimStart([char]0xFEFF)

try {
    $manifest = [xml]$manifestXml
    $namespaceManager = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
    $namespaceManager.AddNamespace("pkg", $manifest.DocumentElement.NamespaceURI)
    $identity = $manifest.SelectSingleNode("/pkg:Package/pkg:Identity", $namespaceManager)

    if ($null -eq $identity) {
        throw "Could not find the MSIX package identity in $desktopManifest."
    }

    $identity.SetAttribute("Version", $packageVersion)
    $manifest.Save($desktopManifest)

    foreach ($runtime in $RuntimeIdentifier) {
        $platform = $platformByRuntime[$runtime]
        $packageDir = Join-Path $stagingRoot $runtime
        $archiveName = "LibreArm-$Version-$runtime-msix.zip"
        $archivePath = Join-Path $OutputDirectory $archiveName

        dotnet msbuild $desktopProject `
            /restore `
            /p:Configuration=$Configuration `
            /p:Platform=$platform `
            /p:RuntimeIdentifier=$runtime `
            /p:GenerateAppxPackageOnBuild=true `
            /p:AppxBundle=Never `
            /p:UapAppxPackageBuildMode=SideloadOnly `
            /p:AppxPackageSigningEnabled=false `
            /p:AppxPackageDir="$packageDir\"

        if (Test-Path $archivePath) {
            Remove-Item -LiteralPath $archivePath -Force
        }

        $packageFolder = Get-ChildItem -Path $packageDir -Directory |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1

        if ($null -eq $packageFolder) {
            throw "No MSIX package folder was generated for $runtime."
        }

        Compress-Archive -Path $packageFolder.FullName -DestinationPath $archivePath -Force
        Write-Host "Created $archivePath"
    }
}
finally {
    [System.IO.File]::WriteAllBytes($desktopManifest, $originalManifestBytes)

    if (Test-Path $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
