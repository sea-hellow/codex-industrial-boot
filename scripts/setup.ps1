[CmdletBinding()]
param(
    [ValidateSet('Install','Restore','Verify')][string]$Action = 'Install',
    [string]$Destination,
    [string[]]$SearchRoots,
    [switch]$NoDefaultLinks,
    [switch]$NoLegacyMigration,
    [switch]$Quiet
)
$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$Destination) { $Destination = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CodexIndustrialNative' }
$Destination = [IO.Path]::GetFullPath($Destination).TrimEnd('\','/')
if ($Destination -eq [IO.Path]::GetPathRoot($Destination).TrimEnd('\','/')) { throw '安装目录不能是磁盘根目录。' }
$exePath = Join-Path $Destination 'Start-Codex-Industrial.exe'
$stateRoot = Join-Path $Destination '.integration'
$stateFile = Join-Path $stateRoot 'state.json'
$reportFile = Join-Path $stateRoot 'report.json'
$report = [ordered]@{ Action=$Action; Status='Running'; Replaced=0; Created=0; Restored=0; AlreadyOwned=0; Preserved=@(); Errors=@(); Started=(Get-Date).ToString('o') }
$mutex = New-Object Threading.Mutex($false,'Local\CodexIndustrialNative-Setup')
$locked = $false
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class CodexPhysicalDirectory {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern uint GetFinalPathNameByHandle(SafeFileHandle handle,StringBuilder path,uint length,uint flags);
    public static string Resolve(string path) {
        using(var h=CreateFile(path,0,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero)) {
            if(h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            var buffer=new StringBuilder(32768);
            uint count=GetFinalPathNameByHandle(h,buffer,(uint)buffer.Capacity,0);
            if(count==0 || count>=buffer.Capacity) throw new Win32Exception(Marshal.GetLastWin32Error());
            string value=buffer.ToString();
            if(value.StartsWith(@"\\?\UNC\")) return @"\\"+value.Substring(8);
            return value.StartsWith(@"\\?\") ? value.Substring(4) : value;
        }
    }
}
'@
function Hash([string]$Path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try { [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
    finally { $stream.Dispose(); $sha.Dispose() }
}
function Equal([string]$Left,[string]$Right) { [string]::Equals($Left,$Right,[StringComparison]::OrdinalIgnoreCase) }
function Save-Json([string]$Path,$Value) {
    $temp = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $temp -Encoding UTF8
    Move-Item -LiteralPath $temp -Destination $Path -Force
}
function Save-State { Save-Json $stateFile $script:state }
function Link([string]$Path) { $script:ws.CreateShortcut($Path) }
function Link-Ids([string]$Path) {
    try {
        $folder = $script:shell.NameSpace([IO.Path]::GetDirectoryName($Path))
        $item = $folder.ParseName([IO.Path]::GetFileName($Path))
        @([string]$item.ExtendedProperty('System.AppUserModel.ID'), [string]$item.ExtendedProperty('System.Link.TargetParsingPath'))
    } catch { @() }
}
function Is-Official([string]$Path) {
    $link = Link $Path
    if ($script:officialPaths.Contains($link.TargetPath)) { return $true }
    $shellTarget = 'shell:AppsFolder\' + $script:appId
    if ((Equal $link.TargetPath $shellTarget) -or ((Equal $link.TargetPath (Join-Path $env:WINDIR 'explorer.exe')) -and (Equal $link.Arguments.Trim().Trim('"') $shellTarget))) { return $true }
    foreach ($id in @(Link-Ids $Path)) { if ((Equal $id $script:appId) -or (Equal $id $shellTarget)) { return $true } }
    return $false
}
function Is-Owned([string]$Path) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    $link = Link $Path
    (Equal $link.TargetPath $exePath) -and [string]::IsNullOrEmpty($link.Arguments) -and (Equal $link.WorkingDirectory $Destination)
}
function Active-Record([string]$Path) { @($script:state.Entries | Where-Object { (Equal $_.Path $Path) -and !$_.Restored }) | Select-Object -Last 1 }
function Install-Link([string]$Path,[bool]$Create=$false) {
    $exists = Test-Path -LiteralPath $Path -PathType Leaf
    $record = Active-Record $Path
    if ($record) {
        if ($exists -and (Hash $Path) -eq $record.InstalledSHA256 -and (Is-Owned $Path)) { $script:report.AlreadyOwned++; return }
        $script:report.Preserved += @{ Path=$Path; Reason='Shortcut was changed or removed after installation.' }; return
    }
    if (!$exists -and !$Create) { return }
    if ($exists -and (([IO.File]::GetAttributes($Path) -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { return }
    $legacyBackup = $null
    $old = if ($exists) { Link $Path } else { $null }
    if ($exists -and !(Is-Official $Path)) {
        if (!$script:legacy) { return }
        $legacyExe = Join-Path $script:legacyRoot 'CodexIndustrial.exe'
        if (!(Equal $old.TargetPath $legacyExe)) { return }
        if ($script:legacy.desktop_entry -and (Equal $Path $script:legacy.desktop_entry.path)) {
            if ((Hash $Path) -ne $script:legacy.desktop_entry.attached_sha256) { return }
            $candidate = [IO.Path]::GetFullPath((Join-Path $script:legacyRoot $script:legacy.desktop_entry.backup))
            if (!$candidate.StartsWith($script:legacyRoot.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $candidate) -or !(Is-Official $candidate)) { throw '旧版原图标备份无效，已保留入口。' }
            $legacyBackup = $candidate
        } elseif (!(Equal $Path $script:legacy.shortcut)) { return }
    }
    $beforeHash = if ($exists) { Hash $Path } else { $null }
    $backup = $null
    if ($exists) {
        $backup = Join-Path $stateRoot ('backups/' + [Guid]::NewGuid().ToString('N') + '.lnk')
        $backupSource = if ($legacyBackup) { $legacyBackup } else { $Path }
        [IO.File]::WriteAllBytes($backup,[IO.File]::ReadAllBytes($backupSource))
        if ((Hash $backup) -ne (Hash $backupSource)) { throw '原图标备份校验失败。' }
    }
    $temp = Join-Path ([IO.Path]::GetDirectoryName($Path)) ('CodexIndustrial-' + [Guid]::NewGuid().ToString('N') + '.lnk')
    try {
        $link = Link $temp
        $link.TargetPath = $exePath; $link.Arguments = ''; $link.WorkingDirectory = $Destination
        $link.Description = 'Codex Industrial - startup animation'
        $link.IconLocation = if ($old -and $old.IconLocation) { $old.IconLocation } else { (Join-Path $Destination 'assets/codex.ico') + ',0' }
        if ($old) { $link.Hotkey = $old.Hotkey; $link.WindowStyle = $old.WindowStyle }
        $link.Save()
        if (!(Is-Owned $temp)) { throw '新入口校验失败。' }
        if ($exists -and (!(Test-Path -LiteralPath $Path) -or (Hash $Path) -ne $beforeHash)) { throw '安装时图标已变化，已保留。' }
        if (!$exists -and (Test-Path -LiteralPath $Path)) { throw '目标位置出现其他图标，已保留。' }
        $record = [pscustomobject]@{ Path=$Path; OriginalExists=$exists; Backup=$backup; OriginalSHA256=$(if ($backup) { Hash $backup } else { $null }); InstalledSHA256=(Hash $temp); Restored=$false }
        $script:state.Entries += @($record)
        Save-State
        Move-Item -LiteralPath $temp -Destination $Path -Force
        if (!(Is-Owned $Path) -or (Hash $Path) -ne $record.InstalledSHA256) { throw '入口写入后校验失败。' }
        if ($exists) { $script:report.Replaced++ } else { $script:report.Created++ }
    } finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp } }
}
try {
    try { $locked = $mutex.WaitOne(30000) } catch [Threading.AbandonedMutexException] { $locked = $true }
    if (!$locked) { throw '另一个安装程序正在运行。' }
    if ((Test-Path -LiteralPath $Destination) -and !(Test-Path -LiteralPath (Join-Path $Destination '.codex-industrial-owner')) -and (Get-ChildItem -LiteralPath $Destination -Force | Where-Object { $_.Name -ne 'logs' })) { throw '安装目录已有其他文件，请选择空目录。' }
    New-Item -ItemType Directory -Path $Destination,$stateRoot,(Join-Path $stateRoot 'backups') -Force | Out-Null
    # Store a physical path: packaged desktop apps can virtualize LocalAppData.
    # Explorer must be able to reach the installed executable outside that context.
    $Destination = [CodexPhysicalDirectory]::Resolve($Destination).TrimEnd('\','/')
    $exePath = Join-Path $Destination 'Start-Codex-Industrial.exe'
    $stateRoot = Join-Path $Destination '.integration'
    $stateFile = Join-Path $stateRoot 'state.json'
    $reportFile = Join-Path $stateRoot 'report.json'
    Set-Content -LiteralPath (Join-Path $Destination '.codex-industrial-owner') -Value 'codex-industrial-native/1' -Encoding ASCII
    $script:state = if (Test-Path -LiteralPath $stateFile) { Get-Content -Raw -LiteralPath $stateFile -Encoding UTF8 | ConvertFrom-Json } else { [pscustomobject]@{ Schema=1; Owner='codex-industrial-native'; Entries=@() } }
    if ($state.Schema -ne 1 -or $state.Owner -ne 'codex-industrial-native') { throw '未知安装记录，未修改入口。' }
    $script:ws = New-Object -ComObject WScript.Shell
    $script:shell = New-Object -ComObject Shell.Application
    if ($Action -eq 'Restore') {
        foreach ($entry in @($state.Entries)) {
            if ($entry.Restored) { continue }
            try {
                if (!(Test-Path -LiteralPath $entry.Path) -or (Hash $entry.Path) -ne $entry.InstalledSHA256 -or !(Is-Owned $entry.Path)) {
                    if ($entry.OriginalExists -and (Test-Path -LiteralPath $entry.Path) -and (Hash $entry.Path) -eq $entry.OriginalSHA256) { $entry.Restored=$true; Save-State; continue }
                    $report.Preserved += @{ Path=$entry.Path; Reason='Shortcut was changed or removed; left as-is.' }; continue
                }
                if ($entry.OriginalExists) {
                    $backup = [IO.Path]::GetFullPath($entry.Backup)
                    $allowed = [IO.Path]::GetFullPath((Join-Path $stateRoot 'backups')) + '\'
                    if (!$backup.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or (Hash $backup) -ne $entry.OriginalSHA256) { throw '原始备份校验失败。' }
                    [IO.File]::WriteAllBytes($entry.Path,[IO.File]::ReadAllBytes($backup))
                    if ((Hash $entry.Path) -ne $entry.OriginalSHA256) { throw '恢复后的图标校验失败。' }
                } else { Remove-Item -LiteralPath $entry.Path }
                $entry.Restored=$true; $report.Restored++; Save-State
            } catch { $report.Errors += @{ Path=$entry.Path; Reason=$_.Exception.Message } }
        }
        $report.Status='Restored'
    } elseif ($Action -eq 'Verify') {
        foreach ($entry in @($state.Entries | Where-Object { !$_.Restored })) {
            if ((Test-Path -LiteralPath $entry.Path) -and (Hash $entry.Path) -eq $entry.InstalledSHA256 -and (Is-Owned $entry.Path)) { $report.AlreadyOwned++ }
            else { $report.Preserved += @{ Path=$entry.Path; Reason='Shortcut does not match the installed version.' } }
        }
        $report.Status='Verified'
    } else {
        [xml]$config = Get-Content -Raw -LiteralPath (Join-Path $sourceRoot 'launcher.config.xml') -Encoding UTF8
        $script:appId = [string]$config.launcher.appUserModelId
        $script:officialPaths = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        if ($config.launcher.executablePath) { $null = $officialPaths.Add([Environment]::ExpandEnvironmentVariables([string]$config.launcher.executablePath)) }
        $startApp = Get-StartApps | Where-Object { $_.AppID -ieq $appId } | Select-Object -First 1
        if (!$startApp -and !$config.launcher.executablePath) { throw '未找到 Codex 桌面版。请先安装桌面应用。' }
        foreach ($package in @(Get-AppxPackage | Where-Object { $_.PackageFamilyName -ieq $appId.Split('!')[0] })) {
            $manifest = Get-AppxPackageManifest -Package $package.PackageFullName
            foreach ($app in @($manifest.Package.Applications.Application | Where-Object { $_.Id -ieq $appId.Split('!')[1] })) {
                if ($app.Executable) { $null = $officialPaths.Add([IO.Path]::GetFullPath((Join-Path $package.InstallLocation $app.Executable))) }
            }
        }
        $payload = @('Start-Codex-Industrial.exe','Setup-Codex-Animation.exe','安装启动动画.exe','launcher.config.xml','media/startup.mp4','assets/codex.ico','scripts/setup.ps1','README.md','Preview-Animation.cmd','Restore-Codex.cmd','LICENSE','NOTICE.md','RIGHTS.md','CREDITS.md','THIRD_PARTY_NOTICES.md','licenses/noto-sans-sc-OFL.txt','安装说明.txt','RELEASE_NOTES.md','CHANGELOG.md','validation.json','docs/GITHUB_PUBLISH.md')
        foreach ($relative in $payload) { if (!(Test-Path -LiteralPath (Join-Path $sourceRoot $relative) -PathType Leaf)) { throw ('安装包缺少文件：' + $relative) } }
        foreach ($relative in $payload) {
            $from = Join-Path $sourceRoot $relative; $to = Join-Path $Destination $relative
            if (Equal ([IO.Path]::GetFullPath($from)) ([IO.Path]::GetFullPath($to))) { continue }
            New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($to)) -Force | Out-Null
            Copy-Item -LiteralPath $from -Destination $to -Force
            if ((Hash $from) -ne (Hash $to)) { throw ('安装文件校验失败：' + $relative) }
        }
        $script:legacy = $null
        $script:legacyRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CodexIndustrialLauncher'
        if (!$NoLegacyMigration -and (Test-Path -LiteralPath (Join-Path $legacyRoot 'installation.json'))) {
            $script:legacyRoot = [CodexPhysicalDirectory]::Resolve($legacyRoot)
            $candidate = Get-Content -Raw -LiteralPath (Join-Path $legacyRoot 'installation.json') -Encoding UTF8 | ConvertFrom-Json
            if ($candidate.owner -eq 'codex-launch-animation') { $script:legacy = $candidate }
        }
        if (!$SearchRoots) {
            $SearchRoots = @([Environment]::GetFolderPath('DesktopDirectory'),[Environment]::GetFolderPath('Programs'),(Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Microsoft/Internet Explorer/Quick Launch'))
        }
        $seen = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        $queue = New-Object 'Collections.Generic.Queue[string]'
        foreach ($folder in $SearchRoots) { if ($folder) { $queue.Enqueue([IO.Path]::GetFullPath($folder)) } }
        while ($queue.Count) {
            $folder = $queue.Dequeue()
            if (!$seen.Add($folder) -or !(Test-Path -LiteralPath $folder -PathType Container)) { continue }
            if (([IO.File]::GetAttributes($folder) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
            foreach ($path in [IO.Directory]::GetFiles($folder,'*.lnk')) {
                try { Install-Link $path } catch { $report.Errors += @{ Path=$path; Reason=$_.Exception.Message } }
            }
            foreach ($child in [IO.Directory]::GetDirectories($folder)) { $queue.Enqueue($child) }
        }
        if (!$NoDefaultLinks) {
            $desktop = [Environment]::GetFolderPath('DesktopDirectory')
            $hasDesktop = @($state.Entries | Where-Object { !$_.Restored -and (Equal ([IO.Path]::GetDirectoryName($_.Path)) $desktop) -and (Is-Owned $_.Path) }).Count -gt 0
            if (!$hasDesktop) { Install-Link (Join-Path $desktop 'Codex Industrial.lnk') $true }
            $programs = [Environment]::GetFolderPath('Programs')
            Install-Link (Join-Path $programs 'Codex Industrial.lnk') $true
        }
        Save-State; $report.Status='Installed'
    }
    if ($report.Errors.Count -gt 0) { throw '部分入口未能处理，请查看安装记录。' }
    if (!$Quiet) { Write-Output ($report | ConvertTo-Json -Depth 8) }
} catch { $report.Status='Failed'; $report.Errors += @{ Reason=$_.Exception.Message }; throw }
finally {
    if ($locked) {
        $report.Finished=(Get-Date).ToString('o')
        if (Test-Path -LiteralPath $stateRoot) { Save-Json $reportFile $report }
        $mutex.ReleaseMutex()
    }
    $mutex.Dispose()
}
