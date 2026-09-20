using System.Diagnostics;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DjScraperPlugin.Services;

internal static class YoutubeTitleService
{
    public static async Task<string> FetchAsync(string ytDlpPath, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Enter a source URL first.");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("--no-playlist");
        process.StartInfo.ArgumentList.Add("--skip-download");
        process.StartInfo.ArgumentList.Add("--print");
        process.StartInfo.ArgumentList.Add("%(title)s");
        process.StartInfo.ArgumentList.Add(url);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"yt-dlp exited with code {process.ExitCode}: {FirstLine(error)}");

        var title = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        return string.IsNullOrWhiteSpace(title)
            ? throw new InvalidOperationException("yt-dlp did not return a video title.")
            : title;
    }

    private static string FirstLine(string text)
        => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "no error details";
}
