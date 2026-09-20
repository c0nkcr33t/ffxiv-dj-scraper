using System.Numerics;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using DjScraperPlugin.Services;

namespace DjScraperPlugin.Windows;

internal sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private string sourceUrl = string.Empty;
    private string trackTitle = string.Empty;
    private string status = "Ready to download a track.";
    private Task<SetupValidationResult>? validationTask;
    private Task<DependencyInstallResult>? installationTask;
    private Task<WorkflowResult>? workflowTask;
    private SetupValidationResult? lastValidation;

    public MainWindow(Plugin plugin)
        : base("DJ Scraper###DjScraper")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 340),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        UpdateBackgroundTasks();

        PushTheme();
        try
        {
            ImGui.TextUnformatted("DJ Scraper");
            ImGui.TextDisabled("Download a track and add it to your selected playlist.");
            ImGui.Spacing();

            if (ImGui.BeginTabBar("##DjScraperTabs"))
            {
                if (ImGui.BeginTabItem("Download"))
                {
                    DrawDownloadTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Settings"))
                {
                    DrawSettingsTab();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }

            ImGui.Separator();
            ImGui.TextWrapped(status);
        }
        finally
        {
            PopTheme();
        }
    }

    private void DrawDownloadTab()
    {
        ImGui.TextUnformatted("Source URL");
        ImGui.TextDisabled("Paste a YouTube URL to download its audio.");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##SourceUrl", "https://www.youtube.com/watch?v=...", ref sourceUrl, 2048);

        if (!string.IsNullOrWhiteSpace(trackTitle))
        {
            ImGui.Spacing();
            ImGui.TextDisabled($"Last resolved title: {trackTitle}");
            ImGui.TextDisabled($"Output: scraper/{MetadataService.SanitizeTrackName(trackTitle)}.scd");
        }

        ImGui.Spacing();
        var canStart = workflowTask is null && !string.IsNullOrWhiteSpace(sourceUrl);
        if (!canStart)
            ImGui.BeginDisabled();

        if (ImGui.Button(workflowTask is null ? "Download and add track" : "Adding track…", new Vector2(-1, 0)))
        {
            status = "Downloading audio, converting to Ogg/SCD, then updating metadata…";
            workflowTask = TrackWorkflowService.RunAsync(
                sourceUrl,
                plugin.Configuration.ModDirectory,
                plugin.Configuration.PlaylistName,
                plugin.Configuration.FfmpegPath,
                plugin.Configuration.YtDlpPath);
        }

        if (!canStart)
            ImGui.EndDisabled();

        if (string.IsNullOrWhiteSpace(sourceUrl))
            ImGui.TextDisabled("Add a source URL to enable downloading.");
        else if (workflowTask is not null)
            ImGui.TextDisabled("The track is being processed in the background.");
    }

    private void DrawSettingsTab()
    {
        plugin.Configuration.ModDirectory = DrawInput("Mod directory", plugin.Configuration.ModDirectory, "Path containing meta.json");
        plugin.Configuration.PlaylistName = DrawInput("Playlist name", plugin.Configuration.PlaylistName, "My Playlist");

        ImGui.Spacing();
        ImGui.TextDisabled("External tools");
        plugin.Configuration.FfmpegPath = DrawInput("FFmpeg", plugin.Configuration.FfmpegPath, "ffmpeg or full path to ffmpeg.exe");
        plugin.Configuration.YtDlpPath = DrawInput("yt-dlp", plugin.Configuration.YtDlpPath, "yt-dlp or full path to yt-dlp.exe");

        if (ImGui.Button("Save settings"))
        {
            plugin.Configuration.Save();
            status = "Settings saved.";
        }
        ImGui.SameLine();
        if (validationTask is null && ImGui.Button("Validate setup"))
        {
            plugin.Configuration.Save();
            status = "Validating mod files and external tools…";
            validationTask = ValidateSetupAsync(
                plugin.Configuration.ModDirectory,
                plugin.Configuration.FfmpegPath,
                plugin.Configuration.YtDlpPath);
        }
        else if (validationTask is not null)
        {
            ImGui.BeginDisabled();
            ImGui.Button("Validating…");
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        if (installationTask is null && validationTask is null && lastValidation is { AllPassed: false } && ImGui.Button("Install missing dependencies"))
        {
            status = "Downloading missing dependencies…";
            installationTask = DependencyInstaller.InstallAsync(
                Plugin.PluginInterface.ConfigDirectory.FullName,
                installFfmpeg: !lastValidation.FfmpegAvailable,
                installYtDlp: !lastValidation.YtDlpAvailable);
        }
        else if (installationTask is not null)
        {
            ImGui.BeginDisabled();
            ImGui.Button("Installing dependencies…");
            ImGui.EndDisabled();
        }
        else if (lastValidation is null)
        {
            ImGui.BeginDisabled();
            ImGui.Button("Validate setup before installing");
            ImGui.EndDisabled();
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.Button("All dependencies are available");
            ImGui.EndDisabled();
        }
        ImGui.TextDisabled("Only failed tools are downloaded into this plugin's private tools folder.");

    }

    private static string DrawInput(string label, string value, string hint)
    {
        ImGui.TextUnformatted(label);
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint($"##{label}", hint, ref value, 1024);
        return value;
    }

    private void UpdateBackgroundTasks()
    {
        if (validationTask is { IsCompleted: true })
        {
            try
            {
                lastValidation = validationTask.GetAwaiter().GetResult();
                status = lastValidation.Message;
            }
            catch (Exception ex)
            {
                status = $"Validation failed unexpectedly: {ex.Message}";
            }
            finally
            {
                validationTask = null;
            }
        }

        if (installationTask is { IsCompleted: true })
        {
            try
            {
                var result = installationTask.GetAwaiter().GetResult();
                status = result.Message;
                if (result.Success)
                {
                    if (result.FfmpegPath is not null)
                        plugin.Configuration.FfmpegPath = result.FfmpegPath;
                    if (result.YtDlpPath is not null)
                        plugin.Configuration.YtDlpPath = result.YtDlpPath;
                    plugin.Configuration.Save();
                    lastValidation = null;
                    status += " Paths saved; run Validate setup to confirm.";
                }
            }
            catch (Exception ex)
            {
                status = $"Dependency installation failed unexpectedly: {ex.Message}";
            }
            finally
            {
                installationTask = null;
            }
        }

        if (workflowTask is { IsCompleted: true })
        {
            try
            {
                var result = workflowTask.GetAwaiter().GetResult();
                trackTitle = result.Title;
                status = $"{(result.UpdatedExisting ? "Updated" : "Added")} '{result.Title}' — SCD and metadata backup created.";
            }
            catch (Exception ex)
            {
                status = $"Track workflow failed: {ex.Message}";
            }
            finally
            {
                workflowTask = null;
            }
        }
    }

    private static async Task<SetupValidationResult> ValidateSetupAsync(
        string modDirectory,
        string ffmpegPath,
        string ytDlpPath)
    {
        var mod = ValidateModDirectory(modDirectory);
        var ffmpeg = await ValidateToolAsync("FFmpeg", ffmpegPath, "-version");
        var ytDlp = await ValidateToolAsync("yt-dlp", ytDlpPath, "--version");
        var results = new[] { mod, ffmpeg, ytDlp };

        var passed = results.Count(result => result.Success);
        return new SetupValidationResult(
            passed == results.Length,
            ffmpeg.Success,
            ytDlp.Success,
            $"Setup check: {passed}/{results.Length} passed\n{string.Join("\n", results.Select(result => result.Message))}");
    }

    private static ValidationResult ValidateModDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return new ValidationResult(false, "✗ Mod directory does not exist.");
        if (!File.Exists(Path.Combine(directory, "meta.json")))
            return new ValidationResult(false, "✗ meta.json was not found in the mod directory.");
        return new ValidationResult(true, "✓ Mod directory and meta.json found.");
    }

    private static async Task<ValidationResult> ValidateToolAsync(string name, string command, string argument)
    {
        if (string.IsNullOrWhiteSpace(command))
            return new ValidationResult(false, $"✗ {name} command is empty.");

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = argument,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = await process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);

            if (process.ExitCode != 0)
                return new ValidationResult(false, $"✗ {name} exited with code {process.ExitCode}: {FirstLine(error)}");

            return new ValidationResult(true, $"✓ {name}: {FirstLine(output)}");
        }
        catch (OperationCanceledException)
        {
            return new ValidationResult(false, $"✗ {name} did not respond within 10 seconds.");
        }
        catch (Exception ex)
        {
            return new ValidationResult(false, $"✗ {name} could not run: {ex.Message}");
        }
    }

    private static string FirstLine(string text)
        => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "no version output";

    private static void PushTheme()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 5f);

        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.11f, 0.12f, 0.16f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.18f, 0.15f, 0.25f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.33f, 0.19f, 0.53f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.43f, 0.26f, 0.68f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.25f, 0.13f, 0.41f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Tab, new Vector4(0.13f, 0.14f, 0.19f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TabHovered, new Vector4(0.28f, 0.18f, 0.42f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TabActive, new Vector4(0.33f, 0.19f, 0.53f, 1f));
    }

    private static void PopTheme()
    {
        ImGui.PopStyleColor(8);
        ImGui.PopStyleVar(2);
    }

    private sealed record ValidationResult(bool Success, string Message);
    private sealed record SetupValidationResult(
        bool AllPassed,
        bool FfmpegAvailable,
        bool YtDlpAvailable,
        string Message);
}
