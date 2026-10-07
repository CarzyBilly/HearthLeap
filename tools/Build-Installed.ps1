[CmdletBinding()]
param([string]$OutputRoot, [string]$Compiler)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
[xml]$projectInfo = Get-Content -LiteralPath (Join-Path $project 'src\HsAuto\HsAuto.csproj') -Raw
$version = [string]$projectInfo.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw '项目版本号格式无效。' }
if (!$OutputRoot) { $OutputRoot = Join-Path $project "artifacts\HearthLeap-$version-installed" }
if (!$Compiler) { $Compiler = Join-Path $project 'vendor\InnoSetup-6.7.3\ISCC.exe' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if (!$OutputRoot.StartsWith((Join-Path $project 'artifacts') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '输出必须位于项目 artifacts 子目录。' }
if (Test-Path -LiteralPath $OutputRoot) { throw "输出目录已存在：$OutputRoot" }
if (!(Test-Path -LiteralPath $Compiler)) { throw '缺少 Inno Setup 编译器。' }
$payload = Join-Path $OutputRoot 'payload'
$base = Join-Path $project 'artifacts\HearthLeap-0.1.5-pacing-verified\HearthLeap-0.1.5-win-x64'
New-Item -ItemType Directory -Force -Path $payload | Out-Null
& dotnet publish (Join-Path $project 'src\HsAuto') -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $payload --nologo 2>&1 | Tee-Object -FilePath (Join-Path $OutputRoot 'framework-publish.txt')
if ($LASTEXITCODE -ne 0) { throw "框架依赖发布失败：$LASTEXITCODE" }
foreach ($folder in @('Bridge','BepInEx.Runtime','Data')) { Copy-Item -LiteralPath (Join-Path $base $folder) -Destination $payload -Recurse }
foreach ($folder in @('docs','licenses')) { Copy-Item -LiteralPath (Join-Path $project $folder) -Destination $payload -Recurse }
foreach ($file in @('README.md','PROVENANCE.md','LICENSE')) { Copy-Item -LiteralPath (Join-Path $project $file) -Destination $payload }
$notes = @"
HearthLeap $version 常规安装版

桌面的 HearthLeap-$version-Setup.exe 是安装程序，不是便携单文件。
双击安装后，从桌面 HearthLeap 快捷方式启动。
默认安装到 %LOCALAPPDATA%\Programs\HearthLeap，可在向导更改位置。
可从 Windows“已安装的应用”或开始菜单卸载。

此包不内置.NET运行时，需要.NET 10 Windows Desktop Runtime x64。
你这台电脑已有所需环境，安装器会在写入文件前检查环境。
其他电脑若缺环境，需要先从微软官方安装该运行时（仅ASP.NET Runtime不够）。

本版修复开局推荐初始化、起手建议暂存、跨对局换牌确认状态和缓存隔离。
开局缺推荐5秒后自动重新定位读取器，最多2次，不修改盒子绑定。
卡牌独立操作随机等待1000～1800毫秒，停止按钮可立即取消等待。

定时：引擎设置 → 时间设置 → 启用 → 填小时/分钟 → 选择停止后操作 → 保存设置。
下次点击“开始脚本”，成功连接并开始读取后才计时；保存设置本身不计时。
可选仅停止脚本、退出炉石、退出炉石+战网+盒子、停止后关机。
随时：运行中点击“立即停止并执行所选操作”，不必启用定时或等倒计时。
普通“停止”只停脚本，并取消尚未执行的退出/关机，不会关闭游戏。
“取消本次定时操作”只取消计时及后续操作，脚本可继续运行。
关机前有30秒应用内缓冲，可按“停止”或取消；不强制关闭其他程序。
保存失败、游戏身份无法确认或程序不能正常退出时，会停止后续操作。
正常连接的Bridge无需重装；安装器不修改游戏目录、不启动炉石/盒子。
账号设置、战绩和日志沿用原来的本机数据；卸载保留这些数据和游戏插件。
安装目录中的 Data、Bridge、BepInEx.Runtime 不能单独移走。
旧便携文件夹不会自动删除。请用安装后桌面快捷方式，不要继续打开桌面旧HearthLeap目录。

安装程序没有代码签名证书，首次运行可能出现Windows来源提示。
验证范围：离线自检、本机模拟管道、隐藏GUI、安装器与文件哈希；不自动进行真实对局。
离线测试通过不代表盒子服务器永远提供推荐，仍需你正常登录后的多局验收。
"@
[IO.File]::WriteAllText((Join-Path $payload '安装版使用说明.txt'), $notes, [Text.UTF8Encoding]::new($false))
$runtime = Get-Content -LiteralPath (Join-Path $payload 'HearthLeap.runtimeconfig.json') -Raw | ConvertFrom-Json
if (@($runtime.runtimeOptions.frameworks).Count -ne 2 -or $runtime.runtimeOptions.includedFrameworks) { throw '不是框架依赖版。' }
if (Test-Path -LiteralPath (Join-Path $payload 'coreclr.dll')) { throw '不应内置.NET运行时。' }
if ((Get-FileHash -LiteralPath (Join-Path $base 'Bridge\HsAuto.OpenBridge.dll')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $payload 'Bridge\HsAuto.OpenBridge.dll')).Hash) { throw 'Bridge发生非预期变化。' }
& $Compiler ("/DAppVersion=$version") ("/DSourceRoot=$payload") ("/DOutputRoot=$OutputRoot") ("/DIconPath=" + (Join-Path $project 'src\HsAuto\Assets\LegendRush.ico')) (Join-Path $PSScriptRoot 'HearthLeap-Setup.iss') 2>&1 | Tee-Object -FilePath (Join-Path $OutputRoot 'installer-build.txt')
if ($LASTEXITCODE -ne 0) { throw "安装程序构建失败：$LASTEXITCODE" }
Get-FileHash -LiteralPath (Join-Path $OutputRoot "HearthLeap-$version-Setup.exe") | Export-Csv -LiteralPath (Join-Path $OutputRoot 'installer-hash.csv') -NoTypeInformation -Encoding utf8
Write-Host "INSTALLER=$(Join-Path $OutputRoot "HearthLeap-$version-Setup.exe")"
