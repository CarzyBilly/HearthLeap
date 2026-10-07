[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [Parameter(Mandatory=$true)][string]$RuntimeDirectory,
    [Parameter(Mandatory=$true)][string]$CardDataPath,
    [string]$BepInExCoreDirectory = '',
    [string]$OutputRoot = ''
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifacts = [IO.Path]::GetFullPath((Join-Path $project 'artifacts'))
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $artifacts ('release-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if (-not $OutputRoot.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '输出必须位于本项目 artifacts 的子目录，避免误写源码或游戏目录。' }
if (Test-Path -LiteralPath $OutputRoot) { throw "输出目录已存在，原文件不会覆盖：$OutputRoot；请选择一个新子目录。" }
$GameRoot = (Resolve-Path -LiteralPath $GameRoot).Path
$RuntimeDirectory = (Resolve-Path -LiteralPath $RuntimeDirectory).Path
$CardDataPath = (Resolve-Path -LiteralPath $CardDataPath).Path
if (-not (Test-Path -LiteralPath (Join-Path $GameRoot 'Hearthstone.exe'))) { throw 'GameRoot 中缺少 Hearthstone.exe。' }
if ([string]::IsNullOrWhiteSpace($BepInExCoreDirectory)) { $BepInExCoreDirectory = Join-Path $RuntimeDirectory 'BepInEx\core' }
foreach ($file in @('winhttp.dll','doorstop_config.ini','BepInEx\core\BepInEx.dll','BepInEx\core\BepInEx.Preloader.dll')) { if (-not (Test-Path -LiteralPath (Join-Path $RuntimeDirectory $file))) { throw "运行时缺少 $file" } }
if ([IO.Path]::GetFileName($CardDataPath) -ne 'hdt-by-id.json') { throw '只能打包公开卡牌数据 hdt-by-id.json，不复制历史或账号文件。' }
$portable = Join-Path $OutputRoot 'HearthLeap-0.1.5-win-x64'
$sourceStage = Join-Path $OutputRoot 'HearthLeap-0.1.5-source'
New-Item -ItemType Directory -Force -Path $portable,$sourceStage | Out-Null

function Run-Dotnet([string[]]$Arguments, [string]$LogFile = '') {
    if ($LogFile) { & dotnet @Arguments 2>&1 | Tee-Object -FilePath $LogFile } else { & dotnet @Arguments }
    if ($LASTEXITCODE -ne 0) { throw "dotnet 命令失败，退出码 $LASTEXITCODE" }
}
function Copy-Tree([string]$Source, [string]$Destination) {
    if (-not (Test-Path -LiteralPath $Source)) { throw "缺少发布输入：$Source" }
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $Source -Force) { Copy-Item -LiteralPath $item.FullName -Destination $Destination -Recurse -Force }
}
function Write-Utf8([string]$Path, [string]$Text) { [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false)) }
New-Item -ItemType Directory -Force -Path (Join-Path $project 'evidence') | Out-Null
Write-Host '[1/7] 构建 GUI / Bridge（不复制游戏 DLL、不修改游戏目录）…'
Run-Dotnet -Arguments @('build', (Join-Path $project 'src\HsAuto'), '-c', 'Release', '--nologo', '-t:Rebuild') -LogFile (Join-Path $project 'evidence\app-build.txt')
Run-Dotnet -Arguments @('build', (Join-Path $project 'src\HsAuto.Bridge'), '-c', 'Release', '--nologo', '-t:Rebuild', "-p:GameRoot=$GameRoot", "-p:BepInExCoreDirectory=$BepInExCoreDirectory") -LogFile (Join-Path $project 'evidence\bridge-build.txt')
Write-Host '[2/7] 运行离线自检和隐藏窗口 GUI smoke…'
Run-Dotnet -Arguments @('run','--project',(Join-Path $project 'tests\SelfTest'),'-c','Release','--',(Join-Path $project 'evidence\self-test.json')) -LogFile (Join-Path $project 'evidence\self-test.txt')
Run-Dotnet -Arguments @((Join-Path $project 'src\HsAuto\bin\Release\net10.0-windows\HearthLeap.dll'),'--smoke',(Join-Path $project 'evidence\smoke-hearthleap-0.1.5'))
Write-Host '[3/7] 发布 Windows x64 自包含 GUI…'
Run-Dotnet -Arguments @('publish',(Join-Path $project 'src\HsAuto'),'-c','Release','-r','win-x64','--self-contained','true','-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true','-p:PublishTrimmed=false','-p:DebugType=None','-p:DebugSymbols=false','-o',$portable,'--nologo') -LogFile (Join-Path $project 'evidence\publish.txt')
Write-Host '[4/7] 复制 Bridge、干净运行时和唯一卡牌数据…'
New-Item -ItemType Directory -Force -Path (Join-Path $portable 'Bridge'),(Join-Path $portable 'BepInEx.Runtime\BepInEx\core'),(Join-Path $portable 'BepInEx.Runtime\BepInEx\corelib_override_full'),(Join-Path $portable 'Data') | Out-Null
Copy-Item -LiteralPath (Join-Path $project 'src\HsAuto.Bridge\bin\Release\netstandard2.0\HsAuto.OpenBridge.dll') -Destination (Join-Path $portable 'Bridge\HsAuto.OpenBridge.dll')
foreach ($file in @('winhttp.dll','doorstop_config.ini','.doorstop_version')) { Copy-Item -LiteralPath (Join-Path $RuntimeDirectory $file) -Destination (Join-Path $portable 'BepInEx.Runtime') }
foreach ($dll in Get-ChildItem -LiteralPath (Join-Path $RuntimeDirectory 'BepInEx\core') -File -Filter '*.dll') { Copy-Item -LiteralPath $dll.FullName -Destination (Join-Path $portable 'BepInEx.Runtime\BepInEx\core') }
foreach ($dll in Get-ChildItem -LiteralPath (Join-Path $RuntimeDirectory 'BepInEx\corelib_override_full') -File -Filter '*.dll') { Copy-Item -LiteralPath $dll.FullName -Destination (Join-Path $portable 'BepInEx.Runtime\BepInEx\corelib_override_full') }
Copy-Item -LiteralPath $CardDataPath -Destination (Join-Path $portable 'Data\hdt-by-id.json')
foreach ($name in @('README.md','PROVENANCE.md','LICENSE')) { Copy-Item -LiteralPath (Join-Path $project $name) -Destination $portable }
Copy-Tree (Join-Path $project 'docs') (Join-Path $portable 'docs')
Copy-Tree (Join-Path $project 'licenses') (Join-Path $portable 'licenses')
Copy-Item -LiteralPath (Join-Path $project 'docs\使用说明.txt') -Destination (Join-Path $portable '开始使用.txt')
Write-Utf8 (Join-Path $portable '发布信息.txt') "版本：0.1.5`r`n平台：Windows x64，自包含 GUI`r`n构建时间：$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')`r`n范围：离线/本机管道测试通过；真实对局待人工验收。"
Write-Host '[5/7] 验证发布后的可执行文件…'
$testOut = Join-Path $project 'evidence\portable-smoke-hearthleap-0.1.5'
$exe = Join-Path $portable 'HearthLeap.exe'
$process = Start-Process -FilePath $exe -ArgumentList @('--smoke', ('"' + $testOut + '"')) -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $testOut 'smoke.txt'))) { throw '发布后的 GUI 验证失败。' }
$ev = Join-Path $portable 'evidence'; New-Item -ItemType Directory -Force -Path $ev | Out-Null
foreach ($name in @('self-test.json','self-test.txt','app-build.txt','bridge-build.txt','publish.txt')) { Copy-Item -LiteralPath (Join-Path $project ('evidence\' + $name)) -Destination $ev }
Copy-Tree $testOut (Join-Path $ev 'portable-smoke')
Write-Host '[6/7] 复制源码（不包含 bin/obj、游戏文件、旧包、日志和备份）…'
foreach ($folder in @('src','tests','tools')) {
    $inputRoot = Join-Path $project $folder
    foreach ($file in Get-ChildItem -LiteralPath $inputRoot -Recurse -File -Force | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }) {
        $dest = Join-Path $sourceStage ([IO.Path]::GetRelativePath($project,$file.FullName))
        New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($dest)) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $dest
    }
}
foreach ($name in @('README.md','PROVENANCE.md','LICENSE','.gitignore')) { Copy-Item -LiteralPath (Join-Path $project $name) -Destination $sourceStage }
foreach ($folder in @('docs','licenses')) { Copy-Tree (Join-Path $project $folder) (Join-Path $sourceStage $folder) }
Copy-Tree $ev (Join-Path $sourceStage 'evidence')
Write-Host '[7/7] 哈希、压缩包…'
$hashes = Get-ChildItem -LiteralPath $portable -Recurse -File | Sort-Object FullName | ForEach-Object { [pscustomobject]@{path=[IO.Path]::GetRelativePath($portable,$_.FullName);sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash;bytes=$_.Length} }
$hashes | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $portable 'SHA256SUMS.json') -Encoding utf8
$portableZip = Join-Path $OutputRoot 'HearthLeap-0.1.5-win-x64.zip'
$sourceZip = Join-Path $OutputRoot 'HearthLeap-0.1.5-source.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($portable,$portableZip,[IO.Compression.CompressionLevel]::Optimal,$true)
[IO.Compression.ZipFile]::CreateFromDirectory($sourceStage,$sourceZip,[IO.Compression.CompressionLevel]::Optimal,$true)
Get-FileHash -LiteralPath $portableZip,$sourceZip -Algorithm SHA256 | Export-Csv -LiteralPath (Join-Path $OutputRoot 'artifact-hashes.csv') -NoTypeInformation -Encoding utf8
Write-Host "PORTABLE=$portable"
Write-Host "ZIP=$portableZip"
Write-Host "SOURCE=$sourceZip"
