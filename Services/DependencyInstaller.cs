using System.IO.Compression;
using System.Net.Http;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DjScraperPlugin.Services;

internal static class DependencyInstaller
{
    private const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    private const string FfmpegUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

    public static async Task<DependencyInstallResult> InstallAsync(
        string configurationDirectory,
        bool installFfmpeg,
        bool installYtDlp)
    {
        if (!installFfmpeg && !installYtDlp)
            return new DependencyInstallResult(true, "All dependencies are already available.", null, null);

        var toolsDirectory = Path.Combine(configurationDirectory, "tools");
        var temporaryDirectory = Path.Combine(toolsDirectory, $".install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            string? ytDlpPath = null;
            string? ffmpegPath = null;
            if (installYtDlp)
            {
                ytDlpPath = Path.Combine(toolsDirectory, "yt-dlp.exe");
                await DownloadFileAsync(YtDlpUrl, ytDlpPath);
            }
            if (installFfmpeg)
            {
                var archivePath = Path.Combine(temporaryDirectory, "ffmpeg.zip");
                await DownloadFileAsync(FfmpegUrl, archivePath);
                ZipFile.ExtractToDirectory(archivePath, temporaryDirectory);
                var extractedFfmpeg = Directory
                    .EnumerateFiles(temporaryDirectory, "ffmpeg.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (extractedFfmpeg is null)
                    throw new InvalidDataException("The FFmpeg archive did not contain ffmpeg.exe.");

                ffmpegPath = Path.Combine(toolsDirectory, "ffmpeg.exe");
                File.Move(extractedFfmpeg, ffmpegPath, overwrite: true);
            }
            return new DependencyInstallResult(
                true,
                $"Installed {DescribeInstalledTools(installFfmpeg, installYtDlp)} in the plugin tools directory.",
                ffmpegPath,
                ytDlpPath);
        }
        catch (Exception ex)
        {
            return new DependencyInstallResult(false, $"Dependency installation failed: {ex.Message}", null, null);
        }
        finally
        {
            try { Directory.Delete(temporaryDirectory, recursive: true); }
            catch { /* Temporary-file cleanup must not hide the install result. */ }
        }
    }

    private static string DescribeInstalledTools(bool ffmpeg, bool ytDlp)
        => (ffmpeg, ytDlp) switch
        {
            (true, true) => "yt-dlp and FFmpeg",
            (true, false) => "FFmpeg",
            (false, true) => "yt-dlp",
            _ => "no tools",
        };

    private static async Task DownloadFileAsync(string url, string destination)
    {
        var temporaryPath = $"{destination}.{Guid.NewGuid():N}.download";
        try
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync();
            await using (var output = File.Create(temporaryPath))
            {
                await input.CopyToAsync(output);
                await output.FlushAsync();
            }
            File.Move(temporaryPath, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}

internal sealed record DependencyInstallResult(
    bool Success,
    string Message,
    string? FfmpegPath,
    string? YtDlpPath);
