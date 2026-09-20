using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DjScraperPlugin.Services;

internal static class TrackWorkflowService
{
    // Current Soundy-compatible single-entry SCD container prefix.
    private static readonly byte[] ScdPrefix = Convert.FromBase64String(
        "U0VEQlNTQ0YDAAAAAAQwAMD5tAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQABAAEAQB9gAAAAcAAAAIAAAAAAAAAAkAAAAAAAAAAgAQAAAAAAAAAAAAAAAAAAUAEAAAAAAAAAAAAAAAAAAOABAAAAAAAAAAAAAAAAAACgAAAAAAAAAAAAAAAAAAAAYAEAAAAAAAAAAAAAAAAAAGAAAwAEAAAAAAAAAAAAAAAAAIA/AACAPwAAgD8AAIA/AAAAAAAAAAAAAAAAAACAPwAAAAAAAPBBAAAgQQAAAAAAAIA/AACAPwAAgD8AAIA/AAAAAAAAAAAAAIA/AAAAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQeAASEEAAAAAIA/AAAAABABAACwBAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABQAyAAAABwAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");

    public static async Task<WorkflowResult> RunAsync(
        string url, string modDirectory, string playlistName, string ffmpegPath, string ytDlpPath)
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"dj-scraper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var source = await DownloadAsync(ytDlpPath, url, temporaryDirectory);
            var title = await YoutubeTitleService.FetchAsync(ytDlpPath, url);
            var safeTitle = MetadataService.SanitizeTrackName(title);
            var ogg = Path.Combine(temporaryDirectory, "track.ogg");
            await ConvertToOggAsync(ffmpegPath, source, ogg);

            var scdPath = Path.Combine(modDirectory, "scraper", $"{safeTitle}.scd");
            Directory.CreateDirectory(Path.GetDirectoryName(scdPath)!);
            WriteScd(ogg, scdPath);
            var metadata = MetadataService.Update(modDirectory, playlistName, safeTitle);
            return new WorkflowResult(safeTitle, scdPath, metadata.BackupPath, metadata.UpdatedExisting);
        }
        finally
        {
            try { Directory.Delete(temporaryDirectory, recursive: true); }
            catch { }
        }
    }

    private static async Task<string> DownloadAsync(string ytDlpPath, string url, string directory)
    {
        var outputTemplate = Path.Combine(directory, "source.%(ext)s");
        var output = await RunProcessAsync(ytDlpPath, ["-f", "bestaudio", "--no-playlist", "-o", outputTemplate, "--print", "after_move:filepath", url]);
        var path = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidOperationException("yt-dlp did not report a downloaded audio file.");
        return path;
    }

    private static async Task ConvertToOggAsync(string ffmpegPath, string source, string output)
    {
        await RunProcessAsync(ffmpegPath,
            ["-y", "-fflags", "+genpts", "-avoid_negative_ts", "make_zero", "-i", source, "-vn", "-map_metadata", "-1",
             "-af", "asetpts=PTS-STARTPTS,aresample=resampler=soxr:precision=33,loudnorm=I=-16:LRA=11:TP=-1.0,alimiter=limit=0.97",
             "-ar", "44100", "-ac", "2", "-c:a", "libvorbis", "-q:a", "10", "-compression_level", "10", output]);
    }

    private static async Task<string> RunProcessAsync(string command, IEnumerable<string> arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo { FileName = command, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(command)} exited with code {process.ExitCode}: {FirstLine(error)}");
        return output;
    }

    private static void WriteScd(string oggPath, string destination)
    {
        var ogg = File.ReadAllBytes(oggPath);
        var (channels, sampleRate) = GetVorbisInfo(ogg);
        var (seekStep, seekTable) = BuildSeekTable(ogg, sampleRate);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(ScdPrefix);
        writer.Write(ogg.Length);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(6); // Vorbis
        writer.Write(0); // loop start
        writer.Write(ogg.Length); // loop end
        writer.Write(0x20 + seekTable.Count * 4);
        writer.Write(1); // Soundy template entry flags
        writer.Write((short)0); writer.Write((short)0); writer.Write(0); writer.Write(0);
        writer.Write(seekStep); writer.Write(seekTable.Count * 4); writer.Write(0); writer.Write(0); writer.Write(0);
        foreach (var offset in seekTable) writer.Write(offset);
        writer.Write(ogg);
        while (stream.Length % 16 != 0) writer.Write((byte)0);
        var data = stream.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x10, 4), data.Length);
        File.WriteAllBytes(destination, data);
    }

    private static (int Channels, int SampleRate) GetVorbisInfo(byte[] ogg)
    {
        var marker = Find(ogg, [1, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s']);
        if (marker < 0 || marker + 16 > ogg.Length) throw new InvalidDataException("Ogg file has no Vorbis identification packet.");
        return (ogg[marker + 11], BinaryPrimitives.ReadInt32LittleEndian(ogg.AsSpan(marker + 12, 4)));
    }

    private static (float SeekStep, List<int> Table) BuildSeekTable(byte[] ogg, int sampleRate)
    {
        var table = new List<int>(); var step = 0.1f; var offset = 0;
        var pattern = new byte[] { (byte)'O', (byte)'g', (byte)'g', (byte)'S', 0 };
        while ((offset = Find(ogg, pattern, offset)) >= 0)
        {
            if (offset + 14 > ogg.Length) throw new InvalidDataException("Ogg page is truncated.");
            var samples = BinaryPrimitives.ReadUInt64LittleEndian(ogg.AsSpan(offset + 6, 8));
            var elapsed = samples / (float)sampleRate;
            if (table.Count == 1 && elapsed > step) step = elapsed;
            while ((step * table.Count) - elapsed < 0.02f) table.Add(offset);
            offset += pattern.Length;
        }
        if (table.Count == 0) throw new InvalidDataException("Could not build an Ogg seek table.");
        return (step, table);
    }

    private static int Find(byte[] data, byte[] pattern, int start = 0)
    {
        for (var i = start; i <= data.Length - pattern.Length; i++)
        {
            var match = true;
            for (var j = 0; j < pattern.Length; j++) if (data[i + j] != pattern[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }

    private static string FirstLine(string value) => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "no error details";
}

internal sealed record WorkflowResult(string Title, string ScdPath, string BackupPath, bool UpdatedExisting);
