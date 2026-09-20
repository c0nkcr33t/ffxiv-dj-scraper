using Dalamud.Configuration;
using System;

namespace DjScraperPlugin;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public string ModDirectory { get; set; } = string.Empty;
    public string PlaylistName { get; set; } = "My Playlist";
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string YtDlpPath { get; set; } = "yt-dlp";

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
