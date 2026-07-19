[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$version = '0.26.07.08'
$archiveName = 'XEiJ_0260708.zip'
$expectedSha256 = '3EA0A1EE273CD6A4BF5D8AF7275AD7400AF61C64DCA0049F55A9841F8933BF3B'
$downloadUri = "https://stdkmd.net/xeij/$archiveName"
$vendorRoot = Join-Path $PSScriptRoot 'third_party\xeij'
$destination = Join-Path $vendorRoot "XEiJ-$version"
$archive = Join-Path $vendorRoot $archiveName

if (Test-Path -LiteralPath (Join-Path $destination 'XEiJ.jar') -PathType Leaf) {
    Write-Output "XEiJ $version は既に導入されています: $destination"
    exit 0
}

if (Test-Path -LiteralPath $destination) {
    throw "不完全なXEiJ展開先が存在します: $destination。内容を確認してから削除または復旧してください。"
}

New-Item -ItemType Directory -Force $vendorRoot | Out-Null
Invoke-WebRequest -Uri $downloadUri -OutFile $archive

$actualSha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
if ($actualSha256 -ne $expectedSha256) {
    throw "XEiJアーカイブのSHA-256が一致しません。期待値: $expectedSha256 / 実値: $actualSha256"
}

New-Item -ItemType Directory -Path $destination | Out-Null
Expand-Archive -LiteralPath $archive -DestinationPath $destination

foreach ($requiredFile in @(
    'XEiJ.jar',
    'HUMAN302.XDF',
    'data\license_XEiJ.txt',
    'xeij\MC68000.java'
)) {
    if (-not (Test-Path -LiteralPath (Join-Path $destination $requiredFile) -PathType Leaf)) {
        throw "XEiJの展開内容が不完全です: $requiredFile"
    }
}

Write-Output "XEiJ $version を導入しました: $destination"
