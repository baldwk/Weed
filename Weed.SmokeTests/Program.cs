using Weed.Abstractions;
using Weed.App;
using Weed.Core;
using Weed.Platform.Windows;
using Weed.Plugins.AppLauncher;
using Weed.Plugins.Calculator;
using Weed.Plugins.Clipboard;
using Weed.Plugins.RunCommand;
using Weed.Plugins.Screenshot;
using IQueryProvider = Weed.Abstractions.IQueryProvider;

var root = Path.Combine(Path.GetTempPath(), "WeedSmoke", Guid.NewGuid().ToString("N"));
var paths = new AppPaths(Path.Combine(root, "appdata"), Path.Combine(root, "local"));
var settings = new SettingsRepository(paths);
settings.Load();
var logger = new ConsoleWeedLogger();
var host = new SmokeHost(logger, settings);

var plugins = new List<LoadedPlugin>();
var appLauncherPlugin = new AppLauncherPlugin();
var calculatorPlugin = new CalculatorPlugin();
var clipboardPlugin = new ClipboardPlugin();
var runCommandPlugin = new RunCommandPlugin();
var screenshotPlugin = new ScreenshotPlugin();
await AddPluginAsync(AppLauncherPlugin.Manifest, appLauncherPlugin);
await AddPluginAsync(CalculatorPlugin.Manifest, calculatorPlugin);
await AddPluginAsync(ClipboardPlugin.Manifest, clipboardPlugin);
await AddPluginAsync(RunCommandPlugin.Manifest, runCommandPlugin);
await AddPluginAsync(ScreenshotPlugin.Manifest, screenshotPlugin);

settings.EnsurePluginDefaults(plugins.Select(p => p.Manifest));
var usage = new UsageHistoryStore(paths.DatabaseFile);
usage.Load();
var router = new QueryRouter(settings, usage);
router.SetPlugins(plugins);

var empty = await router.QueryAsync("", CancellationToken.None);
Require(empty.Count == 0, "empty query should not show plugin startup results");

var calc = await router.QueryAsync("1+2*3", CancellationToken.None);
Require(calc.Any(r => r.Result.Title.Contains("= 7", StringComparison.Ordinal)), "calculator 1+2*3 should equal 7");

var sqrt = await router.QueryAsync("sqrt(9)", CancellationToken.None);
Require(sqrt.Any(r => r.Result.Title.Contains("= 3", StringComparison.Ordinal)), "calculator sqrt(9) should equal 3");

var doubleStarPower = await router.QueryAsync("2**3", CancellationToken.None);
Require(doubleStarPower.Any(r => r.Result.Title.Contains("= 8", StringComparison.Ordinal)), "calculator 2**3 should equal 8");

var caretPower = await router.QueryAsync("2^3", CancellationToken.None);
Require(caretPower.Any(r => r.Result.Title.Contains("= 8", StringComparison.Ordinal)), "calculator 2^3 should equal 8");

var factorial = await router.QueryAsync("5!", CancellationToken.None);
Require(factorial.Any(r => r.Result.Title.Contains("= 120", StringComparison.Ordinal)), "calculator 5! should equal 120");

var zeroFactorial = await router.QueryAsync("0!", CancellationToken.None);
Require(zeroFactorial.Any(r => r.Result.Title.Contains("= 1", StringComparison.Ordinal)), "calculator 0! should equal 1");

var modulo = await router.QueryAsync("10%3", CancellationToken.None);
Require(modulo.Any(r => r.Result.Title.Contains("= 1", StringComparison.Ordinal)), "calculator 10%3 should equal 1");

var postfixPercent = await router.QueryAsync("50%", CancellationToken.None);
Require(postfixPercent.Any(r => r.Result.Title.Contains("= 0.5", StringComparison.Ordinal)), "calculator 50% should equal 0.5");

var multiplicationPercent = await router.QueryAsync("200*10%", CancellationToken.None);
Require(multiplicationPercent.Any(r => r.Result.Title.Contains("= 20", StringComparison.Ordinal)), "calculator should support percent in multiplication");

var invalidFactorial = await router.QueryAsync("2.5!", CancellationToken.None);
Require(!invalidFactorial.Any(r => r.Result.PluginId == CalculatorPlugin.PluginId), "calculator should reject fractional factorial");

var shot = await router.QueryAsync("shot", CancellationToken.None);
Require(shot.Any(r => r.Result.PluginId == ScreenshotPlugin.PluginId && r.Result.Id == "screenshot-region"), "shot keyword should route to screenshot plugin");
var scrollShot = shot.FirstOrDefault(r => r.Result.Id == "screenshot-scrolling");
Require(scrollShot is not null, "shot keyword should expose scrolling screenshot");
await router.ExecuteAsync(scrollShot!.Result, "screenshot.scrolling", CancellationToken.None);
Require(host.ScreenCapture.ScrollingCalled, "screenshot scrolling command should dispatch to screen capture service");

var copyTarget = calc.First(r => r.Result.Title.Contains("= 7", StringComparison.Ordinal));
var copy = await router.ExecuteAsync(copyTarget.Result, copyTarget.Result.DefaultCommand, CancellationToken.None);
Require(copy.Succeeded && host.Clipboard.Text == "7", "calculator default action should copy result");
Require(File.Exists(paths.DatabaseFile), "SQLite database should be created");
Require(usage.ReadAll().Any(i => i.PluginId == "weed.calculator" && i.ResultId == copyTarget.Result.Id), "usage history should be written to SQLite");

var runCmd = await router.QueryAsync("cmd", CancellationToken.None);
var cmdTarget = runCmd.FirstOrDefault(r => r.Result.PluginId == RunCommandPlugin.PluginId && r.Result.Id == "run-cmd");
Require(cmdTarget is not null, "Run Command should return cmd");
var cmdOpen = await router.ExecuteAsync(cmdTarget!.Result, cmdTarget.Result.DefaultCommand, CancellationToken.None);
Require(cmdOpen.Succeeded && host.Shell.OpenedPath == "cmd.exe", "Run Command should execute cmd through shell open");

var runRegedit = await router.QueryAsync("regedit", CancellationToken.None);
var regeditTarget = runRegedit.FirstOrDefault(r => r.Result.PluginId == RunCommandPlugin.PluginId && r.Result.Id == "run-regedit");
Require(regeditTarget is not null, "Run Command should return regedit");
await router.ExecuteAsync(regeditTarget!.Result, regeditTarget.Result.DefaultCommand, CancellationToken.None);
Require(host.Shell.OpenedPath == "regedit.exe", "Run Command should execute regedit through shell open");

var chineseEntry = AppLauncherPlugin.CreateEntry("微信", @"C:\Fake\WeChat.lnk");
Require(AppLauncherPlugin.Score(chineseEntry, "weixin") > 0, "AppLauncher should match Chinese app names by pinyin");
Require(AppLauncherPlugin.Score(chineseEntry, "wx") > 0, "AppLauncher should match Chinese app names by pinyin initials");
Require(chineseEntry.Id == AppLauncherPlugin.CreateEntry("微信", @"C:\Fake\WeChat.lnk").Id, "AppLauncher result ids should be stable across identical shortcuts");
var shortContainsEntry = AppLauncherPlugin.CreateEntry("My App", @"C:\Fake\My App.lnk");
var longContainsEntry = AppLauncherPlugin.CreateEntry("My Application Suite", @"C:\Fake\My Application Suite.lnk");
Require(AppLauncherPlugin.Score(shortContainsEntry, "app") > AppLauncherPlugin.Score(longContainsEntry, "app"), "AppLauncher should rank contains matches by match proportion");
var denseContainsEntry = AppLauncherPlugin.CreateEntry("Banana Tool", @"C:\Fake\Banana Tool.lnk");
var sparseContainsEntry = AppLauncherPlugin.CreateEntry("Steam", @"C:\Fake\Steam.lnk");
Require(AppLauncherPlugin.Score(denseContainsEntry, "a") > AppLauncherPlugin.Score(sparseContainsEntry, "a"), "AppLauncher should rank single-character contains matches by character proportion");
Require(!AppLauncherPlugin.ShouldIndexEntry("卸载微信", @"C:\Fake\卸载微信.lnk", new ShortcutInfo { TargetPath = @"C:\Fake\Uninstall.exe" }), "AppLauncher should hide Chinese uninstall shortcuts");
Require(!AppLauncherPlugin.ShouldIndexEntry("Uninstall Node.js", @"C:\Fake\Uninstall Node.js.lnk", new ShortcutInfo { TargetPath = @"C:\Windows\SysWOW64\msiexec.exe", Arguments = "/x {TEST}" }), "AppLauncher should hide msiexec uninstall shortcuts");
Require(!AppLauncherPlugin.ShouldIndexEntry("Uninstall Cheat Engine", @"C:\Fake\Uninstall Cheat Engine.lnk", new ShortcutInfo { TargetPath = @"C:\Fake\unins000.exe" }), "AppLauncher should hide unins executables");
Require(AppLauncherPlugin.ShouldIndexEntry("Visual Studio Installer", @"C:\Fake\Visual Studio Installer.lnk", new ShortcutInfo { TargetPath = @"C:\Fake\VisualStudioInstaller.exe" }), "AppLauncher should keep normal installer apps");
Require(AppLauncherPlugin.ShouldIndexEntry("Inno Setup Compiler", @"C:\Fake\Inno Setup Compiler.lnk", new ShortcutInfo { TargetPath = @"C:\Fake\Compil32.exe" }), "AppLauncher should keep normal setup tools");
Require(AppLauncherPlugin.ShouldIndexEntry("Uninstall Node.js", @"C:\Fake\Uninstall Node.js.lnk", new ShortcutInfo { TargetPath = @"C:\Windows\SysWOW64\msiexec.exe", Arguments = "/x {TEST}" }, hideMaintenanceShortcuts: false), "AppLauncher should allow uninstall shortcuts when filtering is disabled");
await appLauncherPlugin.ExecuteAsync(new CommandContext
{
    PluginId = AppLauncherPlugin.PluginId,
    Command = "app.openAdmin",
    Data = new Dictionary<string, string>
    {
        ["shortcutPath"] = @"C:\Fake\WeChat.lnk",
        ["targetPath"] = @"C:\Fake\WeChat.exe",
        ["arguments"] = "--smoke",
        ["workingDirectory"] = @"C:\Fake"
    }
}, CancellationToken.None);
Require(host.Shell.AdminPath == @"C:\Fake\WeChat.exe", "AppLauncher should dispatch run-as-admin action to shell");
var refreshApps = await router.QueryAsync("refresh apps", CancellationToken.None);
var refreshAppsResult = refreshApps.FirstOrDefault(r => r.Result.PluginId == AppLauncherPlugin.PluginId && r.Result.Id == "app-refresh-index");
Require(refreshAppsResult is not null, "AppLauncher should expose a manual refresh command");
var refreshAppsCommand = await router.ExecuteAsync(refreshAppsResult!.Result, refreshAppsResult.Result.DefaultCommand, CancellationToken.None);
Require(refreshAppsCommand.Succeeded, "AppLauncher refresh command should succeed");
Require(File.Exists(Path.Combine(settings.GetPluginDataDirectory(AppLauncherPlugin.PluginId), "app-launcher.db")), "AppLauncher should persist its Start Menu index cache");

host.Clipboard.SetSnapshot(new ClipboardSnapshot
{
    Kind = ClipboardContentKind.Text,
    TextContent = "weed sqlite clipboard smoke"
});
await clipboardPlugin.StartAsync(CancellationToken.None);
await Task.Delay(1100);
var clipText = await router.QueryAsync("clip sqlite clipboard", CancellationToken.None);
Require(clipText.Any(r => r.Result.PluginId == ClipboardPlugin.PluginId), "clipboard text should be searchable from SQLite/FTS");
var clipTextResult = clipText.First(r => r.Result.PluginId == ClipboardPlugin.PluginId);
Require(clipTextResult.Result.Data.TryGetValue("displayLayout", out var textDisplayLayout) && textDisplayLayout == "detail", "clipboard text should request detail result layout");
Require(clipTextResult.Result.Data.TryGetValue("previewText", out var textPreview) && textPreview.Contains("weed sqlite clipboard smoke", StringComparison.Ordinal), "clipboard text should expose preview text");
var clipTextItem = SearchResultItem.FromRanked(clipTextResult);
Require(clipTextItem.HasDetailLayout && clipTextItem.HasDetailText && Near(clipTextItem.RowHeight, 66), "launcher should keep detail-capable clipboard rows at standard list height");
var clipTextPreviewModel = new LauncherViewModel();
clipTextPreviewModel.SetResults([clipTextResult], 20);
clipTextPreviewModel.SelectedIndex = 0;
Require(clipTextPreviewModel.HasSelectedPreview &&
        clipTextPreviewModel.SelectedPreviewHasText &&
        clipTextPreviewModel.SelectedPreviewText.Contains("weed sqlite clipboard smoke", StringComparison.Ordinal),
    "launcher should expose selected clipboard text in the side preview");

host.Clipboard.SetSnapshot(new ClipboardSnapshot
{
    Kind = ClipboardContentKind.Text,
    TextContent = "微信 剪贴板 smoke"
});
await Task.Delay(1100);
var clipPinyin = await router.QueryAsync("clip weixin", CancellationToken.None);
Require(clipPinyin.Any(r => r.Result.PluginId == ClipboardPlugin.PluginId && r.Result.Title.Contains("微信", StringComparison.Ordinal)), "clipboard text should be searchable by pinyin");
var clipInitials = await router.QueryAsync("clip wx", CancellationToken.None);
Require(clipInitials.Any(r => r.Result.PluginId == ClipboardPlugin.PluginId && r.Result.Title.Contains("微信", StringComparison.Ordinal)), "clipboard text should be searchable by pinyin initials");
var clipTypeFilter = await router.QueryAsync("clip type:text weixin", CancellationToken.None);
Require(clipTypeFilter.Any(r => r.Result.PluginId == ClipboardPlugin.PluginId && r.Result.Data.TryGetValue("kind", out var textKind) && textKind == "text"), "clipboard search should support type filters with pinyin queries");

host.Clipboard.SetSnapshot(new ClipboardSnapshot
{
    Kind = ClipboardContentKind.Files,
    Files = [Path.Combine(root, "smoke-file.txt")],
    TextContent = Path.Combine(root, "smoke-file.txt")
});
await Task.Delay(1100);
var clipFile = await router.QueryAsync("clip smoke-file", CancellationToken.None);
Require(clipFile.Any(r => r.Result.Subtitle?.Contains("files", StringComparison.OrdinalIgnoreCase) == true), "clipboard file list should be captured and searchable");

host.Clipboard.SetSnapshot(new ClipboardSnapshot
{
    Kind = ClipboardContentKind.Image,
    ImagePng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII="),
    TextContent = "Image smoke"
});
await Task.Delay(1100);
var clipImage = await router.QueryAsync("clip Image smoke", CancellationToken.None);
var imageResult = clipImage.FirstOrDefault(r => r.Result.PluginId == ClipboardPlugin.PluginId && r.Result.Data.TryGetValue("kind", out var kind) && kind == "image");
Require(imageResult is not null, "clipboard image should be searchable");
Require(imageResult!.Result.Data.TryGetValue("objectPath", out var imageObjectPath) && File.Exists(imageObjectPath), "clipboard image result should expose a preview object path");
Require(imageResult.Result.Data.TryGetValue("displayLayout", out var imageDisplayLayout) && imageDisplayLayout == "detail", "clipboard image should request detail result layout");
var imageTypeFilter = await router.QueryAsync("clip type:image", CancellationToken.None);
Require(imageTypeFilter.Any(r => r.Result.PluginId == ClipboardPlugin.PluginId && r.Result.Data.TryGetValue("kind", out var filteredKind) && filteredKind == "image"), "clipboard search should support type-only filters");
var clipImageItem = SearchResultItem.FromRanked(imageResult);
Require(clipImageItem.HasDetailLayout && clipImageItem.HasPreviewImage, "launcher should map clipboard image to a preview row");
var clipImagePreviewModel = new LauncherViewModel();
clipImagePreviewModel.SetResults([imageResult], 20);
clipImagePreviewModel.SelectedIndex = 0;
Require(clipImagePreviewModel.HasSelectedPreview && clipImagePreviewModel.SelectedPreviewHasImage, "launcher should expose selected clipboard images in the side preview");
Require(File.Exists(Path.Combine(settings.GetPluginDataDirectory(ClipboardPlugin.PluginId), "clipboard.db")), "clipboard SQLite database should be created");
await clipboardPlugin.StopAsync(CancellationToken.None);

Require(File.Exists(paths.SettingsFile), "settings file should be written");
Require(File.Exists(paths.HotkeysFile), "hotkeys file should be written");
Require(File.Exists(paths.PluginsFile), "plugins file should be written");
Require(HotkeyText.Normalize("Shift+Ctrl+C") == "Ctrl+Shift+C", "hotkeys should normalize modifier order");
Require(SettingsWindow.ComposeHotkeyText(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift, System.Windows.Input.Key.C) == "Ctrl+Shift+C",
    "hotkey capture should format pressed key gestures");
var appLauncherSettings = ((IPluginSettingsProvider)appLauncherPlugin).GetSettings();
Require(appLauncherSettings.Count == 1 && appLauncherSettings.Any(s => s.Key == "hideMaintenanceShortcuts"), "AppLauncher should only expose filter setting");
Require(appLauncherSettings.All(s => s.Key != "maxResults"), "AppLauncher should hide advanced max results setting");
var clipboardSettings = ((IPluginSettingsProvider)clipboardPlugin).GetSettings();
Require(clipboardSettings.Count == 5 &&
        clipboardSettings.Any(s => s.Key == "captureImages") &&
        clipboardSettings.Any(s => s.Key == "captureFileLists") &&
        clipboardSettings.Any(s => s.Key == "retentionDays") &&
        clipboardSettings.Any(s => s.Key == "maxItems") &&
        clipboardSettings.Any(s => s.Key == "maxObjectMegabytes"), "Clipboard should expose capture, retention, and object quota settings");
Require(((IPluginSettingsProvider)calculatorPlugin).GetSettings().Count == 0, "Calculator should not expose visible plugin settings");
var screenshotSettings = ((IPluginSettingsProvider)screenshotPlugin).GetSettings();
Require(screenshotSettings.Count == 5 &&
        screenshotSettings.Any(s => s.Key == "defaultSaveDirectory") &&
        screenshotSettings.Any(s => s.Key == "defaultFormat") &&
        screenshotSettings.Any(s => s.Key == "jpegQuality") &&
        screenshotSettings.Any(s => s.Key == "defaultColor") &&
        screenshotSettings.Any(s => s.Key == "defaultLineWidth"), "Screenshot should expose save and annotation defaults");
settings.SetPluginSetting(AppLauncherPlugin.PluginId, "hideMaintenanceShortcuts", false);
Require(settings.GetPluginSetting(AppLauncherPlugin.PluginId, "hideMaintenanceShortcuts", true) == false, "plugin settings should persist typed values");
settings.SetPluginSetting(ScreenshotPlugin.PluginId, "defaultColor", "Blue");
Require(settings.GetPluginSetting(ScreenshotPlugin.PluginId, "defaultColor", "Red") == "Blue", "screenshot color setting should persist for the next capture");
Require(SettingsWindow.ShouldShowPriorityControl(AppLauncherPlugin.Manifest), "AppLauncher should expose plugin priority because it supports implicit query");
Require(SettingsWindow.ShouldShowPriorityControl(CalculatorPlugin.Manifest), "Calculator should expose plugin priority because it supports implicit query");
Require(SettingsWindow.ShouldShowPriorityControl(RunCommandPlugin.Manifest), "Run Command should expose plugin priority because it supports implicit query");
Require(!SettingsWindow.ShouldShowPriorityControl(ClipboardPlugin.Manifest), "Clipboard should not expose plugin priority because it has no implicit query activation");
Require(!SettingsWindow.ShouldShowPriorityControl(ScreenshotPlugin.Manifest), "Screenshot should not expose plugin priority because it has no implicit query activation");

var launcherViewModel = new LauncherViewModel();
var rankedResults = Enumerable.Range(0, 45)
    .Select(i => new RankedResult(
        new WeedResult
        {
            Id = $"result-{i}",
            PluginId = CalculatorPlugin.PluginId,
            Title = $"Result {i}",
            DefaultCommand = "copy"
        },
        "Calculator",
        900 - i,
        0,
        0,
        0,
        i,
        null))
    .ToArray();
launcherViewModel.SetResults(rankedResults, 20);
Require(launcherViewModel.DisplayedResultCount == 20, "launcher should initially display 20 real results");
Require(launcherViewModel.Results.Count == 20 && launcherViewModel.HasMoreResults && launcherViewModel.Results.All(result => !result.IsLoadMore), "launcher should not append a manual load more row");
launcherViewModel.SelectedIndex = 0;
Require(launcherViewModel.SelectedDetails == "Calculator", "launcher selected details should hide ranking score");
Require(!launcherViewModel.HasSelectedPreview, "launcher should hide the preview panel for standard results");
launcherViewModel.EnsureDisplayedThrough(20, 20);
Require(launcherViewModel.DisplayedResultCount == 40 && launcherViewModel.Results.Count == 40, "launcher should auto-load the next page when selection moves past the displayed results");
launcherViewModel.LoadMoreResults(20);
Require(launcherViewModel.DisplayedResultCount == 45 && launcherViewModel.Results.Count == 45 && !launcherViewModel.HasMoreResults, "launcher should remove load more row after all results are visible");

var detailRankedResult = new RankedResult(
    new WeedResult
    {
        Id = "detail-result",
        PluginId = "weed.test",
        Title = "Long text item",
        DefaultCommand = "copy",
        Data = new Dictionary<string, string>
        {
            ["displayLayout"] = "detail",
            ["previewText"] = string.Join(Environment.NewLine, Enumerable.Repeat("A long detail preview line", 8))
        }
    },
    "Test",
    1,
    0,
    0,
    0,
    0,
    null);
var detailResultItem = SearchResultItem.FromRanked(detailRankedResult);
Require(detailResultItem.HasDetailLayout &&
        detailResultItem.HasDetailText &&
        detailResultItem.DetailText.Contains("detail preview", StringComparison.Ordinal) &&
        Near(detailResultItem.RowHeight, 66),
    "generic detail layout should support long text previews without changing list row height");
var detailPreviewModel = new LauncherViewModel();
detailPreviewModel.SetResults([rankedResults[0], detailRankedResult], 20);
detailPreviewModel.SelectedIndex = 0;
Require(!detailPreviewModel.HasSelectedPreview, "launcher preview should be based only on the selected result");
detailPreviewModel.SelectedIndex = 1;
Require(detailPreviewModel.HasSelectedPreview &&
        detailPreviewModel.SelectedPreviewTitle == "Long text item" &&
        detailPreviewModel.SelectedPreviewHasText &&
        detailPreviewModel.SelectedPreviewText.Contains("detail preview", StringComparison.Ordinal),
    "launcher should update the side preview when a detail result is selected");

var toolbarSize = new System.Windows.Size(160, 40);
var toolbarBelow = ScreenshotOverlayLayout.PlaceToolbar(
    new System.Windows.Rect(100, 100, 200, 120),
    new System.Windows.Rect(0, 0, 800, 600),
    toolbarSize);
Require(Near(toolbarBelow.X, 140) && Near(toolbarBelow.Y, 230), "screenshot toolbar should prefer below the selection");

var toolbarAbove = ScreenshotOverlayLayout.PlaceToolbar(
    new System.Windows.Rect(100, 540, 200, 40),
    new System.Windows.Rect(0, 0, 800, 600),
    toolbarSize);
Require(Near(toolbarAbove.X, 140) && Near(toolbarAbove.Y, 490), "screenshot toolbar should move above near screen bottom");

var toolbarRight = ScreenshotOverlayLayout.PlaceToolbar(
    new System.Windows.Rect(80, 170, 100, 160),
    new System.Windows.Rect(0, 0, 500, 500),
    new System.Windows.Size(160, 220));
Require(Near(toolbarRight.X, 190) && Near(toolbarRight.Y, 170), "screenshot toolbar should move right when vertical space is tight");

var toolbarClamped = ScreenshotOverlayLayout.PlaceToolbar(
    new System.Windows.Rect(-350, 470, 100, 25),
    new System.Windows.Rect(-400, 0, 800, 500),
    new System.Windows.Size(220, 40));
Require(toolbarClamped.Left >= -400 && toolbarClamped.Right <= 400 && toolbarClamped.Top >= 0 && toolbarClamped.Bottom <= 500, "screenshot toolbar should clamp inside virtual screen bounds");

var deviceRect = ScreenshotOverlayLayout.DeviceRectangleFromPoints(
    new System.Windows.Point(125.2, 250.6),
    new System.Windows.Point(375.8, 501.1));
Require(deviceRect.Left == 125 && deviceRect.Top == 250 && deviceRect.Right == 376 && deviceRect.Bottom == 502, "screenshot device rect should floor origin and ceil extent");

var dipRect100 = ScreenshotOverlayLayout.DeviceRectToDipRect(new System.Drawing.Rectangle(100, 200, 300, 160), 1.0, 1.0);
Require(Near(dipRect100.X, 100) && Near(dipRect100.Y, 200) && Near(dipRect100.Width, 300) && Near(dipRect100.Height, 160), "screenshot DIP conversion should preserve 100 percent scale");

var dipRect125 = ScreenshotOverlayLayout.DeviceRectToDipRect(new System.Drawing.Rectangle(125, 250, 375, 200), 1.25, 1.25);
Require(Near(dipRect125.X, 100) && Near(dipRect125.Y, 200) && Near(dipRect125.Width, 300) && Near(dipRect125.Height, 160), "screenshot DIP conversion should account for 125 percent scale");

var selectedImageBounds = ScreenshotOverlayLayout.ImageBoundsForSelection(
    new System.Windows.Rect(90, 120, 320, 180),
    new System.Windows.Size(400, 225));
Require(Near(selectedImageBounds.X, 90) &&
        Near(selectedImageBounds.Y, 120) &&
        Near(selectedImageBounds.Width, 320) &&
        Near(selectedImageBounds.Height, 180), "screenshot edit frame should stay on the selected DIP bounds");

var negativeImageBounds = ScreenshotOverlayLayout.ImageBoundsForSelection(
    new System.Windows.Rect(-260, 80, 240, 160),
    new System.Windows.Size(300, 200));
Require(Near(negativeImageBounds.X, -260) &&
        Near(negativeImageBounds.Y, 80) &&
        Near(negativeImageBounds.Width, 240) &&
        Near(negativeImageBounds.Height, 160), "screenshot edit frame should not recenter negative-coordinate monitor selections");

var movedBounds = ScreenshotOverlayLayout.MoveSelectionBounds(
    new System.Windows.Rect(90, 120, 320, 180),
    new System.Windows.Vector(45, -30),
    new System.Windows.Rect(0, 0, 800, 600));
Require(Near(movedBounds.X, 135) &&
        Near(movedBounds.Y, 90) &&
        Near(movedBounds.Width, 320) &&
        Near(movedBounds.Height, 180), "screenshot move tool should move the selected screenshot bounds");

var clampedMoveBounds = ScreenshotOverlayLayout.MoveSelectionBounds(
    new System.Windows.Rect(90, 120, 320, 180),
    new System.Windows.Vector(900, 900),
    new System.Windows.Rect(0, 0, 800, 600));
Require(Near(clampedMoveBounds.X, 480) &&
        Near(clampedMoveBounds.Y, 420), "screenshot move tool should keep moved bounds inside the overlay");

var displayRect200 = new System.Windows.Rect(120, 160, 320, 180);
var pixelRect200 = ScreenshotOverlayLayout.DisplayRectToPixelRect(
    displayRect200,
    new System.Windows.Size(1920, 1080),
    new System.Windows.Size(3840, 2160));
Require(pixelRect200.Left == 240 &&
        pixelRect200.Top == 320 &&
        pixelRect200.Width == 640 &&
        pixelRect200.Height == 360, "snapshot selection should map display bounds to 200 percent pixel bounds");
var roundTripDisplay200 = ScreenshotOverlayLayout.PixelRectToDisplayRect(
    pixelRect200,
    new System.Windows.Size(1920, 1080),
    new System.Windows.Size(3840, 2160));
Require(Near(roundTripDisplay200.X, displayRect200.X) &&
        Near(roundTripDisplay200.Y, displayRect200.Y) &&
        Near(roundTripDisplay200.Width, displayRect200.Width) &&
        Near(roundTripDisplay200.Height, displayRect200.Height), "snapshot selection should round-trip display and pixel bounds");

var pixelRect125 = ScreenshotOverlayLayout.DisplayRectToPixelRect(
    new System.Windows.Rect(100, 80, 240, 160),
    new System.Windows.Size(1000, 800),
    new System.Windows.Size(1250, 1000));
Require(Math.Abs(pixelRect125.Left - 125) <= 1 &&
        Math.Abs(pixelRect125.Top - 100) <= 1 &&
        Math.Abs(pixelRect125.Width - 300) <= 1 &&
        Math.Abs(pixelRect125.Height - 200) <= 1, "snapshot selection should map display bounds to 125 percent pixel bounds");

var movedPixelSelection = ScreenshotOverlayLayout.MovePixelSelection(
    new System.Drawing.Rectangle(240, 320, 640, 360),
    new System.Drawing.Point(80, -120),
    new System.Drawing.Rectangle(0, 0, 3840, 2160));
Require(movedPixelSelection.Left == 320 &&
        movedPixelSelection.Top == 200 &&
        movedPixelSelection.Width == 640 &&
        movedPixelSelection.Height == 360, "snapshot move tool should move the pixel selection");

using (var synthetic = new System.Drawing.Bitmap(4, 2, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
{
    synthetic.SetPixel(0, 0, System.Drawing.Color.Red);
    synthetic.SetPixel(1, 0, System.Drawing.Color.Red);
    synthetic.SetPixel(2, 0, System.Drawing.Color.Blue);
    synthetic.SetPixel(3, 0, System.Drawing.Color.Blue);
    synthetic.SetPixel(0, 1, System.Drawing.Color.Red);
    synthetic.SetPixel(1, 1, System.Drawing.Color.Red);
    synthetic.SetPixel(2, 1, System.Drawing.Color.Blue);
    synthetic.SetPixel(3, 1, System.Drawing.Color.Blue);
    var cropBytes = ScreenshotOverlayLayout.RenderSelectionPng(synthetic, new System.Drawing.Rectangle(2, 0, 2, 2));
    using var cropStream = new MemoryStream(cropBytes);
    using var crop = new System.Drawing.Bitmap(cropStream);
    Require(crop.Width == 2 &&
            crop.Height == 2 &&
            crop.GetPixel(0, 0).ToArgb() == System.Drawing.Color.Blue.ToArgb(), "snapshot renderer should crop the selected pixels");
}

var atomicPath = Path.Combine(root, "atomic-screenshot.png");
ScreenshotFileIO.WriteAllBytesAtomic(atomicPath, [1, 2, 3]);
Require(File.ReadAllBytes(atomicPath).SequenceEqual(new byte[] { 1, 2, 3 }), "atomic screenshot writer should create output file");
ScreenshotFileIO.WriteAllBytesAtomic(atomicPath, [4, 5]);
Require(File.ReadAllBytes(atomicPath).SequenceEqual(new byte[] { 4, 5 }), "atomic screenshot writer should replace existing output file");
Require(!Directory.EnumerateFiles(root, "atomic-screenshot.png.*.tmp").Any(), "atomic screenshot writer should clean temporary files");

var updatePackage = Path.Combine(root, "Weed-test.zip");
File.WriteAllText(updatePackage, "weed update package");
var updateManifestPath = Path.Combine(root, "update-manifest.json");
var updateHash = UpdateService.Sha256File(updatePackage);
var futureVersion = new Version(UpdateService.CurrentVersion.Major + 1, 0, 0).ToString();
File.WriteAllText(updateManifestPath, System.Text.Json.JsonSerializer.Serialize(new UpdateManifest
{
    Version = futureVersion,
    PublishedAt = DateTimeOffset.UtcNow.ToString("O"),
    PackageUrl = Path.GetFileName(updatePackage),
    Sha256 = updateHash,
    Notes = "smoke"
}));
var updateService = new UpdateService();
var updateCheck = await updateService.CheckAsync(updateManifestPath, CancellationToken.None);
Require(updateCheck.IsUpdateAvailable && updateCheck.Manifest is not null, "update service should detect a newer manifest version");
var updateDownload = await updateService.DownloadPackageAsync(updateCheck.Manifest!, paths.Updates, CancellationToken.None, updateManifestPath);
Require(updateDownload.Verified, "update package hash should verify");
Require(File.Exists(updateDownload.PackagePath), "update package should be downloaded to updates directory");
Require(UpdateService.Sha256File(updateDownload.PackagePath) == updateHash, "downloaded update package should match manifest hash");

Require(File.Exists(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "publish-release.ps1")), "publish script should exist");
Require(File.Exists(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "install-current-user.ps1")), "install script should exist");
Require(File.Exists(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "uninstall-current-user.ps1")), "uninstall script should exist");
Require(File.Exists(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "templates", "plugin", "Example.Plugin.csproj")), "plugin project template should exist");
Require(File.Exists(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "schemas", "manifest.schema.json")), "plugin manifest schema should exist");

Console.WriteLine("Smoke checks passed.");

async Task AddPluginAsync(WeedPluginManifest manifest, IWeedPlugin plugin)
{
    await plugin.InitializeAsync(host, CancellationToken.None);
    plugins.Add(new LoadedPlugin(
        manifest,
        plugin,
        plugin as IQueryProvider,
        plugin as ICommandHandler,
        plugin as IResidentPlugin));
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static bool Near(double actual, double expected) => Math.Abs(actual - expected) < 0.01;

public sealed class ConsoleWeedLogger : IWeedLogger
{
    public void Info(string message) => Console.WriteLine($"INFO {message}");

    public void Warn(string message) => Console.WriteLine($"WARN {message}");

    public void Error(string message, Exception? exception = null) =>
        Console.WriteLine($"ERROR {message} {exception}");
}

public sealed class SmokeHost : IWeedHost
{
    public SmokeHost(IWeedLogger logger, SettingsRepository settings)
    {
        Logger = logger;
        Settings = settings;
        Storage = settings;
        Clipboard = new SmokeClipboard();
        Shell = new SmokeShell();
        Windows = new SmokeWindows();
        ScreenCapture = new SmokeScreenCapture();
    }

    public IWeedLogger Logger { get; }

    public IWeedSettings Settings { get; }

    public IWeedStorage Storage { get; }

    public SmokeClipboard Clipboard { get; }

    IWeedClipboard IWeedHost.Clipboard => Clipboard;

    public SmokeShell Shell { get; }

    IWeedShell IWeedHost.Shell => Shell;

    public IWeedWindowService Windows { get; }

    public SmokeScreenCapture ScreenCapture { get; }

    IWeedScreenCapture IWeedHost.ScreenCapture => ScreenCapture;
}

public sealed class SmokeClipboard : IWeedClipboard
{
    private ClipboardSnapshot? _snapshot;

    public string? Text { get; private set; }

    public ValueTask<ClipboardSnapshot?> TryReadAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(_snapshot ?? (Text is null
            ? null
            : new ClipboardSnapshot { Kind = ClipboardContentKind.Text, TextContent = Text }));

    public ValueTask<string?> TryGetTextAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Text);

    public ValueTask SetTextAsync(string text, CancellationToken cancellationToken)
    {
        Text = text;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetFilesAsync(IReadOnlyList<string> files, CancellationToken cancellationToken)
    {
        Text = string.Join(Environment.NewLine, files);
        return ValueTask.CompletedTask;
    }

    public ValueTask PasteTextAsync(string text, CancellationToken cancellationToken)
    {
        Text = text;
        return ValueTask.CompletedTask;
    }

    public ValueTask PasteCurrentAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask SetImageAsync(string imagePath, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public void SetSnapshot(ClipboardSnapshot snapshot)
    {
        _snapshot = snapshot;
        Text = snapshot.TextContent;
    }
}

public sealed class SmokeShell : IWeedShell
{
    public string? AdminPath { get; private set; }

    public string? OpenedPath { get; private set; }

    public ValueTask OpenAsync(string pathOrUri, CancellationToken cancellationToken)
    {
        OpenedPath = pathOrUri;
        return ValueTask.CompletedTask;
    }

    public ValueTask OpenAsAdministratorAsync(string path, string? arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        AdminPath = path;
        return ValueTask.CompletedTask;
    }

    public ValueTask OpenContainingFolderAsync(string path, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask CopyPathAsync(string path, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

public sealed class SmokeWindows : IWeedWindowService
{
    public ValueTask ShowLauncherAsync(string? initialQuery, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask ShowClipboardPanelAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask ShowMessageAsync(string title, string message, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

public sealed class SmokeScreenCapture : IWeedScreenCapture
{
    public bool ScrollingCalled { get; private set; }

    public ValueTask<ScreenCaptureResult?> CaptureRegionInteractiveAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<ScreenCaptureResult?>(null);

    public ValueTask<ScreenCaptureResult?> CapturePrimaryScreenAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<ScreenCaptureResult?>(null);

    public ValueTask<ScreenCaptureResult?> CaptureScrollingInteractiveAsync(CancellationToken cancellationToken)
    {
        ScrollingCalled = true;
        return ValueTask.FromResult<ScreenCaptureResult?>(new ScreenCaptureResult
        {
            FilePath = Path.Combine(Path.GetTempPath(), "weed-scroll-smoke.png"),
            Width = 10,
            Height = 30
        });
    }
}
