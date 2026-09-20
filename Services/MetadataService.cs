using System.Text.Json;
using System.Text.Json.Nodes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DjScraperPlugin.Services;

internal static class MetadataService
{
    public static string SanitizeTrackName(string title)
    {
        var invalid = new HashSet<char>("<>:\"/\\|?*".Concat(Path.GetInvalidFileNameChars()));
        var sanitized = new string(title
            .Trim()
            .Select(character => char.IsControl(character) || invalid.Contains(character) ? '_' : character)
            .ToArray())
            .Trim(' ', '.');

        if (string.IsNullOrWhiteSpace(sanitized))
            throw new ArgumentException("Track title contains no usable filename characters.");
        if (sanitized.Length > 120)
            sanitized = sanitized[..120].TrimEnd(' ', '.');
        if (IsReservedWindowsName(sanitized))
            sanitized = $"_{sanitized}";
        return sanitized;
    }

    public static MetadataUpdateResult Update(string modDirectory, string playlistName, string rawTitle)
    {
        var title = SanitizeTrackName(rawTitle);
        var metadataPath = Path.Combine(modDirectory, "meta.json");
        if (!File.Exists(metadataPath))
            throw new FileNotFoundException("meta.json was not found in the mod directory.", metadataPath);

        var root = JsonNode.Parse(File.ReadAllText(metadataPath))?.AsObject()
            ?? throw new InvalidDataException("meta.json does not contain a JSON object.");
        var groups = root["Groups"]?.AsArray()
            ?? throw new InvalidDataException("meta.json does not contain a Groups array.");
        var group = FindPlaylist(groups, playlistName)
            ?? throw new KeyNotFoundException($"Could not find playlist group named '{playlistName}'.");
        var options = group["Options"]?.AsArray()
            ?? throw new InvalidDataException("The selected playlist group does not contain an Options array.");

        var relativePath = $"scraper\\{title}.scd";
        var entry = options
            .OfType<JsonObject>()
            .FirstOrDefault(option => string.Equals(option["Name"]?.GetValue<string>(), title, StringComparison.OrdinalIgnoreCase)
                || string.Equals(option["Files"]?["sound/dam.scd"]?.GetValue<string>(), relativePath, StringComparison.OrdinalIgnoreCase));
        var updated = entry is not null;
        entry ??= new JsonObject { ["Id"] = Guid.NewGuid().ToString() };
        entry["Name"] = title;
        entry["Files"] = new JsonObject { ["sound/dam.scd"] = relativePath };
        if (!updated)
            options.Add(entry);

        var backupPath = Path.Combine(
            modDirectory,
            $"meta.json.backup-{DateTime.Now:yyyyMMdd-HHmmssfff}");
        File.Copy(metadataPath, backupPath, overwrite: false);

        var temporaryPath = Path.Combine(modDirectory, $".meta-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(temporaryPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, metadataPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }

        return new MetadataUpdateResult(title, relativePath, backupPath, updated);
    }

    private static JsonObject? FindPlaylist(JsonArray groups, string playlistName)
        => groups.OfType<JsonObject>().FirstOrDefault(group => string.Equals(
               group["Name"]?.GetValue<string>(), playlistName, StringComparison.OrdinalIgnoreCase))
           ?? groups.OfType<JsonObject>().FirstOrDefault(group => group["Name"]?.GetValue<string>()?.Contains(
               playlistName, StringComparison.OrdinalIgnoreCase) == true);

    private static bool IsReservedWindowsName(string value)
    {
        var baseName = value.Split('.')[0];
        return new[] { "CON", "PRN", "AUX", "NUL" }.Contains(baseName, StringComparer.OrdinalIgnoreCase)
            || (baseName.Length == 4 && baseName[..3].Equals("COM", StringComparison.OrdinalIgnoreCase) && baseName[3] is >= '1' and <= '9')
            || (baseName.Length == 4 && baseName[..3].Equals("LPT", StringComparison.OrdinalIgnoreCase) && baseName[3] is >= '1' and <= '9');
    }
}

internal sealed record MetadataUpdateResult(string Title, string RelativePath, string BackupPath, bool UpdatedExisting);
