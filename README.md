# DJ Scraper Dalamud Plugin (experimental)

This is a separate C# experiment; the Python application remains available as
the established standalone workflow.

The `/djscraper` window persists its source URL, mod directory, playlist name,
and external FFmpeg/yt-dlp paths. It can validate or install missing external
tools, download and convert a track, create a Vorbis SCD, and update the
selected mod's `meta.json` with a timestamped backup.

Generated SCDs are stored beneath the selected mod directory in
`scraper/<sanitized-track-title>.scd`. The matching metadata mapping uses that
same relative path.

## Installation for friends

Once a release is published, add this URL in Dalamud Settings > Experimental >
Custom Plugin Repositories, then install **DJ Scraper** through `/xlplugins`:

```text
https://raw.githubusercontent.com/c0nkcr33t/ffxiv-dj-scraper/main/pluginmaster.json
```

See [RELEASING.md](RELEASING.md) for the maintainer release process.

## Current limitations

- The workflow is experimental and should be tested in-game with a copy of a
  mod before relying on it for a production mod.
- Downloads and conversion cannot yet be cancelled from the plugin window.

## Testing in Dalamud

Build with `dotnet build`, then copy the **entire** `bin/Debug` folder to a
dedicated folder on the Windows machine, for example
`C:\\Dev\\DjScraperPlugin`. Do not copy only the DLL: Dalamud needs the
same-named manifest beside it.

```text
C:\Dev\DjScraperPlugin\
├── DjScraperPlugin.dll
├── DjScraperPlugin.json
└── DjScraperPlugin.deps.json
```

In Dalamud Settings > Experimental > Dev Plugin Locations, add either that
folder or the full path to `DjScraperPlugin.dll`. Do not point Dalamud at the
copied `Hooks\\dev` dependency folder; it would attempt to load every Dalamud
dependency as a plugin.

Start from the current [Dalamud SamplePlugin](https://github.com/goatcorp/SamplePlugin)
setup guide.
