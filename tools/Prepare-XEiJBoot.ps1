[CmdletBinding()]
param(
    [string]$Program
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$xeijRoot = Join-Path $PSScriptRoot 'third_party\xeij\XEiJ-0.26.07.08'
$systemDisk = Join-Path $xeijRoot 'HUMAN302.XDF'
$bootDirectory = Join-Path $xeijRoot 'xeij_boot'

if ([string]::IsNullOrWhiteSpace($Program)) {
    $Program = Join-Path $repoRoot 'src\platform\x68k\build\BREAKOUT.X'
}

$programPath = (Resolve-Path -LiteralPath $Program).Path
if (-not (Test-Path -LiteralPath $systemDisk -PathType Leaf)) {
    throw "Human68kシステムディスクがありません: $systemDisk"
}

$mcopyCommand = Get-Command mcopy -CommandType Application -ErrorAction SilentlyContinue
$mcopyPath = if ($null -ne $mcopyCommand) { $mcopyCommand.Path } else { $null }
if ([string]::IsNullOrWhiteSpace($mcopyPath)) {
    $mcopyCandidate = 'C:\msys64\mingw64\bin\mcopy.exe'
    if (Test-Path -LiteralPath $mcopyCandidate -PathType Leaf) {
        $mcopyPath = $mcopyCandidate
    }
}

if ([string]::IsNullOrWhiteSpace($mcopyPath)) {
    throw 'mcopyが見つかりません。MSYS2で mingw-w64-x86_64-mtools を導入してください。'
}

New-Item -ItemType Directory -Force -Path $bootDirectory | Out-Null

$humanCommand = Join-Path $bootDirectory 'COMMAND.X'
if (-not (Test-Path -LiteralPath $humanCommand -PathType Leaf)) {
    Push-Location $bootDirectory
    try {
        & $mcopyPath -s -o -i '..\HUMAN302.XDF' '::*' '.'
        if ($LASTEXITCODE -ne 0) {
            throw "HUMAN302.XDFの展開に失敗しました。mcopy終了コード: $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }
}

$destination = Join-Path $bootDirectory (Split-Path $programPath -Leaf)
Copy-Item -LiteralPath $programPath -Destination $destination -Force

Write-Output "XEiJ用HFS起動ディレクトリを準備しました: $bootDirectory"
Write-Output "配置したプログラム: $destination"
