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

    internal static async Task DownloadAndInstallAsync(UpdateRelease release, IProgress<string>? progress = null)
    {
        var temp = Path.Combine(Path.GetTempPath(), "SnapLine-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var archive = Path.Combine(temp, "package.zip");
            await using (var output = File.Create(archive))
            await using (var input = await Client.GetStreamAsync(release.PackageUrl))
                await input.CopyToAsync(output);
            progress?.Report(Words.T("正在校验更新…", "Verifying the update…"));
            var checksum = release.Digest ?? (await Client.GetStringAsync(release.ChecksumUrl!)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            await using var file = File.OpenRead(archive);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(file));
            if (checksum == null || !actual.Equals(checksum, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(Words.T("安装包校验失败。", "Package checksum verification failed."));
            await file.DisposeAsync();
            var staged = Path.Combine(temp, "package");
            progress?.Report(Words.T("正在解压更新…", "Extracting the update…"));
            await Task.Run(() => ZipFile.ExtractToDirectory(archive, staged));
            if (!File.Exists(Path.Combine(staged, "SnapLine.exe")) || Directory.Exists(Path.Combine(staged, "Data"))) throw new InvalidDataException(Words.T("安装包内容无效。", "Invalid package contents."));
            var script = Path.Combine(temp, "install.ps1");
            await File.WriteAllTextAsync(script, InstallerScript, new UTF8Encoding(true));
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-Source", staged, "-Target", AppContext.BaseDirectory, "-ExecutableName", Path.GetFileName(Environment.ProcessPath ?? throw new InvalidOperationException(Words.T("无法确定程序路径。", "Could not determine the executable path."))), "-ParentId", Environment.ProcessId.ToString(), "-Temp", temp }) start.ArgumentList.Add(arg);
            _ = Process.Start(start) ?? throw new InvalidOperationException(Words.T("无法启动更新程序。", "Could not start the updater."));
        }
        catch { try { Directory.Delete(temp, true); } catch { } throw; }
    }

    private const string InstallerScript = """
param([string]$Source, [string]$Target, [string]$ExecutableName, [int]$ParentId, [string]$Temp)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework
$backup = Join-Path $Temp 'backup'
$copied = New-Object System.Collections.Generic.List[string]
$cleanup = $false
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
    Start-Process -FilePath (Join-Path $Target $ExecutableName) -WorkingDirectory $Target -ErrorAction Stop
    $cleanup = $true
} catch {
    $reason = $_.Exception.Message
    $rollbackFailed = $false
    foreach ($relative in $copied) {
        try {
            $destination = Join-Path $Target $relative
            $saved = Join-Path $backup $relative
            if (Test-Path -LiteralPath $saved) { Copy-Item -LiteralPath $saved -Destination $destination -Force }
            elseif (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Force }
        } catch { $rollbackFailed = $true }
    }
    if ($rollbackFailed) {
        [System.Windows.MessageBox]::Show("更新失败，旧文件未能完全恢复。备份保存在：`n$backup`n`n$reason", 'SnapLine') | Out-Null
    } else {
        [System.Windows.MessageBox]::Show("更新失败，已恢复原文件：`n$reason", 'SnapLine') | Out-Null
        try {
            if (Test-Path -LiteralPath (Join-Path $Target $ExecutableName)) { Start-Process -FilePath (Join-Path $Target $ExecutableName) -WorkingDirectory $Target -ErrorAction Stop }
            $cleanup = $true
        } catch {
            [System.Windows.MessageBox]::Show("无法启动原程序，备份保存在：`n$backup`n`n$($_.Exception.Message)", 'SnapLine') | Out-Null
        }
    }
} finally { if ($cleanup) { Remove-Item -LiteralPath $Temp -Recurse -Force -ErrorAction SilentlyContinue } }
""";
}
