[CmdletBinding()]
param(
    [ValidateSet("win-x64", "linux-x64", "osx-x64", "osx-arm64")]
    [string]$Runtime = "win-x64",

    [string]$OutputRoot = (Join-Path $PSScriptRoot "dist")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$project = Join-Path $PSScriptRoot "src\M68kAsm\M68kAsm.csproj"
$projectXml = [xml](Get-Content -Raw -LiteralPath $project)
$versionNode = $projectXml.SelectSingleNode("/Project/PropertyGroup/Version")
if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
    throw "M68kAsm.csprojにVersionがありません"
}

$version = $versionNode.InnerText
$packageName = "m68kasm-$version-$Runtime"
$outputRootFull = [IO.Path]::GetFullPath($OutputRoot)
$stage = Join-Path $outputRootFull $packageName
$stageFull = [IO.Path]::GetFullPath($stage)
$relativeStage = [IO.Path]::GetRelativePath($outputRootFull, $stageFull)

if ([IO.Path]::IsPathRooted($relativeStage) -or $relativeStage.StartsWith("..", [StringComparison]::Ordinal)) {
    throw "パッケージ作業ディレクトリが出力先の外です: $stageFull"
}

if (Test-Path -LiteralPath $stageFull) {
    Remove-Item -LiteralPath $stageFull -Recurse -Force
}

New-Item -ItemType Directory -Path $stageFull | Out-Null
$publishDirectory = Join-Path $stageFull "bin"

dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publishに失敗しました: exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot "package\README.md") -Destination $stageFull
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "package\INTERNAL-PREVIEW-NOTICE.txt") -Destination $stageFull

$examplesDirectory = Join-Path $stageFull "examples"
New-Item -ItemType Directory -Path $examplesDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "examples\language_features.m68") -Destination $examplesDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "examples\labels_and_addressing.m68") -Destination $examplesDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "examples\addressing_modes.m68") -Destination $examplesDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "examples\constant_expressions.m68") -Destination $examplesDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "examples\logical_operations.m68") -Destination $examplesDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "examples\structured_macros.m68") -Destination $examplesDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "examples\include") -Destination $examplesDirectory -Recurse

$docsDirectory = Join-Path $stageFull "docs"
New-Item -ItemType Directory -Path $docsDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "..\..\docs\ASSEMBLER_LANGUAGE.md") -Destination $docsDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "..\..\docs\INSTRUCTION_SUPPORT.md") -Destination $docsDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "..\..\docs\INSTRUCTION_IMPLEMENTATION_REPORT_2026-07-19.md") -Destination $docsDirectory

$dotnetLicenseDirectory = Join-Path $stageFull "licenses\dotnet"
New-Item -ItemType Directory -Path $dotnetLicenseDirectory -Force | Out-Null
$dotnetRoot = Split-Path -Parent (Get-Command dotnet).Source
$dotnetLicense = Join-Path $dotnetRoot "LICENSE.txt"
$dotnetNotices = Join-Path $dotnetRoot "ThirdPartyNotices.txt"
if (!(Test-Path -LiteralPath $dotnetLicense) -or !(Test-Path -LiteralPath $dotnetNotices)) {
    throw ".NETランタイムのライセンス文書が見つかりません: $dotnetRoot"
}

Copy-Item -LiteralPath $dotnetLicense -Destination $dotnetLicenseDirectory
Copy-Item -LiteralPath $dotnetNotices -Destination $dotnetLicenseDirectory

$manifest = [ordered]@{
    name = "m68kasm"
    version = $version
    channel = "preview"
    runtime = $Runtime
    executable = "bin/m68kasm.exe"
    cpuProfiles = @("MC68000", "MC68010", "MC68020", "MC68030")
    defaultCpu = "MC68000"
    outputFormat = "raw-big-endian-machine-code"
    targetSpecificComponents = @()
    externalToolDependencies = @()
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $stageFull "manifest.json") -Encoding utf8NoBOM

$zipPath = Join-Path $outputRootFull "$packageName.zip"
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $stageFull "*") -DestinationPath $zipPath -CompressionLevel Optimal
$hash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
$hashLine = "$($hash.Hash.ToLowerInvariant()) *$([IO.Path]::GetFileName($zipPath))"
Set-Content -LiteralPath "$zipPath.sha256" -Value $hashLine -Encoding ascii

Write-Host "package: $zipPath"
Write-Host "sha256: $($hash.Hash.ToLowerInvariant())"
