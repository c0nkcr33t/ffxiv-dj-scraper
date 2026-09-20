# Releasing DJ Scraper

The custom Dalamud repository URL is:

```text
https://raw.githubusercontent.com/c0nkcr33t/ffxiv-dj-scraper/main/pluginmaster.json
```

## First release

1. Commit and push this project to the `main` branch of
   `c0nkcr33t/ffxiv-dj-scraper`.
2. Build a release version. From the development machine, run the same build
   command used for testing but add `--configuration Release`.
3. The SDK creates the installable archive at
   `bin/Release/DjScraperPlugin/latest.zip`.
4. On GitHub, open **Releases** > **Draft a new release**, create the tag
   `v0.1.0`, give it a title such as `DJ Scraper v0.1.0`, and upload that
   `latest.zip` file. Publish the release.
5. Add the custom repository URL above in Dalamud's **Experimental** settings,
   then verify that the plugin installs through `/xlplugins`.

## Later releases

1. Change `<Version>` in `DjScraperPlugin.csproj` (for example, from
   `0.1.0.0` to `0.2.0.0`).
2. Change `AssemblyVersion` in `pluginmaster.json` to the exact same value,
   and replace `LastUpdate` with the current Unix timestamp.
3. Build `Release`, upload the generated `latest.zip` to a new GitHub Release,
   and commit/push the changed project file and `pluginmaster.json`.

The version in `pluginmaster.json` must exactly match the version embedded in
the generated plugin manifest; otherwise Dalamud will reject the update.
