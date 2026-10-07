[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [string]$BepInExCoreDirectory = '',
    [string]$OutputRoot = ''
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$game = (Resolve-Path -LiteralPath $GameRoot).Path
if (-not (Test-Path (Join-Path $game 'Hearthstone.exe'))) { throw "GameRoot 中缺少 Hearthstone.exe：$game" }
if ([string]::IsNullOrWhiteSpace($BepInExCoreDirectory)) { $BepInExCoreDirectory = Join-Path $game 'BepInEx\core' }
$bep = (Resolve-Path -LiteralPath $BepInExCoreDirectory).Path
$managed = Join-Path $game 'Hearthstone_Data\Managed'
foreach ($file in @('BepInEx.dll')) { if (-not (Test-Path (Join-Path $bep $file))) { throw "缺少桥接编译引用：$(Join-Path $bep $file)" } }
foreach ($file in @('UnityEngine.CoreModule.dll','UnityEngine.dll')) { if (-not (Test-Path (Join-Path $managed $file))) { throw "缺少游戏程序集：$(Join-Path $managed $file)" } }
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $project 'artifacts\UserBuild' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
function Invoke-Dotnet([string[]]$Args) { & dotnet @Args; if ($LASTEXITCODE -ne 0) { throw "dotnet 命令失败，退出码 $LASTEXITCODE" } }
Write-Host '[1/3] 构建 HearthLeap GUI…'
Invoke-Dotnet @('build',(Join-Path $project 'src\HsAuto'),'-c','Release','--nologo')
Write-Host '[2/3] 构建 Bridge（只读取本机引用，不复制游戏 DLL）…'
Invoke-Dotnet @('build',(Join-Path $project 'src\HsAuto.Bridge'),'-c','Release','--nologo',"-p:GameRoot=$game", "-p:BepInExCoreDirectory=$bep")
Write-Host '[3/3] 运行离线自检…'
Invoke-Dotnet @('run','--project',(Join-Path $project 'tests\SelfTest'),'-c','Release','--no-restore','--',(Join-Path $OutputRoot 'selftest.json'))
$bridge = Join-Path $project 'src\HsAuto.Bridge\bin\Release\netstandard2.0\HsAuto.OpenBridge.dll'
Copy-Item (Join-Path $project 'src\HsAuto\bin\Release\net10.0-windows\HearthLeap.dll') (Join-Path $OutputRoot 'HearthLeap.dll') -Force
Copy-Item $bridge (Join-Path $OutputRoot 'HsAuto.OpenBridge.dll') -Force
Write-Host "输出目录：$OutputRoot"
