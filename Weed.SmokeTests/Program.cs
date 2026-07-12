using System.IO.Compression;
using Weed.Abstractions;
using Weed.App;
using Weed.Core;
using Weed.PluginHost;
using Weed.Platform.Windows;
using Weed.Plugins.AppLauncher;
using Weed.Plugins.Calculator;
using Weed.Plugins.Clipboard;
using Weed.Plugins.Emoji;
using Weed.Plugins.FileSearch;
using Weed.Plugins.RunCommand;
using Weed.Plugins.Screenshot;
using Weed.Plugins.Translate;
using Weed.Plugins.Toolbox;
using IQueryProvider = Weed.Abstractions.IQueryProvider;

var root = Path.Combine(Path.GetTempPath(), "WeedSmoke", Guid.NewGuid().ToString("N"));
var paths = new AppPaths(Path.Combine(root, "appdata"), Path.Combine(root, "local"));
var settings = new SettingsRepository(paths);
settings.Load();
var logger = new ConsoleWeedLogger();
Require(new WeedAppSettings().ExternalPluginRegistryUrl == WeedAppSettings.DefaultExternalPluginRegistryUrl,
    "new settings should use the official stable plugin registry");
await VerifySingleInstanceAsync();
Require(StartupManager.BuildCommand(@"C:\Program Files\Weed\Weed.exe") == "\"C:\\Program Files\\Weed\\Weed.exe\" --startup",
    "startup command should quote the executable and mark login startup");
Require(FileSearchPlugin.Manifest.ExternalDependencies.Any(dependency =>
        dependency.Id == "everything" && dependency.AutoStart && dependency.ReadinessProbe == "everythingIpc"),
    "File Search should declare its Everything runtime dependency");
var dependency = FileSearchPlugin.Manifest.ExternalDependencies.Single(dependency => dependency.Id == "everything");
var dependencyProbeCount = 0;
var dependencyStartCount = 0;
var dependencyCoordinator = new ExternalDependencyCoordinator(logger, TimeSpan.FromMilliseconds(50),
    _ => ++dependencyProbeCount >= 2,
    _ => @"C:\Everything\Everything.exe",
    _ => dependencyStartCount++);
var dependencyStatuses = await dependencyCoordinator.EnsureAsync([(FileSearchPlugin.PluginId, dependency)], CancellationToken.None);
Require(dependencyStartCount == 1 && dependencyStatuses.Single().Available,
    "dependency coordinator should start an unavailable dependency once and wait for readiness");
var missingDependencyCoordinator = new ExternalDependencyCoordinator(logger, TimeSpan.FromMilliseconds(10), _ => false, _ => null, _ => { });
var missingDependencyStatuses = await missingDependencyCoordinator.EnsureAsync([(FileSearchPlugin.PluginId, dependency)], CancellationToken.None);
Require(!missingDependencyStatuses.Single().Available,
    "dependency coordinator should report a missing executable without blocking startup");
var host = new SmokeHost(logger, settings);
await VerifyExternalPluginImportAsync(root, paths, host, logger);

var plugins = new List<LoadedPlugin>();
var appLauncherPlugin = new AppLauncherPlugin();
var calculatorPlugin = new CalculatorPlugin();
var clipboardPlugin = new ClipboardPlugin();
var emojiPlugin = new EmojiPlugin();
var translationClient = new SmokeTranslationClient();
var translatePlugin = new TranslatePlugin(translationClient);
var everythingClient = new SmokeEverythingSearchClient(
[
    new EverythingSearchResult(@"C:\Reports\Quarterly Report.pdf", FileSearchResultKind.File),
    new EverythingSearchResult(@"C:\Reports", FileSearchResultKind.Folder)
]);
var fileSearchPlugin = new FileSearchPlugin(everythingClient);
var runCommandPlugin = new RunCommandPlugin();
var screenshotPlugin = new ScreenshotPlugin();
var toolboxPlugin = new ToolboxPlugin();
await AddPluginAsync(AppLauncherPlugin.Manifest, appLauncherPlugin);
await AddPluginAsync(CalculatorPlugin.Manifest, calculatorPlugin);
await AddPluginAsync(ClipboardPlugin.Manifest, clipboardPlugin);
await AddPluginAsync(EmojiPlugin.Manifest, emojiPlugin);
await AddPluginAsync(TranslatePlugin.Manifest, translatePlugin);
await AddPluginAsync(FileSearchPlugin.Manifest, fileSearchPlugin);
await AddPluginAsync(RunCommandPlugin.Manifest, runCommandPlugin);
await AddPluginAsync(ScreenshotPlugin.Manifest, screenshotPlugin);
await AddPluginAsync(ToolboxPlugin.Manifest, toolboxPlugin);

settings.EnsurePluginDefaults(plugins.Select(p => p.Manifest));
var usage = new UsageHistoryStore(paths.DatabaseFile);
usage.Load();
var router = new QueryRouter(settings, usage);
router.SetPlugins(plugins);
settings.SetPluginSetting(TranslatePlugin.PluginId, "queryDelayMilliseconds", 0);

var empty = await router.QueryAsync("", CancellationToken.None);
Require(empty.Count == 0, "empty query should not show plugin startup results");

await VerifyToolboxAsync(router, settings, host, logger);

var calc = await router.QueryAsync("1+2*3", CancellationToken.None);
Require(calc.Any(r => r.Result.Title.Contains("= 7", StringComparison.Ordinal)), "calculator 1+2*3 should equal 7");

var sqrt = await router.QueryAsync("sqrt(9)", CancellationToken.None);
Require(sqrt.Any(r => r.Result.Title.Contains("= 3", StringComparison.Ordinal)), "calculator sqrt(9) should equal 3");

var log = await router.QueryAsync("log(100)", CancellationToken.None);
Require(log.Any(r => r.Result.Title.Contains("= 2", StringComparison.Ordinal)), "calculator log(100) should equal 2");

var naturalLog = await router.QueryAsync("ln(e)", CancellationToken.None);
Require(naturalLog.Any(r => r.Result.Title.Contains("= 1", StringComparison.Ordinal)), "calculator ln(e) should equal 1");

var logBase2 = await router.QueryAsync("log2(8)", CancellationToken.None);
Require(logBase2.Any(r => r.Result.Title.Contains("= 3", StringComparison.Ordinal)), "calculator log2(8) should equal 3");

var logBase3 = await router.QueryAsync("log3(81)", CancellationToken.None);
Require(logBase3.Any(r => r.Result.Title.Contains("= 4", StringComparison.Ordinal)), "calculator log3(81) should equal 4");

var logBase10 = await router.QueryAsync("log10(1000)", CancellationToken.None);
Require(logBase10.Any(r => r.Result.Title.Contains("= 3", StringComparison.Ordinal)), "calculator log10(1000) should equal 3");

var invalidLogBase = await router.QueryAsync("log1(10)", CancellationToken.None);
Require(!invalidLogBase.Any(r => r.Result.PluginId == CalculatorPlugin.PluginId), "calculator should reject log base 1");

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

var emoji = await router.QueryAsync("emoji rocket", CancellationToken.None);
var rocket = emoji.FirstOrDefault(r => r.Result.PluginId == EmojiPlugin.PluginId && r.Result.Data.TryGetValue("emoji", out var value) && value == "🚀");
Require(rocket is not null, "Emoji Search should find rocket by name");
var emojiCopy = await router.ExecuteAsync(rocket!.Result, rocket.Result.DefaultCommand, CancellationToken.None);
Require(emojiCopy.Succeeded && host.Clipboard.Text == "🚀", "Emoji Search default action should copy the emoji");
var heart = await router.QueryAsync("emoji heart", CancellationToken.None);
Require(heart.Any(r => r.Result.PluginId == EmojiPlugin.PluginId && r.Result.Title.Contains("heart", StringComparison.OrdinalIgnoreCase)), "Emoji Search should match aliases and names");
var coffee = await router.QueryAsync("emoji coffee", CancellationToken.None);
Require(coffee.Any(r => r.Result.PluginId == EmojiPlugin.PluginId && r.Result.Data.TryGetValue("emoji", out var coffeeValue) && coffeeValue == "\u2615"), "Emoji Search should match common aliases from Unicode data");
var lightBlueHeart = await router.QueryAsync("emoji light blue heart", CancellationToken.None);
Require(lightBlueHeart.Any(r => r.Result.PluginId == EmojiPlugin.PluginId && r.Result.Data.TryGetValue("emoji", out var lightBlueHeartValue) && lightBlueHeartValue == "\U0001FA75"), "Emoji Search should include the full Unicode emoji set");

var translation = await router.QueryAsync("tr en zh HELLO Weed", CancellationToken.None);
var translationTarget = translation.FirstOrDefault(r => r.Result.PluginId == TranslatePlugin.PluginId && r.Result.Title == "你好 Weed");
Require(translationTarget is not null, "Translator should return a translated result");
Require(translationClient.LastRequest?.Text == "HELLO Weed", "keyword routing should preserve raw translation casing");
var translateCopy = await router.ExecuteAsync(translationTarget!.Result, translationTarget.Result.DefaultCommand, CancellationToken.None);
Require(translateCopy.Succeeded && host.Clipboard.Text == "你好 Weed", "Translator default action should copy translated text");
var translateSwap = await router.ExecuteAsync(translationTarget.Result, "translate.swap", CancellationToken.None);
Require(translateSwap.Succeeded && translateSwap.Behavior == CommandBehavior.ShowLauncher && translateSwap.InitialQuery == "tr zh en HELLO Weed", "Translator should create a swapped launcher query");
var translateAuto = await router.QueryAsync("translate auto en 你好", CancellationToken.None);
Require(translateAuto.Any(r => r.Result.PluginId == TranslatePlugin.PluginId && r.Result.Title == "[en] 你好"), "Translator should support the translate alias and auto source language");
var translateDefaultTarget = await router.QueryAsync("tr hello", CancellationToken.None);
Require(translateDefaultTarget.Any(r => r.Result.PluginId == TranslatePlugin.PluginId && r.Result.Title == "[zh-CN] hello"),
    "Translator should send unlabeled text to the default target language");
var translateChineseTarget = await router.QueryAsync("tr \u4F60\u597D", CancellationToken.None);
Require(translateChineseTarget.Any(r => r.Result.PluginId == TranslatePlugin.PluginId && r.Result.Title == "[en] \u4F60\u597D"),
    "Translator should send text detected as the default target language to the secondary target language");
Require(translationClient.LastRequest?.SourceLanguage == "zh-CN" && translationClient.LastRequest.TargetLanguage == "en",
    "Translator should use detected source and secondary target languages");
settings.SetPluginSetting(TranslatePlugin.PluginId, "defaultTargetLanguage", "en");
settings.SetPluginSetting(TranslatePlugin.PluginId, "secondaryTargetLanguage", "ja");
var translateAlreadyDefaultTarget = await router.QueryAsync("tr hello", CancellationToken.None);
Require(translateAlreadyDefaultTarget.Any(r => r.Result.PluginId == TranslatePlugin.PluginId && r.Result.Title == "[ja] hello"),
    "Translator should use the secondary target when detected text already matches the configured default target language");
settings.SetPluginSetting(TranslatePlugin.PluginId, "defaultTargetLanguage", "zh-CN");
settings.SetPluginSetting(TranslatePlugin.PluginId, "secondaryTargetLanguage", "en");
settings.SetPluginSetting(TranslatePlugin.PluginId, "queryDelayMilliseconds", 250);
var requestCountBeforeCancel = translationClient.RequestCount;
using (var translateCancel = new CancellationTokenSource(20))
{
    try
    {
        await router.QueryAsync("tr debounce", translateCancel.Token);
        Require(false, "Translator debounce delay should observe cancellation");
    }
    catch (OperationCanceledException)
    {
    }
}

Require(translationClient.RequestCount == requestCountBeforeCancel, "Translator should not call the API when a delayed query is canceled");
settings.SetPluginSetting(TranslatePlugin.PluginId, "queryDelayMilliseconds", 0);
var translateError = await router.QueryAsync("tr en zh fail quota", CancellationToken.None);
Require(translateError.Any(r => r.Result.PluginId == TranslatePlugin.PluginId && r.Result.Id.StartsWith("translate-error-", StringComparison.Ordinal)), "Translator should show provider errors as results");

var fileResults = await router.QueryAsync("file report", CancellationToken.None);
var reportFile = fileResults.FirstOrDefault(r => r.Result.PluginId == FileSearchPlugin.PluginId && r.Result.Data.TryGetValue("path", out var path) && path.EndsWith("Quarterly Report.pdf", StringComparison.OrdinalIgnoreCase));
Require(reportFile is not null, "File Search should return Everything file results");
Require(reportFile!.Result.Icon?.Path?.EndsWith(Path.Combine("assets", "plugins", "file-search.png"), StringComparison.OrdinalIgnoreCase) == true, "File Search should use the magnifying-glass icon asset");
Require(everythingClient.LastQuery == "report", "File Search should pass the query to Everything");
Require(everythingClient.LastSettings?.Sort.Value == EverythingSortOption.NameAscending.Value, "File Search should use Everything name ascending sort by default");
settings.SetPluginSetting(FileSearchPlugin.PluginId, "sort", EverythingSortOption.RunCountDescending.Value);
await router.QueryAsync("file report", CancellationToken.None);
Require(everythingClient.LastSettings?.Sort.SortType == EverythingSortOption.RunCountDescending.SortType, "File Search should pass the selected Everything sort to the SDK client");
var fileOpen = await router.ExecuteAsync(reportFile.Result, reportFile.Result.DefaultCommand, CancellationToken.None);
Require(fileOpen.Succeeded && host.Shell.OpenedPath == @"C:\Reports\Quarterly Report.pdf", "File Search default action should open the selected path");
await router.ExecuteAsync(reportFile.Result, "file.copyPath", CancellationToken.None);
Require(host.Shell.CopiedPath == @"C:\Reports\Quarterly Report.pdf", "File Search should copy paths through the shell service");
everythingClient.FailNext = true;
var fileDiagnostic = await router.QueryAsync("file unavailable", CancellationToken.None);
Require(fileDiagnostic.Any(r => r.Result.PluginId == FileSearchPlugin.PluginId && r.Result.Id == "file-search-unavailable"), "File Search should return a diagnostic result when Everything is unavailable");
var emojiKeywordKey = ActivationSettings.KeywordSettingKey(EmojiPlugin.Manifest.Activations.First(a => a.Keyword == "emoji"));
settings.SetPluginSetting(EmojiPlugin.PluginId, emojiKeywordKey, "emo");
var oldEmojiKeyword = await router.QueryAsync("emoji rocket", CancellationToken.None);
Require(!oldEmojiKeyword.Any(r => r.Result.PluginId == EmojiPlugin.PluginId), "custom keyword should replace the default keyword");
var customEmojiKeyword = await router.QueryAsync("emo rocket", CancellationToken.None);
Require(customEmojiKeyword.Any(r => r.Result.PluginId == EmojiPlugin.PluginId && r.Result.Data.TryGetValue("emoji", out var customEmoji) && customEmoji == "🚀"), "custom keyword should invoke the plugin");
settings.SetPluginSetting(EmojiPlugin.PluginId, emojiKeywordKey, "emoji");

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
var appIdentity = AppLauncherPlugin.CreateLaunchIdentity(@"C:\Menu\User\Foo.lnk", new ShortcutInfo { TargetPath = @"D:\Apps\Foo.exe" });
var duplicateAppIdentity = AppLauncherPlugin.CreateLaunchIdentity(@"C:\Menu\Common\Foo.lnk", new ShortcutInfo { TargetPath = @"d:\apps\foo.exe" });
var profiledAppIdentity = AppLauncherPlugin.CreateLaunchIdentity(@"C:\Menu\Common\Foo Work.lnk", new ShortcutInfo { TargetPath = @"D:\Apps\Foo.exe", Arguments = "--profile work" });
Require(appIdentity == duplicateAppIdentity, "AppLauncher should deduplicate shortcuts that target the same executable");
Require(appIdentity != profiledAppIdentity, "AppLauncher should keep shortcuts with different launch arguments");
var packagedAppEntry = AppLauncherPlugin.CreatePackagedAppEntry("Codex", "OpenAI.Codex_2p2nqsd0c76g0!App");
Require(AppLauncherPlugin.Score(packagedAppEntry, "codex") > 0, "AppLauncher should match packaged Windows apps");
Require(packagedAppEntry.ShortcutPath == @"shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App", "AppLauncher should launch packaged apps through AppsFolder");
Require(AppLauncherPlugin.CreateLaunchIdentity(packagedAppEntry.ShortcutPath, null) == "appx:OPENAI.CODEX_2P2NQSD0C76G0!APP", "AppLauncher should give packaged apps a stable launch identity");
var startupDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
if (!string.IsNullOrWhiteSpace(startupDirectory))
{
    Require(AppLauncherPlugin.IsStartupShortcut(Path.Combine(startupDirectory, "Foo.lnk")), "AppLauncher should skip user Startup shortcuts");
}

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
Require(clipboardSettings.Count == 6 &&
        clipboardSettings.Any(s => s.Key == "captureImages") &&
        clipboardSettings.Any(s => s.Key == "captureFileLists") &&
        clipboardSettings.Any(s => s.Key == "retentionDays") &&
        clipboardSettings.Any(s => s.Key == "maxItems") &&
        clipboardSettings.Any(s => s.Key == "resultLimit") &&
        clipboardSettings.Any(s => s.Key == "maxObjectMegabytes"), "Clipboard should expose capture, retention, result limit, and object quota settings");
Require(((IPluginSettingsProvider)calculatorPlugin).GetSettings().Count == 0, "Calculator should not expose visible plugin settings");
var screenshotSettings = ((IPluginSettingsProvider)screenshotPlugin).GetSettings();
Require(screenshotSettings.Count == 6 &&
        screenshotSettings.Any(s => s.Key == "defaultSaveDirectory") &&
        screenshotSettings.Any(s => s.Key == "defaultFormat") &&
        screenshotSettings.Any(s => s.Key == "jpegQuality") &&
        screenshotSettings.Any(s => s.Key == "maxSavedFileMegabytes") &&
        screenshotSettings.Any(s => s.Key == "defaultColor") &&
        screenshotSettings.Any(s => s.Key == "defaultLineWidth"), "Screenshot should expose save and annotation defaults");
var emojiSettings = ((IPluginSettingsProvider)emojiPlugin).GetSettings();
Require(emojiSettings.Count == 2 &&
        emojiSettings.Any(s => s.Key == "maxResults") &&
        emojiSettings.Any(s => s.Key == "copyFormat"), "Emoji Search should expose result limit and copy format settings");
var translateSettings = ((IPluginSettingsProvider)translatePlugin).GetSettings();
Require(translateSettings.Any(s => s.Key == "provider") &&
        translateSettings.Any(s => s.Key == "defaultSourceLanguage") &&
        translateSettings.Any(s => s.Key == "defaultTargetLanguage") &&
        translateSettings.Any(s => s.Key == "secondaryTargetLanguage") &&
        translateSettings.Any(s => s.Key == "queryDelayMilliseconds") &&
        translateSettings.Any(s => s.Key == "proxyMode") &&
        translateSettings.Any(s => s.Key == "proxyUrl") &&
        translateSettings.Any(s => s.Key == "baiduAppId") &&
        translateSettings.Any(s => s.Key == "baiduSecretKey"), "Translator should expose provider, language, credential, and proxy settings");
var fileSearchSettings = ((IPluginSettingsProvider)fileSearchPlugin).GetSettings();
Require(fileSearchSettings.Count == 3 &&
        fileSearchSettings.Any(s => s.Key == "includeFolders") &&
        fileSearchSettings.Any(s => s.Key == "maxResults") &&
        fileSearchSettings.Any(s => s.Key == "sort" && s.Kind == PluginSettingKind.Select && s.Options.Any(o => o.Value == EverythingSortOption.RunCountDescending.Value)), "File Search should expose Everything SDK result and sort settings without a user-entered executable path");
settings.SetPluginSetting(AppLauncherPlugin.PluginId, "hideMaintenanceShortcuts", false);
Require(settings.GetPluginSetting(AppLauncherPlugin.PluginId, "hideMaintenanceShortcuts", true) == false, "plugin settings should persist typed values");
settings.SetPluginSetting(ScreenshotPlugin.PluginId, "defaultColor", "Blue");
Require(settings.GetPluginSetting(ScreenshotPlugin.PluginId, "defaultColor", "Red") == "Blue", "screenshot color setting should persist for the next capture");
Require(SettingsWindow.ShouldShowPriorityControl(AppLauncherPlugin.Manifest), "AppLauncher should expose plugin priority because it supports implicit query");
Require(SettingsWindow.ShouldShowPriorityControl(CalculatorPlugin.Manifest), "Calculator should expose plugin priority because it supports implicit query");
Require(SettingsWindow.ShouldShowPriorityControl(RunCommandPlugin.Manifest), "Run Command should expose plugin priority because it supports implicit query");
Require(!SettingsWindow.ShouldShowPriorityControl(ClipboardPlugin.Manifest), "Clipboard should not expose plugin priority because it has no implicit query activation");
Require(!SettingsWindow.ShouldShowPriorityControl(ScreenshotPlugin.Manifest), "Screenshot should not expose plugin priority because it has no implicit query activation");
Require(!SettingsWindow.ShouldShowPriorityControl(EmojiPlugin.Manifest), "Emoji Search should not expose plugin priority because it has no implicit query activation");
Require(!SettingsWindow.ShouldShowPriorityControl(TranslatePlugin.Manifest), "Translator should not expose plugin priority because it has no implicit query activation");
Require(!SettingsWindow.ShouldShowPriorityControl(FileSearchPlugin.Manifest), "File Search should not expose plugin priority because it has no implicit query activation");

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
var firstVisibleResult = launcherViewModel.Results[0];
launcherViewModel.EnsureDisplayedThrough(20, 20);
Require(launcherViewModel.DisplayedResultCount == 40 && launcherViewModel.Results.Count == 40, "launcher should auto-load the next page when selection moves past the displayed results");
Require(ReferenceEquals(firstVisibleResult, launcherViewModel.Results[0]), "launcher should append load-more results without rebuilding existing rows");
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

var windowTargets = ScreenshotOverlayLayout.WindowTargetsFromScreenBounds(
    [
        new System.Drawing.Rectangle(10, 10, 120, 100),
        new System.Drawing.Rectangle(40, 40, 160, 120)
    ],
    new System.Drawing.Rectangle(0, 0, 400, 300));
var windowHit = ScreenshotOverlayLayout.HitTestWindowTarget(
    windowTargets,
    new System.Windows.Point(50, 50),
    new System.Windows.Size(400, 300),
    new System.Windows.Size(400, 300));
Require(windowHit?.PixelBounds == new System.Drawing.Rectangle(10, 10, 120, 100), "window selection should prefer the topmost matching window");

var clippedWindowTargets = ScreenshotOverlayLayout.WindowTargetsFromScreenBounds(
    [new System.Drawing.Rectangle(-450, 10, 200, 100)],
    new System.Drawing.Rectangle(-400, 0, 800, 500));
Require(clippedWindowTargets.Count == 1 &&
        clippedWindowTargets[0].PixelBounds == new System.Drawing.Rectangle(0, 10, 150, 100), "window selection should clip negative-coordinate windows to the virtual screen");
var clippedWindowHit = ScreenshotOverlayLayout.HitTestWindowTarget(
    clippedWindowTargets,
    new System.Windows.Point(12, 20),
    new System.Windows.Size(800, 500),
    new System.Windows.Size(800, 500));
Require(clippedWindowHit is not null, "window selection should hit clipped multi-monitor window bounds");

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

using (var large = new System.Drawing.Bitmap(420, 420, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
{
    var random = new Random(12345);
    for (var y = 0; y < large.Height; y++)
    {
        for (var x = 0; x < large.Width; x++)
        {
            large.SetPixel(x, y, System.Drawing.Color.FromArgb(random.Next(256), random.Next(256), random.Next(256)));
        }
    }

    using var largeStream = new MemoryStream();
    large.Save(largeStream, System.Drawing.Imaging.ImageFormat.Png);
    var compressed = ScreenshotImageEncoder.EncodeForSave(
        largeStream.ToArray(),
        Path.Combine(root, "large-screenshot.png"),
        90,
        120_000);
    Require(compressed.Bytes.LongLength <= 120_000, "screenshot encoder should compress large images under the configured threshold");
    Require(Path.GetExtension(compressed.FilePath).Equals(".jpg", StringComparison.OrdinalIgnoreCase), "screenshot encoder should convert oversized PNG saves to JPEG");
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

static async Task VerifySingleInstanceAsync()
{
    var instanceId = $"Smoke.{Guid.NewGuid():N}";
    await using var primary = new SingleInstanceCoordinator(instanceId);
    Require(primary.IsPrimary, "the first coordinator should own the application instance");

    var activation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    primary.ActivationRequested += () => activation.TrySetResult();
    primary.StartListening();

    await using var secondary = new SingleInstanceCoordinator(instanceId);
    Require(!secondary.IsPrimary, "a second coordinator should be rejected");
    await secondary.NotifyPrimaryAsync();
    await activation.Task.WaitAsync(TimeSpan.FromSeconds(3));
}

static async Task VerifyExternalPluginImportAsync(
    string root,
    AppPaths paths,
    SmokeHost host,
    ConsoleWeedLogger logger)
{
    var source = Path.Combine(root, "external-plugin-source");
    Directory.CreateDirectory(source);
    await File.WriteAllTextAsync(Path.Combine(source, "manifest.json"), """
    {
      "id": "example.external",
      "name": "Example External",
      "version": "0.1.0",
      "sdkVersion": "0.1",
      "assembly": "Example.External.dll",
      "entryType": "Example.External.Plugin",
      "activations": []
    }
    """);
    await File.WriteAllTextAsync(Path.Combine(source, "Example.External.dll"), string.Empty);

    var importer = new ExternalPluginImporter();
    var folderResult = await importer.ImportAsync(source, paths.Plugins, overwrite: false, CancellationToken.None);
    Require(folderResult.Succeeded, "external plugin folder import should succeed");
    Require(File.Exists(Path.Combine(paths.Plugins, "example.external", "manifest.json")), "external plugin import should copy the manifest");

    var duplicateResult = await importer.ImportAsync(source, paths.Plugins, overwrite: false, CancellationToken.None);
    Require(!duplicateResult.Succeeded, "external plugin import should reject duplicates without replace");

    var dllPluginsRoot = Path.Combine(root, "dll-plugins");
    var dllResult = await importer.ImportAsync(Path.Combine(source, "Example.External.dll"), dllPluginsRoot, overwrite: false, CancellationToken.None);
    Require(dllResult.Succeeded, "external plugin DLL import should use the manifest next to the selected DLL");
    Require(File.Exists(Path.Combine(dllPluginsRoot, "example.external", "manifest.json")), "external plugin DLL import should copy the manifest");

    var zipPath = Path.Combine(root, "example-external.zip");
    ZipFile.CreateFromDirectory(source, zipPath);
    var zipPluginsRoot = Path.Combine(root, "zip-plugins");
    var zipResult = await importer.ImportAsync(zipPath, zipPluginsRoot, overwrite: false, CancellationToken.None);
    Require(zipResult.Succeeded, "external plugin ZIP import should succeed");
    Require(File.Exists(Path.Combine(zipPluginsRoot, "example.external", "manifest.json")), "external plugin ZIP import should copy the manifest");

    var sourceProject = Path.Combine(root, "external-plugin-project");
    Directory.CreateDirectory(sourceProject);
    await File.WriteAllTextAsync(Path.Combine(sourceProject, "manifest.json"), """
    {
      "id": "source.external",
      "name": "Source External",
      "version": "0.1.0",
      "sdkVersion": "0.1",
      "assembly": "Source.External.dll",
      "entryType": "Source.External.Plugin",
      "activations": []
    }
    """);
    await File.WriteAllTextAsync(Path.Combine(sourceProject, "Source.External.csproj"), $$"""
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <TargetFramework>net9.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
      </PropertyGroup>
      <ItemGroup>
        <Reference Include="Weed.Abstractions">
          <HintPath>{{Path.Combine(AppContext.BaseDirectory, "Weed.Abstractions.dll")}}</HintPath>
          <Private>false</Private>
        </Reference>
      </ItemGroup>
      <ItemGroup>
        <Content Include="manifest.json">
          <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
          <CopyToPublishDirectory>Always</CopyToPublishDirectory>
        </Content>
      </ItemGroup>
    </Project>
    """);
    await File.WriteAllTextAsync(Path.Combine(sourceProject, "Plugin.cs"), """
    using Weed.Abstractions;

    namespace Source.External;

    public sealed class Plugin : IWeedPlugin
    {
        public ValueTask InitializeAsync(IWeedHost host, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    """);
    var sourcePluginsRoot = Path.Combine(root, "source-plugins");
    var sourceResult = await importer.ImportAsync(sourceProject, sourcePluginsRoot, overwrite: false, CancellationToken.None);
    Require(sourceResult.Succeeded, "external plugin source folder import should publish and copy the plugin");
    Require(File.Exists(Path.Combine(sourcePluginsRoot, "source.external", "Source.External.dll")), "external plugin source import should publish the assembly");

    var registrySource = Path.Combine(root, "registry-plugin-source");
    CopyDirectoryForSmoke(source, registrySource);
    var registryManifestPath = Path.Combine(registrySource, "manifest.json");
    var registryManifest = (await File.ReadAllTextAsync(registryManifestPath))
        .Replace("\"version\": \"0.1.0\"", "\"version\": \"0.2.0\"", StringComparison.Ordinal);
    await File.WriteAllTextAsync(registryManifestPath, registryManifest);
    var registryPackagePath = Path.Combine(root, "example-external-0.2.zip");
    ZipFile.CreateFromDirectory(registrySource, registryPackagePath);
    var registryHash = ExternalPluginRegistryService.Sha256File(registryPackagePath);
    var registryPath = Path.Combine(root, "plugin-registry.json");
    await File.WriteAllTextAsync(registryPath, $$"""
    {
      "schemaVersion": "1",
      "plugins": [
        {
          "id": "example.external",
          "name": "Example External",
          "description": "Smoke test plugin",
          "version": "0.2.0",
          "sdkVersion": "0.1",
          "minWeedVersion": "0.1.0",
          "packageUrl": "example-external-0.2.zip",
          "sha256": "{{registryHash}}",
          "repositoryUrl": "https://github.com/example/example.external",
          "releaseNotesUrl": "https://github.com/example/example.external/releases/tag/v0.2.0",
          "trusted": true,
          "tags": ["smoke"]
        }
      ]
    }
    """);

    var registryService = new ExternalPluginRegistryService();
    var stableRegistry = await registryService.ReadRegistryAsync(
        Path.Combine(Environment.CurrentDirectory, "plugins.registry.json"),
        CancellationToken.None);
    Require(stableRegistry.Plugins.Any(plugin =>
            plugin.Id == ToolboxPlugin.PluginId &&
            plugin.PackageUrl.Contains("toolbox-v0.1.0", StringComparison.Ordinal) &&
            plugin.Sha256.Length == 64),
        "stable plugin registry should publish the verified Toolbox release");

    var registry = await registryService.ReadRegistryAsync(registryPath, CancellationToken.None);
    Require(registry.Plugins.Count == 1 && registry.Plugins[0].Id == "example.external", "external plugin registry should parse plugin entries");
    var plans = registryService.BuildInstallPlans(registry, paths.Plugins);
    Require(plans.Count == 1 && plans[0].State == ExternalPluginInstallState.UpdateAvailable, "external plugin registry should detect update availability");

    var registryInstallRoot = Path.Combine(root, "registry-plugins");
    var registryInstall = await registryService.DownloadAndImportAsync(
        registry.Plugins[0],
        registryPath,
        registryInstallRoot,
        Path.Combine(root, "registry-downloads"),
        CancellationToken.None);
    Require(registryInstall.Succeeded, "external plugin registry install should download, verify, and import a ZIP");
    Require(File.Exists(Path.Combine(registryInstallRoot, "example.external", "manifest.json")), "external plugin registry install should copy the manifest");

    var mismatchedPackage = await registryService.DownloadAndImportAsync(
        registry.Plugins[0] with { Id = "different.external" },
        registryPath,
        Path.Combine(root, "mismatched-registry-plugins"),
        Path.Combine(root, "mismatched-registry-downloads"),
        CancellationToken.None);
    Require(!mismatchedPackage.Succeeded && mismatchedPackage.Message.Contains("does not match registry id", StringComparison.Ordinal),
        "external plugin registry should reject packages whose manifest id differs from the registry");

    var mismatchedVersion = await registryService.DownloadAndImportAsync(
        registry.Plugins[0] with { Version = "0.3.0" },
        registryPath,
        Path.Combine(root, "mismatched-version-plugins"),
        Path.Combine(root, "mismatched-version-downloads"),
        CancellationToken.None);
    Require(!mismatchedVersion.Succeeded && mismatchedVersion.Message.Contains("does not match registry version", StringComparison.Ordinal),
        "external plugin registry should reject packages whose manifest version differs from the registry");

    var badHashInstall = await registryService.DownloadAndImportAsync(
        registry.Plugins[0] with { Sha256 = new string('0', 64) },
        registryPath,
        Path.Combine(root, "bad-registry-plugins"),
        Path.Combine(root, "bad-registry-downloads"),
        CancellationToken.None);
    Require(!badHashInstall.Succeeded, "external plugin registry install should reject SHA256 mismatches");

    var incompatibleRegistry = new ExternalPluginRegistry
    {
        Plugins =
        [
            registry.Plugins[0] with
            {
                Id = "example.incompatible",
                Name = "Example Incompatible",
                MinWeedVersion = "999.0.0"
            }
        ]
    };
    var incompatiblePlans = registryService.BuildInstallPlans(incompatibleRegistry, paths.Plugins);
    Require(incompatiblePlans.Count == 1 && incompatiblePlans[0].State == ExternalPluginInstallState.Incompatible, "external plugin registry should detect incompatible plugin versions");

    var uninstaller = new ExternalPluginUninstaller();
    var outsideResult = await uninstaller.UninstallAsync(
        "example.external",
        source,
        paths.Plugins,
        CancellationToken.None);
    Require(!outsideResult.Succeeded, "external plugin uninstall should reject directories outside the plugin root");

    var uninstallResult = await uninstaller.UninstallAsync(
        "example.external",
        Path.Combine(paths.Plugins, "example.external"),
        paths.Plugins,
        CancellationToken.None);
    Require(uninstallResult.Succeeded && uninstallResult.RestartRequired,
        "external plugin uninstall should remove a validated installed package");
    Require(!Directory.Exists(Path.Combine(paths.Plugins, "example.external")),
        "external plugin uninstall should remove the installed plugin directory");

    var pendingRoot = Path.Combine(root, "pending-uninstall-plugins");
    var pendingPlugin = Path.Combine(pendingRoot, "pending.external");
    Directory.CreateDirectory(pendingPlugin);
    await File.WriteAllTextAsync(Path.Combine(pendingPlugin, ExternalPluginUninstaller.PendingRemovalMarker), "pending");
    await using (var runtime = new PluginRuntime(host, logger))
    {
        await runtime.ScanDirectoryAsync(pendingRoot, CancellationToken.None);
    }
    Require(!Directory.Exists(pendingPlugin), "plugin startup should clean pending uninstall directories");
}

static void CopyDirectoryForSmoke(string source, string destination)
{
    Directory.CreateDirectory(destination);
    foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
    {
        Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
    }

    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        var target = Path.Combine(destination, Path.GetRelativePath(source, file));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file, target, overwrite: true);
    }
}

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

static async Task VerifyToolboxAsync(
    QueryRouter router,
    SettingsRepository settings,
    SmokeHost host,
    ConsoleWeedLogger logger)
{
    var unrelated = await router.QueryAsync("definitely-not-a-toolbox-command", CancellationToken.None);
    Require(!unrelated.Any(result => result.Result.PluginId == ToolboxPlugin.PluginId),
        "Toolbox should reject unrelated implicit queries");

    var uuid = (await router.QueryAsync("uuid", CancellationToken.None))
        .Single(result => result.Result.PluginId == ToolboxPlugin.PluginId);
    Require(Guid.TryParse(uuid.Result.Title, out _), "Toolbox uuid should generate a valid UUID");
    var secondUuid = (await router.QueryAsync("uuid", CancellationToken.None))
        .Single(result => result.Result.PluginId == ToolboxPlugin.PluginId);
    Require(uuid.Result.Title != secondUuid.Result.Title, "Toolbox uuid should generate a new value for a new query");
    var uuidArguments = (await router.QueryAsync("uuid 5", CancellationToken.None))
        .Single(result => result.Result.PluginId == ToolboxPlugin.PluginId);
    Require(uuidArguments.Result.DefaultCommand == "__noop", "Toolbox uuid should reject arguments");

    var timestamps = (await router.QueryAsync("timestamp", CancellationToken.None))
        .Where(result => result.Result.PluginId == ToolboxPlugin.PluginId)
        .ToArray();
    Require(timestamps.Length == 2, "Toolbox timestamp should return millisecond and second values");
    Require(timestamps[0].Result.Id == "timestamp.current.milliseconds" && timestamps[0].Result.MatchScore == 30,
        "Toolbox timestamp should rank milliseconds first");
    Require(timestamps[1].Result.Id == "timestamp.current.seconds" && timestamps[1].Result.MatchScore == 27,
        "Toolbox timestamp should rank seconds second");
    Require(long.Parse(timestamps[0].Result.Title) / 1000 == long.Parse(timestamps[1].Result.Title),
        "Toolbox timestamp values should come from the same instant");

    var timestampToDate = (await router.QueryAsync("timestamp 1783843200000", CancellationToken.None))
        .Where(result => result.Result.PluginId == ToolboxPlugin.PluginId)
        .ToArray();
    Require(timestampToDate.Length == 2 && timestampToDate[0].Result.Id == "timestamp.toDate.local" &&
            timestampToDate[1].Result.Id == "timestamp.toDate.utc",
        "Toolbox should convert Unix timestamps to local and UTC time");
    var dateToTimestamp = (await router.QueryAsync("timestamp 2026-07-12T08:00:00Z", CancellationToken.None))
        .Where(result => result.Result.PluginId == ToolboxPlugin.PluginId)
        .ToArray();
    Require(dateToTimestamp.Length == 2 && dateToTimestamp[0].Result.Id == "timestamp.fromDate.milliseconds",
        "Toolbox should convert ISO dates to Unix timestamps");
    var invalidTimestamp = (await router.QueryAsync("timestamp 123", CancellationToken.None))
        .Single(result => result.Result.PluginId == ToolboxPlugin.PluginId);
    Require(invalidTimestamp.Result.DefaultCommand == "__noop", "Toolbox should reject timestamps with invalid digit counts");

    var base64Menu = (await router.QueryAsync("base64", CancellationToken.None))
        .Where(result => result.Result.PluginId == ToolboxPlugin.PluginId)
        .ToArray();
    Require(base64Menu.Length == 2 && base64Menu[0].Result.Title == "Encode" && base64Menu[1].Result.Title == "Decode",
        "Toolbox base64 should show an ordered operation menu");
    var base64Selection = await router.ExecuteAsync(
        base64Menu[0].Result,
        base64Menu[0].Result.DefaultCommand,
        CancellationToken.None);
    Require(base64Selection.Behavior == CommandBehavior.ShowLauncher && base64Selection.InitialQuery == "base64 encode ",
        "Toolbox operation menus should continue with a completed query");

    var encoded = (await router.QueryAsync("base64 encode \u4f60\u597d", CancellationToken.None))
        .Single(result => result.Result.PluginId == ToolboxPlugin.PluginId);
    Require(encoded.Result.Title == "5L2g5aW9", "Toolbox should Base64-encode UTF-8 text");
    var decoded = (await router.QueryAsync("base64 decode 5L2g5aW9", CancellationToken.None))
        .Single(result => result.Result.PluginId == ToolboxPlugin.PluginId);
    Require(decoded.Result.Title == "\u4f60\u597d", "Toolbox should Base64-decode UTF-8 text");
    var invalidBase64 = (await router.QueryAsync("base64 decode !!!", CancellationToken.None))
        .Single(result => result.Result.PluginId == ToolboxPlugin.PluginId);
    Require(invalidBase64.Result.DefaultCommand == "__noop", "Toolbox should report invalid Base64 input");

    var urlEncoded = (await router.QueryAsync("url encode a+b c", CancellationToken.None))
        .Single(result => result.Result.PluginId == ToolboxPlugin.PluginId);
    Require(urlEncoded.Result.Title == "a%2Bb%20c", "Toolbox should use percent encoding for URL components");
    var urlDecoded = (await router.QueryAsync("url decode a%2Bb%20c", CancellationToken.None))
        .Single(result => result.Result.PluginId == ToolboxPlugin.PluginId);
    Require(urlDecoded.Result.Title == "a+b c", "Toolbox should decode URL components");

    var sha256 = (await router.QueryAsync("hash sha256 hello", CancellationToken.None))
        .Single(result => result.Result.PluginId == ToolboxPlugin.PluginId);
    Require(sha256.Result.Title == "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
        "Toolbox should calculate SHA-256 using UTF-8");
    var hashMenu = (await router.QueryAsync("hash", CancellationToken.None))
        .Where(result => result.Result.PluginId == ToolboxPlugin.PluginId)
        .ToArray();
    Require(hashMenu.Select(result => result.Result.Title).SequenceEqual(["SHA-256", "SHA-512", "SHA-1", "MD5"]),
        "Toolbox should order hash operations consistently");

    var formattedJson = (await router.QueryAsync("json format {\"b\":2,\"a\":1}", CancellationToken.None))
        .Single(result => result.Result.PluginId == ToolboxPlugin.PluginId);
    Require(formattedJson.Result.Title.Contains(Environment.NewLine, StringComparison.Ordinal),
        "Toolbox should format JSON with indentation");
    var minifiedJson = (await router.QueryAsync("json minify { \"b\": 2, \"a\": 1 }", CancellationToken.None))
        .Single(result => result.Result.PluginId == ToolboxPlugin.PluginId);
    Require(minifiedJson.Result.Title == "{\"b\":2,\"a\":1}", "Toolbox should minify JSON");

    await router.ExecuteAsync(encoded.Result, "toolbox.copy", CancellationToken.None);
    Require(host.Clipboard.Text == "5L2g5aW9", "Toolbox copy should write the result to the clipboard");
    await router.ExecuteAsync(decoded.Result, "toolbox.paste", CancellationToken.None);
    Require(host.Clipboard.Text == "\u4f60\u597d", "Toolbox paste should paste the result text");

    settings.SetPluginSetting(ToolboxPlugin.PluginId, "timestampCommand", "ts");
    var configuredTimestamp = await router.QueryAsync("ts", CancellationToken.None);
    Require(configuredTimestamp.Count(result => result.Result.PluginId == ToolboxPlugin.PluginId) == 2,
        "Toolbox should apply configured tool names immediately");
    var oldTimestampName = await router.QueryAsync("timestamp", CancellationToken.None);
    Require(!oldTimestampName.Any(result => result.Result.PluginId == ToolboxPlugin.PluginId),
        "Toolbox should stop matching an old configured tool name");
    settings.SetPluginSetting(ToolboxPlugin.PluginId, "timestampCommand", "timestamp");

    settings.SetPluginSetting(ToolboxPlugin.PluginId, "base64Command", "b64");
    var configuredBase64Menu = (await router.QueryAsync("b64", CancellationToken.None))
        .First(result => result.Result.PluginId == ToolboxPlugin.PluginId);
    var configuredSelection = await router.ExecuteAsync(
        configuredBase64Menu.Result,
        configuredBase64Menu.Result.DefaultCommand,
        CancellationToken.None);
    Require(configuredSelection.InitialQuery == "b64 encode ",
        "Toolbox operation menus should use the configured tool name");
    settings.SetPluginSetting(ToolboxPlugin.PluginId, "base64Command", "base64");

    settings.SetPluginSetting(ToolboxPlugin.PluginId, "uuidCommand", "bad name");
    await router.QueryAsync("uuid", CancellationToken.None);
    await router.QueryAsync("uuid", CancellationToken.None);
    Require(logger.Warnings.Count(message => message.Contains("uuidCommand", StringComparison.Ordinal)) == 1,
        "Toolbox should warn only once for each invalid command setting");
    settings.SetPluginSetting(ToolboxPlugin.PluginId, "uuidCommand", "uuid");

    settings.SetPluginSetting(ToolboxPlugin.PluginId, "base64Command", "url");
    var conflict = (await router.QueryAsync("url", CancellationToken.None))
        .Single(result => result.Result.PluginId == ToolboxPlugin.PluginId);
    Require(conflict.Result.Id == "toolbox.error.configuration-conflict",
        "Toolbox should report duplicate configured command names");
    settings.SetPluginSetting(ToolboxPlugin.PluginId, "base64Command", "base64");
}

static bool Near(double actual, double expected) => Math.Abs(actual - expected) < 0.01;

public sealed class ConsoleWeedLogger : IWeedLogger
{
    public List<string> Warnings { get; } = [];

    public void Info(string message) => Console.WriteLine($"INFO {message}");

    public void Warn(string message)
    {
        Warnings.Add(message);
        Console.WriteLine($"WARN {message}");
    }

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

    public string? ContainingPath { get; private set; }

    public string? CopiedPath { get; private set; }

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

    public ValueTask OpenContainingFolderAsync(string path, CancellationToken cancellationToken)
    {
        ContainingPath = path;
        return ValueTask.CompletedTask;
    }

    public ValueTask CopyPathAsync(string path, CancellationToken cancellationToken)
    {
        CopiedPath = path;
        return ValueTask.CompletedTask;
    }
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

    public ValueTask<ScreenCaptureResult?> CaptureRegionRawAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<ScreenCaptureResult?>(null);

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

public sealed class SmokeTranslationClient : ITranslationClient
{
    public int RequestCount { get; private set; }

    public TranslationRequest? LastRequest { get; private set; }

    public TranslateSettings? LastSettings { get; private set; }

    public ValueTask<TranslationResponse> TranslateAsync(
        TranslationRequest request,
        TranslateSettings settings,
        CancellationToken cancellationToken)
    {
        RequestCount++;
        LastRequest = request;
        LastSettings = settings;
        if (request.Text.Contains("fail", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("fake quota exceeded");
        }

        var detectedSource = DetectSourceLanguage(request.Text);
        var translated = request.SourceLanguage.Equals("en", StringComparison.OrdinalIgnoreCase) &&
                         request.TargetLanguage.Equals("zh", StringComparison.OrdinalIgnoreCase) &&
                         request.Text.Equals("HELLO Weed", StringComparison.Ordinal)
            ? "你好 Weed"
            : $"[{request.TargetLanguage}] {request.Text}";
        return ValueTask.FromResult(new TranslationResponse(translated, "Smoke", detectedSource));
    }

    private static string DetectSourceLanguage(string text) =>
        text.Any(character =>
            character is >= '\u3400' and <= '\u4DBF' ||
            character is >= '\u4E00' and <= '\u9FFF' ||
            character is >= '\uF900' and <= '\uFAFF')
            ? "zh-CN"
            : "en";
}

public sealed class SmokeEverythingSearchClient : IEverythingSearchClient
{
    private readonly IReadOnlyList<EverythingSearchResult> _results;

    public SmokeEverythingSearchClient(IReadOnlyList<EverythingSearchResult> results)
    {
        _results = results;
    }

    public string? LastQuery { get; private set; }

    public FileSearchSettings? LastSettings { get; private set; }

    public bool FailNext { get; set; }

    public ValueTask<IReadOnlyList<EverythingSearchResult>> SearchAsync(
        string query,
        FileSearchSettings settings,
        CancellationToken cancellationToken)
    {
        LastQuery = query;
        LastSettings = settings;
        if (FailNext)
        {
            FailNext = false;
            throw new EverythingUnavailableException("Everything is not running.");
        }

        return ValueTask.FromResult(_results);
    }
}
