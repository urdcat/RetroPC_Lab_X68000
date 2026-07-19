[CmdletBinding()]
param(
    [switch]$Check,

    [string]$BootDirectory,

    [string]$HostDirectory,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$XEiJArgument
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$xeijRoot = Join-Path $PSScriptRoot 'third_party\xeij\XEiJ-0.26.07.08'
$jar = Join-Path $xeijRoot 'XEiJ.jar'
$systemDisk = Join-Path $xeijRoot 'HUMAN302.XDF'

foreach ($requiredFile in @($jar, $systemDisk, (Join-Path $xeijRoot 'data\license_XEiJ.txt'))) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "XEiJの必要ファイルがありません: $requiredFile"
    }
}

$javaCommand = Get-Command java -CommandType Application -ErrorAction SilentlyContinue
$javaPath = if ($null -ne $javaCommand) { $javaCommand.Path } else { $null }

if ($null -eq $javaPath) {
    $javaCandidates = [System.Collections.Generic.List[string]]::new()
    foreach ($scope in @('Process', 'User', 'Machine')) {
        $javaHome = [Environment]::GetEnvironmentVariable('JAVA_HOME', $scope)
        if (-not [string]::IsNullOrWhiteSpace($javaHome)) {
            $javaCandidates.Add((Join-Path $javaHome 'bin\\java.exe'))
        }
    }

    $adoptiumRoot = 'C:\\Program Files\\Eclipse Adoptium'
    if (Test-Path -LiteralPath $adoptiumRoot -PathType Container) {
        Get-ChildItem -LiteralPath $adoptiumRoot -Directory |
            Sort-Object Name -Descending |
            ForEach-Object { $javaCandidates.Add((Join-Path $_.FullName 'bin\\java.exe')) }
    }

    $javaPath = $javaCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($javaPath)) {
    throw 'Javaが見つかりません。OpenJDK 26以上を導入して、java.exe を PATH に追加してください。'
}

$javaVersion = (& $javaPath -version 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0 -or $javaVersion -notmatch 'version "(?<major>\d+)') {
    throw "Javaのバージョンを確認できません。OpenJDK 26以上が必要です。出力: $javaVersion"
}

if ([int]$Matches.major -lt 26) {
    throw "Java $($Matches.major) が検出されました。XEiJ 0.26.07.08にはOpenJDK 26以上が必要です。"
}

if ($Check) {
    Write-Output "XEiJ起動条件を確認しました。Java $($Matches.major): $javaPath"
    exit 0
}

$bootTarget = 'HUMAN302.XDF'
if (-not [string]::IsNullOrWhiteSpace($BootDirectory)) {
    $resolvedBootDirectory = (Resolve-Path -LiteralPath $BootDirectory).Path
    if (-not (Test-Path -LiteralPath $resolvedBootDirectory -PathType Container)) {
        throw "HFS起動ディレクトリがありません: $BootDirectory"
    }
    $bootTarget = $resolvedBootDirectory
}

$launchArguments = @(
    '-jar', 'XEiJ.jar',
    '-config=default',
    '-saveonexit=off',
    '-model=EXPERT',
    '-mpu=68000',
    '-clock=10',
    '-memory=2',
    "-boot=$bootTarget"
)

if (-not [string]::IsNullOrWhiteSpace($HostDirectory)) {
    $resolvedHostDirectory = (Resolve-Path -LiteralPath $HostDirectory).Path
    if (-not (Test-Path -LiteralPath $resolvedHostDirectory -PathType Container)) {
        throw "共有するホストディレクトリがありません: $HostDirectory"
    }
    $launchArguments += "-hf0=$resolvedHostDirectory"
}

$launchArguments += $XEiJArgument

Push-Location $xeijRoot
try {
    & $javaPath @launchArguments
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
