$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$preservationRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$inventory = [Collections.Generic.List[object]]::new()
function Preserve-File([string]$Source, [string]$Relative, [string]$Category, [string]$Publication) {
    $sourcePath = [IO.Path]::GetFullPath((Join-Path $repoRoot $Source))
    $targetPath = [IO.Path]::GetFullPath((Join-Path $preservationRoot $Relative))
    if (-not $sourcePath.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar)) { throw 'Source escaped workspace' }
    if (-not $targetPath.StartsWith($preservationRoot + [IO.Path]::DirectorySeparatorChar)) { throw 'Destination escaped preservation folder' }
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Missing required preservation input: $Source" }
    $sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if (Test-Path -LiteralPath $targetPath) {
        if ((Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sourceHash) { throw "Existing snapshot differs: $Relative" }
    } else {
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($targetPath)) -Force | Out-Null
        Copy-Item -LiteralPath $sourcePath -Destination $targetPath
    }
    $inventory.Add([pscustomobject]@{category=$Category;original=$sourcePath;preserved=$targetPath;relative=$Relative;bytes=(Get-Item -LiteralPath $targetPath).Length;sha256=$sourceHash;publication=$Publication})
}
foreach ($file in @('hello.s','breakout.s','Makefile')) {
    Preserve-File "src/platform/x68k/$file" "human68k-samples/source/$file" 'old-games' 'authored-source'
}
foreach ($file in @('HELLO.X','BREAKOUT.X')) {
    Preserve-File "src/platform/x68k/build/$file" "human68k-samples/build/$file" 'old-games' 'local-ignored-binary'
}
foreach ($file in @('demo.m68','native_fast.m68','ssaa.m68','ssaa_resolve_fast.m68','local_cached.m68','reproduction_logic.m68','auto_cycle.m68','boss_animation.m68','direct_boss.m68')) {
    Preserve-File "src/platform/x68k/champon8_poly_demo/$file" "legacy-polygon/source/$file" 'old-rotation-and-game' 'authored-source'
}
foreach ($file in @('build_champon8_polygon_demo.py','build_champon8_ssaa_demo.py','build_champon8_local_cached.py','build_champon8_reproduction.py','build_champon8_auto_cycle.py','build_champon8_boss_animation.py','build_champon8_direct_boss.py','build_champon8_clock_fix.py','build_champon8_player_speed.py','benchmark_champon8_native_fast.py','benchmark_champon8_ssaa_resolve.py')) {
    Preserve-File "tools/$file" "legacy-polygon/tools/$file" 'old-build-dependencies' 'authored-source'
}
$legacyBuild = 'src/platform/x68k/champon8_poly_demo/build'
$payloads = @(
    'x68000-starcruiser-512.bin', 'model_generated.inc',
    'ssaa/x68000-starcruiser-ssaa.bin', 'ssaa/model_generated.inc', 'ssaa/aa_lookup.bin',
    'native-fast-20260916/starless/demo.bin', 'native-fast-20260916/starless/build.json',
    'native-fast-20260916/ssaa-resolve-fast/demo.bin',
    'local-cached-20260916/verify-1789566540613092200/scene-0/guest.bin',
    'local-cached-20260916/verify-1789566540613092200/scene-0/build.json',
    'local-cached-20260916/verify-1789566540613092200/scene-1/guest.bin',
    'local-cached-20260916/verify-1789566540613092200/scene-1/build.json',
    'auto-cycle-20260917/guest.bin', 'auto-cycle-20260917/build.json',
    'boss-animation-20260917/verify-1789593386315951000/choice-0/guest.bin',
    'boss-animation-20260917/verify-1789593386315951000/choice-0/build.json',
    'boss-animation-20260917/verify-1789593386315951000/choice-1/guest.bin',
    'boss-animation-20260917/verify-1789593386315951000/choice-1/build.json',
    'direct-boss-20260917/verify-1789594816336667200/direct-0/guest.bin',
    'direct-boss-20260917/verify-1789594816336667200/direct-0/build.json',
    'reproduction-20260917/guest.bin', 'reproduction-20260917/build.json',
    'clock-fix-20260918/verify-1789698053920466700/candidate/guest.bin',
    'clock-fix-20260918/verify-1789698053920466700/candidate/build.json',
    'player-speed-20260918/verify-1789698952021697700/candidate/guest.bin',
    'player-speed-20260918/verify-1789698952021697700/candidate/build.json',
    'player-speed-20260918/verify-1789698952021697700/shared-latest.png'
)
foreach ($file in $payloads) {
    Preserve-File "$legacyBuild/$file" "legacy-polygon/build/$file" 'old-runtime-payload' 'local-ignored-binary-or-derived-art'
}
$generatedDirectories = @(
    'local-cached-20260916/verify-1789566540613092200/scene-0',
    'local-cached-20260916/verify-1789566540613092200/scene-1',
    'auto-cycle-20260917', 'reproduction-20260917',
    'boss-animation-20260917/verify-1789593386315951000/choice-0',
    'boss-animation-20260917/verify-1789593386315951000/choice-1',
    'direct-boss-20260917/verify-1789594816336667200/direct-0',
    'clock-fix-20260918/verify-1789698053920466700/candidate',
    'player-speed-20260918/verify-1789698952021697700/candidate'
)
foreach ($directory in $generatedDirectories) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repoRoot "$legacyBuild/$directory") -Recurse -File | Where-Object { $_.Extension -in @('.m68','.inc') }) {
        $relativeGenerated = [IO.Path]::GetRelativePath((Join-Path $repoRoot $legacyBuild),$file.FullName).Replace('\','/')
        $relativeTarget = "legacy-polygon/build/$relativeGenerated"
        if (-not ($inventory | Where-Object relative -eq $relativeTarget)) {
            Preserve-File "$legacyBuild/$relativeGenerated" $relativeTarget 'old-generated-build-dependencies' 'local-ignored-generated-source'
        }
    }
}
foreach ($version in @('quality-before-20261005','quality-before-20261005-02')) {
    $category = if ($version -eq 'quality-before-20261005') { 'axxphorg-first-rich-v04' } else { 'axxphorg-quality01' }
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repoRoot "prototypes/axxphorg-rich/build/$version") -File) {
        $safeSource = $file.Extension -in @('.py','.m68','.html','.txt') -or $file.Name -eq 'settings.json'
        $section = if ($safeSource) { 'source' } else { 'build' }
        $publication = if ($safeSource) { 'authored-frozen-source' } else { 'local-ignored-evidence' }
        Preserve-File "prototypes/axxphorg-rich/build/$version/$($file.Name)" "$category/$section/$($file.Name)" $category $publication
    }
}
$dependencies = @(
    'D:/work/DevelopTools/Assemblers/m68kasm/dist/m68kasm-0.9.0-preview.1-win-x64/bin/m68kasm.exe',
    'D:/work/DevelopTools/Emulators/Champon8/client.py',
    'D:/work/PolygonGames/proposals/20261005-axxphorg-six-part-background-v04/manifest.json',
    'D:/work/PolygonGames/proposals/20261005-axxphorg-six-part-background-v05/manifest.json',
    'D:/work/PolygonGames/specs/20260916-pyuta-cross-machine-reproduction-v1/SPEC.md',
    'C:/Users/urdca/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe',
    'D:/work/PC8001mkⅡで何か動かそう/ports/starcruiser-dual-demo/tools/model_reference.py',
    'D:/work/PC8001mkⅡで何か動かそう/tmp/starcruiser-render-opt-20260905/ports/star-cruiser/reference/Star Cruiser (Japan).md',
    'C:/msys64/home/urdca/xdev68k/run68/run68.exe',
    'C:/msys64/home/urdca/xdev68k/x68k_bin/HAS060.X',
    'C:/msys64/home/urdca/xdev68k/x68k_bin/hlk301.x'
)
$external = foreach ($dependency in $dependencies) {
    $exists = Test-Path -LiteralPath $dependency -PathType Leaf
    [pscustomobject]@{path=$dependency;present=$exists;sha256= $(if ($exists) { (Get-FileHash -LiteralPath $dependency -Algorithm SHA256).Hash.ToLowerInvariant() } else { $null });copied=$false;publication='external-reference-only'}
}
$result = [pscustomobject]@{reportId='20261005-x68000-preservation-inventory-01';workspace=$repoRoot;originalsUnmodified=$true;files=$inventory;externalDependencies=$external;excluded='ROM and extracted reference geometry/BIN retained locally, not published; shared credentials and huge video/cache not copied'}
$json = $result | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText((Join-Path $preservationRoot 'INVENTORY.json'), $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
$inventory | Group-Object category | Select-Object Name,Count
