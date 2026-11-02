using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SnapLine;

internal static class BackupService
{
    internal static string CreateSnapshot(string destination)
    {
        destination = Path.GetFullPath(destination);
        if (destination.StartsWith(Path.TrimEndingDirectorySeparator(Storage.Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException(Words.T("备份文件不能保存在 Data 文件夹中。", "The backup file cannot be saved inside Data."));
        var snapshot = Path.Combine(Path.GetTempPath(), "SnapLine-snapshot-" + Guid.NewGuid().ToString("N"));
        try
        {
            bool IsTransient(string file)
            {
                if (!Path.GetDirectoryName(file)!.Equals(Path.TrimEndingDirectorySeparator(Storage.Root), StringComparison.OrdinalIgnoreCase)) return false;
                var name = Path.GetFileName(file);
                return name.Equals("state.json.tmp", StringComparison.OrdinalIgnoreCase)
                    || (name.StartsWith("recovery-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json.tmp", StringComparison.OrdinalIgnoreCase));
            }
            var files = Directory.EnumerateFiles(Storage.Root, "*", SearchOption.AllDirectories).Where(file => !IsTransient(file)).ToArray();
            long size = 0;
            foreach (var file in files) size = checked(size + new FileInfo(file).Length);
            var snapshotDrive = new DriveInfo(Path.GetPathRoot(snapshot)!);
            var destinationDrive = new DriveInfo(Path.GetPathRoot(destination)!);
            var required = checked(size + 4L * 1024 * 1024);
            if (snapshotDrive.Name.Equals(destinationDrive.Name, StringComparison.OrdinalIgnoreCase))
            {
                if (snapshotDrive.AvailableFreeSpace < checked(required * 2)) throw new IOException(Words.T("磁盘空间不足，无法创建备份快照。", "Not enough disk space to create the backup snapshot."));
            }
            else if (snapshotDrive.AvailableFreeSpace < required || destinationDrive.AvailableFreeSpace < required)
                throw new IOException(Words.T("磁盘空间不足，无法创建备份快照。", "Not enough disk space to create the backup snapshot."));
            Directory.CreateDirectory(snapshot);
            foreach (var directory in Directory.EnumerateDirectories(Storage.Root, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(snapshot, Path.GetRelativePath(Storage.Root, directory)));
            bool IsMetadata(string file)
            {
                if (!Path.GetDirectoryName(file)!.Equals(Path.TrimEndingDirectorySeparator(Storage.Root), StringComparison.OrdinalIgnoreCase)) return false;
                var name = Path.GetFileName(file);
                return name.Equals("state.json", StringComparison.OrdinalIgnoreCase) || name.Equals("state.json.bak", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("state.json.recovered", StringComparison.OrdinalIgnoreCase)
                    || (name.StartsWith("recovery-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
            }
            Storage.WithSaveLock(() =>
            {
                foreach (var file in Directory.EnumerateFiles(Storage.Root).Where(IsMetadata))
                    File.Copy(file, Path.Combine(snapshot, Path.GetRelativePath(Storage.Root, file)));
            });
            foreach (var file in files.Where(file => !IsMetadata(file)))
            {
                File.Copy(file, Path.Combine(snapshot, Path.GetRelativePath(Storage.Root, file)));
            }
            var statePath = Path.Combine(snapshot, "state.json");
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(statePath))
                ?? throw new InvalidDataException(Words.T("备份状态文件无效。", "The backup state is invalid."));
            var external = (state.Captures ?? new()).Where(capture => capture != null && !string.IsNullOrWhiteSpace(capture.Path)
                && Path.IsPathFullyQualified(capture.Path) && !Storage.Owns(capture.Path))
                .Select(capture => capture.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            long externalSize = 0;
            foreach (var path in external)
            {
                if (!File.Exists(path)) throw new FileNotFoundException(Words.T("无法备份缺失的截图：", "Cannot back up missing image: ") + path);
                externalSize = checked(externalSize + new FileInfo(path).Length);
            }
            var archiveSpace = checked(size + externalSize + 4L * 1024 * 1024);
            if (snapshotDrive.Name.Equals(destinationDrive.Name, StringComparison.OrdinalIgnoreCase))
            {
                if (snapshotDrive.AvailableFreeSpace < checked(externalSize + archiveSpace)) throw new IOException(Words.T("磁盘空间不足，无法备份外部截图。", "Not enough disk space to back up external images."));
            }
            else if (snapshotDrive.AvailableFreeSpace < externalSize || destinationDrive.AvailableFreeSpace < archiveSpace)
                throw new IOException(Words.T("磁盘空间不足，无法备份外部截图。", "Not enough disk space to back up external images."));
            var copied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var inbox = Path.Combine(snapshot, "Inbox");
            Directory.CreateDirectory(inbox);
            foreach (var path in external)
            {
                var name = "Backup-" + Guid.NewGuid().ToString("N") + Path.GetExtension(path);
                File.Copy(path, Path.Combine(inbox, name));
                copied.Add(path, Path.Combine("Inbox", name));
            }
            foreach (var capture in state.Captures ?? new())
                if (capture != null && capture.Path != null && copied.TryGetValue(capture.Path, out var localPath)) capture.Path = localPath;
            File.WriteAllText(statePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            // 备用状态与快照保持一致，回退时也能找到已打包的截图。
            File.Copy(statePath, statePath + ".bak", true);
            return snapshot;
        }
        catch { try { Directory.Delete(snapshot, true); } catch { } throw; }
    }
    internal static void Export(string destination, string snapshot)
    {
        destination = Path.GetFullPath(destination);
        if (destination.StartsWith(Path.TrimEndingDirectorySeparator(Storage.Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException(Words.T("备份文件不能保存在 Data 文件夹中。", "The backup file cannot be saved inside Data."));
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            ZipFile.CreateFromDirectory(snapshot, temporary, CompressionLevel.Optimal, false);
            Validate(temporary);
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static void RestoreAndRestart(string source)
    {
        source = Path.GetFullPath(source);
        Validate(source);
        var temporary = Path.Combine(Path.GetTempPath(), "SnapLine-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var archive = Path.Combine(temporary, "backup.zip");
        File.Copy(source, archive, true);
        var script = Path.Combine(temporary, "restore.ps1");
        File.WriteAllText(script, RestoreScript, new UTF8Encoding(true));
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-Source", archive, "-Data", Storage.Root, "-Executable", Environment.ProcessPath ?? throw new InvalidOperationException(Words.T("无法确定程序路径。", "Could not determine the executable path.")), "-WorkingDirectory", AppContext.BaseDirectory, "-ParentId", Environment.ProcessId.ToString(), "-Temp", temporary }) start.ArgumentList.Add(argument);
        _ = Process.Start(start) ?? throw new InvalidOperationException(Words.T("无法启动恢复程序。", "Could not start the restore helper."));
    }

    internal static int CountMissingExternalImages(string source)
    {
        var state = Validate(source);
        return (state.Captures ?? Enumerable.Empty<Capture>())
            .Where(capture => capture != null && !string.IsNullOrWhiteSpace(capture.Path)
                && Path.IsPathFullyQualified(capture.Path) && !Storage.Owns(capture.Path) && !File.Exists(capture.Path))
            .Select(capture => capture.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    }

    private static State Validate(string source)
    {
        using var archive = ZipFile.OpenRead(source);
        var stateEntries = archive.Entries.Where(entry => entry.FullName.Equals("state.json", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (stateEntries.Length != 1)
            throw new InvalidDataException(Words.T("备份中没有状态文件。", "The backup does not contain a state file."));
        State? state;
        using (var stream = stateEntries[0].Open())
        {
            state = JsonSerializer.Deserialize<State>(stream);
            if (state == null || state.SchemaVersion > 1)
                throw new InvalidDataException(Words.T("备份状态文件无效或版本不受支持。", "The backup state is invalid or its version is unsupported."));
        }
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FullName) || Path.IsPathFullyQualified(entry.FullName) || entry.FullName.Contains(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || entry.FullName.Contains("../", StringComparison.Ordinal))
                throw new InvalidDataException(Words.T("备份包含无效路径。", "The backup contains an invalid path."));
        }
        var names = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var capture in state.Captures ?? Enumerable.Empty<Capture>())
        {
            if (capture == null || string.IsNullOrWhiteSpace(capture.Path)) continue;
            if (Path.IsPathFullyQualified(capture.Path) && !Storage.Owns(capture.Path)) continue;
            var name = "Inbox/" + Path.GetFileName(capture.Path);
            if (!names.Contains(name))
                throw new InvalidDataException(Words.T("备份缺少截图文件：", "Backup is missing an image: ") + name);
        }
        return state;
    }

    private const string RestoreScript = """
param([string]$Source, [string]$Data, [string]$Executable, [string]$WorkingDirectory, [int]$ParentId, [string]$Temp)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework
$previous = "$Data.restore-backup-$([guid]::NewGuid().ToString('N'))"
$staged = "$Data.restore-new-$([guid]::NewGuid().ToString('N'))"
$movedPrevious = $false
$installed = $false
$started = $null
$acknowledgement = Join-Path $Temp 'healthy.txt'
$token = [guid]::NewGuid().ToString('N')
try {
    Wait-Process -Id $ParentId -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $staged | Out-Null
    Expand-Archive -LiteralPath $Source -DestinationPath $staged -Force
    if (-not (Test-Path -LiteralPath (Join-Path $staged 'state.json'))) { throw '备份没有状态文件。' }
    if (Test-Path -LiteralPath $Data) { Move-Item -LiteralPath $Data -Destination $previous; $movedPrevious = $true }
    Move-Item -LiteralPath $staged -Destination $Data
    $installed = $true
    $launch = New-Object System.Diagnostics.ProcessStartInfo
    $launch.FileName = $Executable
    $launch.WorkingDirectory = $WorkingDirectory
    $launch.UseShellExecute = $false
    $launch.Environment['SNAPLINE_UPDATE_ACK'] = $acknowledgement
    $launch.Environment['SNAPLINE_UPDATE_TOKEN'] = $token
    $started = [System.Diagnostics.Process]::Start($launch)
    $healthy = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    $maximum = [DateTime]::UtcNow.AddMinutes(10)
    while ([DateTime]::UtcNow -lt $deadline -and [DateTime]::UtcNow -lt $maximum) {
        if ((Test-Path -LiteralPath $acknowledgement) -and (Get-Content -LiteralPath $acknowledgement -Raw).Trim() -eq $token) { $healthy = $true; break }
        $progress = Get-Item -LiteralPath "$acknowledgement.progress" -ErrorAction SilentlyContinue
        if ($progress -and $progress.LastWriteTimeUtc -gt [DateTime]::UtcNow.AddSeconds(-15)) { $deadline = [DateTime]::UtcNow.AddSeconds(60) }
        if ($started.HasExited) { break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $healthy) { throw '恢复后的程序未完成初始化确认。' }
    Remove-Item -LiteralPath $previous -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $Temp -Recurse -Force -ErrorAction SilentlyContinue
} catch {
    $reason = $_.Exception.Message
    if ($started -and -not $started.HasExited) { try { $started.Kill(); $started.WaitForExit() } catch { } }
    try {
        if ($installed -and (Test-Path -LiteralPath $Data)) { Remove-Item -LiteralPath $Data -Recurse -Force }
        if ($movedPrevious) { Move-Item -LiteralPath $previous -Destination $Data }
        Start-Process -FilePath $Executable -WorkingDirectory $WorkingDirectory -ErrorAction Stop
    } catch { $reason += "`n原数据备份可能保存在：$previous`n$($_.Exception.Message)" }
    if (Test-Path -LiteralPath $staged) { Remove-Item -LiteralPath $staged -Recurse -Force -ErrorAction SilentlyContinue }
    [System.Windows.MessageBox]::Show("恢复失败：`n$reason", 'SnapLine') | Out-Null
}
""";
}
