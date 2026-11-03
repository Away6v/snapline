using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SnapLine;

internal sealed record UpdateRelease(string Version, string Notes, string PackageUrl, string? Digest, string? ChecksumUrl);

internal sealed record UpdateProgress(string Text, bool Downloading = false, double? Percent = null);

internal static class UpdateService
{
    private static readonly HttpClient Client = CreateClient();
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SnapLine-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    internal static async Task<UpdateRelease?> CheckAsync()
    {
        using var response = await Client.GetAsync("https://api.github.com/repos/Away6v/snapline/releases/latest");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) throw new InvalidOperationException(Words.T("尚未找到正式发布版本。", "No stable release was found."));
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) throw new InvalidDataException(Words.T("发布版本号无效。", "Invalid release version."));
        var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
        if (version <= new Version(current.Major, current.Minor, Math.Max(0, current.Build))) return null;
        var runtime = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.Arm64 => "win-arm64",
            _ => throw new NotSupportedException(Words.T("当前处理器架构不受支持。", "This processor architecture is not supported."))
        };
        var name = $"SnapLine-{version}-{runtime}.zip";
        string? package = null, digest = null, checksum = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var assetName = asset.GetProperty("name").GetString();
            if (assetName == name)
            {
                package = asset.GetProperty("browser_download_url").GetString();
                if (asset.TryGetProperty("digest", out var value) && value.ValueKind == JsonValueKind.String)
                {
                    var raw = value.GetString();
                    if (raw != null && raw.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) digest = raw.Substring(7);
                }
            }
            if (assetName == name + ".sha256") checksum = asset.GetProperty("browser_download_url").GetString();
        }
        if (package == null || (digest == null && checksum == null)) throw new InvalidDataException(Words.T("此版本缺少适用于当前设备的安装包或校验信息。", "The package or checksum for this device is missing."));
        return new UpdateRelease(tag, root.GetProperty("body").GetString() ?? "", package, digest, checksum);
    }

    internal static async Task DownloadAndInstallAsync(UpdateRelease release, IProgress<UpdateProgress>? progress = null)
    {
        var temp = Path.Combine(Path.GetTempPath(), "SnapLine-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var archive = Path.Combine(temp, "package.zip");
            using var response = await Client.GetAsync(release.PackageUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var downloadSize = response.Content.Headers.ContentLength;
            if (downloadSize is > 0)
                EnsureFreeSpace(temp, checked(downloadSize.Value + 4L * 1024 * 1024));
            var downloadText = Words.T("正在下载更新…", "Downloading the update…");
            progress?.Report(new UpdateProgress(downloadText, true, downloadSize is > 0 ? 0 : null));
            await using (var output = File.Create(archive))
            await using (var input = await response.Content.ReadAsStreamAsync())
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                var timer = Stopwatch.StartNew();
                int count;
                while ((count = await input.ReadAsync(buffer)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, count));
                    downloaded += count;
                    if (timer.ElapsedMilliseconds < 100) continue;
                    progress?.Report(new UpdateProgress(downloadText, true, downloadSize is > 0 ? Math.Min(100, downloaded * 100.0 / downloadSize.Value) : null));
                    timer.Restart();
                }
                if (downloadSize is > 0 && downloaded != downloadSize.Value) throw new IOException(Words.T("更新下载不完整。", "The update download is incomplete."));
            }
            progress?.Report(new UpdateProgress(Words.T("正在校验更新…", "Verifying the update…")));
            var checksum = release.Digest ?? (await Client.GetStringAsync(release.ChecksumUrl!)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            await using var file = File.OpenRead(archive);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(file));
            if (checksum == null || !actual.Equals(checksum, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(Words.T("安装包校验失败。", "Package checksum verification failed."));
            await file.DisposeAsync();
            var staged = Path.Combine(temp, "package");
            var target = AppContext.BaseDirectory;
            CheckInstallSpace(archive, temp, target, Path.GetFileName(Environment.ProcessPath ?? throw new InvalidOperationException(Words.T("无法确定程序路径。", "Could not determine the executable path."))));
            progress?.Report(new UpdateProgress(Words.T("正在解压更新…", "Extracting the update…")));
            await Task.Run(() => ZipFile.ExtractToDirectory(archive, staged));
            if (!File.Exists(Path.Combine(staged, "SnapLine.exe")) || Directory.Exists(Path.Combine(staged, "Data"))) throw new InvalidDataException(Words.T("安装包内容无效。", "Invalid package contents."));
            var probe = Path.Combine(target, ".snapline-update-probe");
            try { await File.WriteAllTextAsync(probe, "ok"); File.Delete(probe); }
            catch (Exception ex) { throw new IOException(Words.T("程序目录不可写，无法安装更新。", "The app folder is not writable, so the update cannot be installed."), ex); }
            var acknowledgement = Path.Combine(temp, "healthy.txt");
            var token = Guid.NewGuid().ToString("N");
            var script = Path.Combine(temp, "install.ps1");
            await File.WriteAllTextAsync(script, InstallerScript, new UTF8Encoding(true));
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-Source", staged, "-Target", target, "-ExecutableName", Path.GetFileName(Environment.ProcessPath ?? throw new InvalidOperationException(Words.T("无法确定程序路径。", "Could not determine the executable path."))), "-ParentId", Environment.ProcessId.ToString(), "-Temp", temp, "-Acknowledgement", acknowledgement, "-Token", token }) start.ArgumentList.Add(arg);
            _ = Process.Start(start) ?? throw new InvalidOperationException(Words.T("无法启动更新程序。", "Could not start the updater."));
        }
        catch { try { Directory.Delete(temp, true); } catch { } throw; }
    }

    private static void EnsureFreeSpace(string path, long required)
    {
        if (new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace < required)
            throw new IOException(Words.T("磁盘空间不足，无法安全安装更新。", "There is not enough disk space to safely install the update."));
    }

    private static void CheckInstallSpace(string archive, string temp, string target, string executableName)
    {
        if (!Directory.Exists(target)) throw new DirectoryNotFoundException(target);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)) + Path.DirectorySeparatorChar;
        long extracted = 0, backup = 0;
        using (var package = ZipFile.OpenRead(archive))
        {
            foreach (var entry in package.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                if (relative.Equals("SnapLine.exe", StringComparison.OrdinalIgnoreCase)) relative = executableName;
                var destination = Path.GetFullPath(Path.Combine(root, relative));
                if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(Words.T("安装包包含无效路径。", "The package contains an invalid path."));
                extracted = checked(extracted + entry.Length);
                if (File.Exists(destination)) backup = checked(backup + new FileInfo(destination).Length);
            }
        }
        const long reserve = 4L * 1024 * 1024;
        var temporaryRequired = checked(extracted + backup + reserve);
        var targetRequired = checked(extracted + reserve);
        if (Path.GetPathRoot(Path.GetFullPath(temp))!.Equals(Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase))
            EnsureFreeSpace(temp, checked(temporaryRequired + targetRequired));
        else { EnsureFreeSpace(temp, temporaryRequired); EnsureFreeSpace(target, targetRequired); }
    }

    private const string InstallerScript = """
param([string]$Source, [string]$Target, [string]$ExecutableName, [int]$ParentId, [string]$Temp, [string]$Acknowledgement, [string]$Token)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework
$backup = Join-Path $Temp 'backup'
$copied = New-Object System.Collections.Generic.List[string]
$started = $null
$healthy = $false
function Restore-Previous {
    $rollbackFailed = $false
    foreach ($relative in $copied) {
        try {
            $destination = Join-Path $Target $relative
            $saved = Join-Path $backup $relative
            if (Test-Path -LiteralPath $saved) { Copy-Item -LiteralPath $saved -Destination $destination -Force }
            elseif (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Force }
        } catch { $rollbackFailed = $true }
    }
    return $rollbackFailed
}
try {
    Wait-Process -Id $ParentId -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $backup | Out-Null
    Get-ChildItem -LiteralPath $Source -File -Recurse | ForEach-Object {
        $relative = $_.FullName.Substring($Source.Length).TrimStart('\')
        if ($relative -eq 'SnapLine.exe') { $relative = $ExecutableName }
        $destination = Join-Path $Target $relative
        $saved = Join-Path $backup $relative
        if (Test-Path -LiteralPath $destination) {
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $saved) | Out-Null
            Copy-Item -LiteralPath $destination -Destination $saved -Force
        }
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        $copied.Add($relative)
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
    }
    $launch = New-Object System.Diagnostics.ProcessStartInfo
    $launch.FileName = Join-Path $Target $ExecutableName
    $launch.WorkingDirectory = $Target
    $launch.UseShellExecute = $false
    $launch.Environment['SNAPLINE_UPDATE_ACK'] = $Acknowledgement
    $launch.Environment['SNAPLINE_UPDATE_TOKEN'] = $Token
    $started = [System.Diagnostics.Process]::Start($launch)
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    $maximum = [DateTime]::UtcNow.AddMinutes(10)
    while ([DateTime]::UtcNow -lt $deadline -and [DateTime]::UtcNow -lt $maximum) {
        if (Test-Path -LiteralPath $Acknowledgement) {
            if ((Get-Content -LiteralPath $Acknowledgement -Raw).Trim() -eq $Token) { $healthy = $true; break }
        }
        $progress = Get-Item -LiteralPath "$Acknowledgement.progress" -ErrorAction SilentlyContinue
        if ($progress -and $progress.LastWriteTimeUtc -gt [DateTime]::UtcNow.AddSeconds(-15)) { $deadline = [DateTime]::UtcNow.AddSeconds(60) }
        if ($started.HasExited) { break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $healthy) { throw '新版未能完成初始化确认。' }
    Remove-Item -LiteralPath $Temp -Recurse -Force -ErrorAction SilentlyContinue
} catch {
    $reason = $_.Exception.Message
    if ($started -and -not $started.HasExited) { try { $started.Kill(); $started.WaitForExit() } catch { } }
    $rollbackFailed = Restore-Previous
    if ($rollbackFailed) {
        [System.Windows.MessageBox]::Show("更新失败，旧文件未能完全恢复。备份保存在：`n$backup`n`n$reason", 'SnapLine') | Out-Null
    } else {
        [System.Windows.MessageBox]::Show("更新失败，已恢复原文件：`n$reason", 'SnapLine') | Out-Null
        try { Start-Process -FilePath (Join-Path $Target $ExecutableName) -WorkingDirectory $Target -ErrorAction Stop }
        catch { [System.Windows.MessageBox]::Show("无法启动原程序，备份保存在：`n$backup`n`n$($_.Exception.Message)", 'SnapLine') | Out-Null }
    }
}
""";
}
