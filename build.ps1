$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '需要 Windows x64 和 .NET Framework 4.8。' }
$references = @('System.dll','System.Core.dll','System.Xaml.dll','System.Xml.dll','System.Xml.Linq.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Drawing.dll')
foreach ($name in @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll')) { $references += Join-Path $framework ('WPF/' + $name) }
$options = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001','/warn:4','/warnaserror+',('/win32manifest:' + (Join-Path $PSScriptRoot 'src/app.manifest')),('/win32icon:' + (Join-Path $PSScriptRoot 'assets/codex.ico')))
foreach ($reference in $references) { $options += '/reference:' + $reference }
& $compiler @options ('/out:' + (Join-Path $PSScriptRoot 'Start-Codex-Industrial.exe')) (Join-Path $PSScriptRoot 'src/Launcher.cs')
if ($LASTEXITCODE -ne 0) { throw '启动器构建失败。' }
& $compiler @options ('/out:' + (Join-Path $PSScriptRoot 'Setup-Codex-Animation.exe')) (Join-Path $PSScriptRoot 'src/SetupHost.cs')
if ($LASTEXITCODE -ne 0) { throw '安装程序构建失败。' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Setup-Codex-Animation.exe') -Destination (Join-Path $PSScriptRoot '安装启动动画.exe') -Force
Write-Output 'Build succeeded.'
