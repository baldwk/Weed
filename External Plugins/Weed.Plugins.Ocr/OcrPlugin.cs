using System.Security.Cryptography;
using System.Text;
using RapidOCRLib;
using Weed.Abstractions;
using IQueryProvider = Weed.Abstractions.IQueryProvider;

namespace Weed.Plugins.Ocr;

public sealed class OcrPlugin : IWeedPlugin, IQueryProvider, ICommandHandler, IPluginSettingsProvider
{
    public const string PluginId = "weed.ocr";
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".webp"];
    private readonly SemaphoreSlim _engineGate = new(1, 1);
    private OcrEngine? _engine;
    private OcrSettings? _engineSettings;
    private IWeedHost? _host;

    public string ProviderId => "ocr";

    public ValueTask InitializeAsync(IWeedHost host, CancellationToken cancellationToken)
    {
        _host = host;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _engineGate.Dispose();
        return ValueTask.CompletedTask;
    }

    public IReadOnlyList<PluginSettingDefinition> GetSettings() =>
    [
        new()
        {
            Key = "modelDirectory",
            Label = "Model directory",
            Kind = PluginSettingKind.Path,
            DefaultValue = string.Empty,
            Description = "Leave empty to use the plugin's bundled models folder."
        },
        new()
        {
            Key = "maxSideLen",
            Label = "Max side length",
            Kind = PluginSettingKind.Integer,
            DefaultValue = "1024",
            Min = 512,
            Max = 4096,
            Description = "Higher values improve small text at the cost of speed."
        },
        new()
        {
            Key = "padding",
            Label = "Padding",
            Kind = PluginSettingKind.Integer,
            DefaultValue = "50",
            Min = 0,
            Max = 200,
            Description = "Extra image padding before text detection."
        },
        new()
        {
            Key = "doAngle",
            Label = "Angle detection",
            Kind = PluginSettingKind.Boolean,
            DefaultValue = "true",
            Description = "Use the classifier model to fix rotated text."
        }
    ];

    public async ValueTask<IReadOnlyList<WeedResult>> QueryAsync(QueryContext context, CancellationToken cancellationToken)
    {
        if (_host is null)
        {
            return [];
        }

        var settings = OcrSettings.FromHost(_host.Settings);
        var input = context.RawText.Trim();
        if (input.Length == 0)
        {
            return DefaultResults(settings);
        }

        if (!TryParseImagePath(input, out var imagePath))
        {
            return
            [
                new WeedResult
                {
                    Id = "ocr-image-path-help",
                    PluginId = PluginId,
                    Title = "Recognize an image file",
                    Subtitle = "Use: ocr C:\\path\\to\\image.png",
                    Icon = WeedIcon.FromGlyph("OCR"),
                    MatchScore = 12,
                    DefaultCommand = "__noop",
                    Actions =
                    [
                        new WeedAction { Command = "__noop", Title = "Keep open", Shortcut = "Enter" }
                    ]
                }
            ];
        }

        if (!File.Exists(imagePath))
        {
            return [ImageMissingResult(imagePath)];
        }

        if (!IsSupportedImage(imagePath))
        {
            return [UnsupportedImageResult(imagePath)];
        }

        var missing = settings.MissingModelFiles();
        if (missing.Count > 0)
        {
            return [ModelsMissingResult(settings, missing)];
        }

        try
        {
            var engine = await GetEngineAsync(settings, cancellationToken);
            var text = await engine.RecognizeAsync(imagePath, settings, cancellationToken);
            return [OcrTextResult(imagePath, text)];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _host.Logger.Warn($"OCR failed for {imagePath}: {ex.Message}");
            return [ErrorResult(imagePath, ex.Message)];
        }
    }

    public async ValueTask<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (_host is null)
        {
            return CommandResult.Failed("OCR is not initialized.");
        }

        return context.Command switch
        {
            "ocr.captureRegion" => await CaptureRegionAsync(cancellationToken),
            "ocr.copyText" => await CopyTextAsync(context, cancellationToken),
            "ocr.openTextFile" => await OpenTextFileAsync(context, cancellationToken),
            "ocr.openImage" => await OpenImageAsync(context, cancellationToken),
            "ocr.openImageFolder" => await OpenImageFolderAsync(context, cancellationToken),
            "ocr.openModelDirectory" => await OpenModelDirectoryAsync(context, cancellationToken),
            "__noop" => CommandResult.Ok(CommandBehavior.KeepLauncherOpen),
            _ => CommandResult.Failed($"Unknown OCR command: {context.Command}")
        };
    }

    private IReadOnlyList<WeedResult> DefaultResults(OcrSettings settings)
    {
        var missing = settings.MissingModelFiles();
        var results = new List<WeedResult>
        {
            new()
            {
                Id = "ocr-capture-region",
                PluginId = PluginId,
                Title = "Capture region and recognize text",
                Subtitle = "Select an area, then OCR the saved image",
                Icon = WeedIcon.FromGlyph("OCR"),
                MatchScore = missing.Count == 0 ? 28 : 16,
                DefaultCommand = "ocr.captureRegion",
                Actions =
                [
                    new WeedAction { Command = "ocr.captureRegion", Title = "Capture region", Shortcut = "Enter" }
                ]
            },
            new()
            {
                Id = "ocr-image-path",
                PluginId = PluginId,
                Title = "Recognize an image file",
                Subtitle = "Type: ocr C:\\path\\to\\image.png",
                Icon = WeedIcon.FromGlyph("OCR"),
                MatchScore = 18,
                DefaultCommand = "__noop",
                Actions =
                [
                    new WeedAction { Command = "__noop", Title = "Keep open", Shortcut = "Enter" }
                ]
            }
        };

        if (missing.Count > 0)
        {
            results.Insert(0, ModelsMissingResult(settings, missing));
        }

        return results;
    }

    private async ValueTask<CommandResult> CaptureRegionAsync(CancellationToken cancellationToken)
    {
        var settings = OcrSettings.FromHost(_host!.Settings);
        var missing = settings.MissingModelFiles();
        if (missing.Count > 0)
        {
            return CommandResult.Failed($"OCR model files are missing: {string.Join(", ", missing)}");
        }

        var capture = await _host.ScreenCapture.CaptureRegionRawAsync(cancellationToken);
        if (capture?.ImagePng is null || capture.ImagePng.Length == 0)
        {
            return CommandResult.Ok(CommandBehavior.KeepLauncherOpen, "OCR capture cancelled.");
        }

        var imagePath = CaptureImagePath();
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        await File.WriteAllBytesAsync(imagePath, capture.ImagePng, cancellationToken);
        return new CommandResult
        {
            Succeeded = true,
            Behavior = CommandBehavior.ShowLauncher,
            InitialQuery = $"ocr \"{imagePath}\"",
            Message = "Captured region."
        };
    }

    private async ValueTask<CommandResult> OpenTextFileAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (!context.Data.TryGetValue("ocrText", out var text))
        {
            return CommandResult.Failed("OCR text is missing.");
        }

        var sourcePath = context.Data.GetValueOrDefault("sourcePath", "image");
        var textPath = TextResultPath(sourcePath, text);
        Directory.CreateDirectory(Path.GetDirectoryName(textPath)!);
        await File.WriteAllTextAsync(textPath, text, Encoding.UTF8, cancellationToken);
        await _host!.Shell.OpenAsync(textPath, cancellationToken);
        return CommandResult.Ok(message: "Opened OCR text.");
    }

    private async ValueTask<CommandResult> CopyTextAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (!context.Data.TryGetValue("ocrText", out var text))
        {
            return CommandResult.Failed("OCR text is missing.");
        }

        await _host!.Clipboard.SetTextAsync(text, cancellationToken);
        return CommandResult.Ok(message: "Copied OCR text.");
    }

    private async ValueTask<CommandResult> OpenImageAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (!context.Data.TryGetValue("sourcePath", out var sourcePath) || !File.Exists(sourcePath))
        {
            return CommandResult.Failed("OCR source image is missing.");
        }

        await _host!.Shell.OpenAsync(sourcePath, cancellationToken);
        return CommandResult.Ok(message: "Opened image.");
    }

    private async ValueTask<CommandResult> OpenImageFolderAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (!context.Data.TryGetValue("sourcePath", out var sourcePath))
        {
            return CommandResult.Failed("OCR source image is missing.");
        }

        await _host!.Shell.OpenContainingFolderAsync(sourcePath, cancellationToken);
        return CommandResult.Ok(message: "Opened image folder.");
    }

    private async ValueTask<CommandResult> OpenModelDirectoryAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var modelDirectory = context.Data.TryGetValue("modelDirectory", out var path) && !string.IsNullOrWhiteSpace(path)
            ? path
            : OcrSettings.FromHost(_host!.Settings).ModelDirectory;
        Directory.CreateDirectory(modelDirectory);
        await _host!.Shell.OpenAsync(modelDirectory, cancellationToken);
        return CommandResult.Ok(message: "Opened OCR model directory.");
    }

    private async Task<OcrEngine> GetEngineAsync(OcrSettings settings, CancellationToken cancellationToken)
    {
        await _engineGate.WaitAsync(cancellationToken);
        try
        {
            if (_engine is not null && settings.HasSameEngineFiles(_engineSettings))
            {
                return _engine;
            }

            var engine = new OcrEngine(settings);
            await engine.InitializeAsync(cancellationToken);
            _engine = engine;
            _engineSettings = settings;
            return engine;
        }
        finally
        {
            _engineGate.Release();
        }
    }

    private WeedResult OcrTextResult(string imagePath, string text)
    {
        var normalizedText = string.IsNullOrWhiteSpace(text) ? "No text recognized." : text.Trim();
        var firstLine = normalizedText.Split(["\r\n", "\n"], StringSplitOptions.None)
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? "No text recognized.";
        return new WeedResult
        {
            Id = $"ocr-result-{StableHash($"{imagePath}:{File.GetLastWriteTimeUtc(imagePath).Ticks}")}",
            PluginId = PluginId,
            Title = firstLine.Length <= 90 ? firstLine : string.Concat(firstLine.AsSpan(0, 90), "..."),
            Subtitle = $"{Path.GetFileName(imagePath)} - OCR result",
            Icon = WeedIcon.FromGlyph("OCR"),
            MatchScore = 30,
            DefaultCommand = "ocr.copyText",
            Actions =
            [
                new WeedAction { Command = "ocr.copyText", Title = "Copy text", Shortcut = "Enter" },
                new WeedAction { Command = "ocr.openTextFile", Title = "Open text file" },
                new WeedAction { Command = "ocr.openImage", Title = "Open source image" },
                new WeedAction { Command = "ocr.openImageFolder", Title = "Open source folder" }
            ],
            Data = new Dictionary<string, string>
            {
                ["sourcePath"] = imagePath,
                ["ocrText"] = normalizedText,
                ["detailText"] = normalizedText,
                ["displayLayout"] = "detail"
            }
        };
    }

    private static WeedResult ModelsMissingResult(OcrSettings settings, IReadOnlyList<string> missing) => new()
    {
        Id = "ocr-models-missing",
        PluginId = PluginId,
        Title = "OCR models are not installed",
        Subtitle = $"Missing {missing.Count} file(s). Run scripts\\fetch-ocr-models.ps1 and package the plugin again.",
        Icon = WeedIcon.FromGlyph("OCR"),
        MatchScore = 30,
        DefaultCommand = "ocr.openModelDirectory",
        Actions =
        [
            new WeedAction { Command = "ocr.openModelDirectory", Title = "Open model directory", Shortcut = "Enter" }
        ],
        Data = new Dictionary<string, string>
        {
            ["modelDirectory"] = settings.ModelDirectory,
            ["detailText"] = $"Expected model directory:{Environment.NewLine}{settings.ModelDirectory}{Environment.NewLine}{Environment.NewLine}Missing:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}",
            ["displayLayout"] = "detail"
        }
    };

    private static WeedResult ImageMissingResult(string imagePath) => new()
    {
        Id = $"ocr-image-missing-{StableHash(imagePath)}",
        PluginId = PluginId,
        Title = "Image file not found",
        Subtitle = imagePath,
        Icon = WeedIcon.FromGlyph("OCR"),
        MatchScore = 12,
        DefaultCommand = "__noop",
        Actions =
        [
            new WeedAction { Command = "__noop", Title = "Keep open", Shortcut = "Enter" }
        ]
    };

    private static WeedResult UnsupportedImageResult(string imagePath) => new()
    {
        Id = $"ocr-unsupported-{StableHash(imagePath)}",
        PluginId = PluginId,
        Title = "Unsupported image type",
        Subtitle = string.Join(", ", ImageExtensions),
        Icon = WeedIcon.FromGlyph("OCR"),
        MatchScore = 12,
        DefaultCommand = "__noop",
        Actions =
        [
            new WeedAction { Command = "__noop", Title = "Keep open", Shortcut = "Enter" }
        ]
    };

    private static WeedResult ErrorResult(string imagePath, string message) => new()
    {
        Id = $"ocr-error-{StableHash($"{imagePath}:{message}")}",
        PluginId = PluginId,
        Title = "OCR failed",
        Subtitle = message,
        Icon = WeedIcon.FromGlyph("OCR"),
        MatchScore = 12,
        DefaultCommand = "__noop",
        Actions =
        [
            new WeedAction { Command = "__noop", Title = "Keep open", Shortcut = "Enter" }
        ],
        Data = new Dictionary<string, string>
        {
            ["detailText"] = message,
            ["displayLayout"] = "detail"
        }
    };

    private static bool TryParseImagePath(string input, out string imagePath)
    {
        var value = input.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        value = Environment.ExpandEnvironmentVariables(value);
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            value = uri.LocalPath;
        }

        try
        {
            imagePath = Path.GetFullPath(value);
            return !string.IsNullOrWhiteSpace(imagePath);
        }
        catch
        {
            imagePath = string.Empty;
            return false;
        }
    }

    private static bool IsSupportedImage(string path)
    {
        var extension = Path.GetExtension(path);
        return ImageExtensions.Any(item => item.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    private string CaptureImagePath() =>
        Path.Combine(_host!.Storage.GetPluginCacheDirectory(PluginId), "captures", $"OCR-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");

    private string TextResultPath(string sourcePath, string text)
    {
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var safeStem = string.Concat(stem.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch));
        return Path.Combine(
            _host!.Storage.GetPluginDataDirectory(PluginId),
            "results",
            $"{safeStem}-{StableHash($"{sourcePath}:{text}")}.txt");
    }

    private static string StableHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}

public sealed record OcrSettings(
    string ModelDirectory,
    int MaxSideLen,
    int Padding,
    bool DoAngle)
{
    public string DetPath => Path.Combine(ModelDirectory, "ch_PP-OCRv5_mobile_det.onnx");

    public string ClsPath => Path.Combine(ModelDirectory, "ch_ppocr_mobile_v2.0_cls_infer.onnx");

    public string RecPath => Path.Combine(ModelDirectory, "ch_PP-OCRv5_rec_mobile_infer.onnx");

    public string KeyDicPath => Path.Combine(ModelDirectory, "ppocrv5_dict.txt");

    public static OcrSettings FromHost(IWeedSettings settings)
    {
        var configuredModelDirectory = settings.GetPluginSetting(OcrPlugin.PluginId, "modelDirectory", string.Empty).Trim();
        var modelDirectory = string.IsNullOrWhiteSpace(configuredModelDirectory)
            ? Path.Combine(PluginDirectory(), "models")
            : Environment.ExpandEnvironmentVariables(configuredModelDirectory);
        return new OcrSettings(
            Path.GetFullPath(modelDirectory),
            Math.Clamp(settings.GetPluginSetting(OcrPlugin.PluginId, "maxSideLen", 1024), 512, 4096),
            Math.Clamp(settings.GetPluginSetting(OcrPlugin.PluginId, "padding", 50), 0, 200),
            settings.GetPluginSetting(OcrPlugin.PluginId, "doAngle", true));
    }

    public IReadOnlyList<string> MissingModelFiles()
    {
        var files = new[] { DetPath, ClsPath, RecPath, KeyDicPath };
        return files.Where(path => !File.Exists(path)).Select(Path.GetFileName).OfType<string>().ToArray();
    }

    public bool HasSameEngineFiles(OcrSettings? other) =>
        other is not null &&
        DetPath.Equals(other.DetPath, StringComparison.OrdinalIgnoreCase) &&
        ClsPath.Equals(other.ClsPath, StringComparison.OrdinalIgnoreCase) &&
        RecPath.Equals(other.RecPath, StringComparison.OrdinalIgnoreCase) &&
        KeyDicPath.Equals(other.KeyDicPath, StringComparison.OrdinalIgnoreCase);

    private static string PluginDirectory()
    {
        var location = typeof(OcrPlugin).Assembly.Location;
        return string.IsNullOrWhiteSpace(location)
            ? AppContext.BaseDirectory
            : Path.GetDirectoryName(location) ?? AppContext.BaseDirectory;
    }
}

public sealed class OcrEngine
{
    private readonly OcrLite _engine;

    public OcrEngine(OcrSettings settings)
    {
        _engine = new OcrLite
        {
            DetPath = settings.DetPath,
            ClsPath = settings.ClsPath,
            RecPath = settings.RecPath,
            KeyDicPath = settings.KeyDicPath
        };
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _engine.InitModels();
    }

    public async Task<string> RecognizeAsync(string imagePath, OcrSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await _engine.DetectAsync(
            imagePath,
            settings.Padding,
            settings.MaxSideLen,
            doAngle: settings.DoAngle);
        return result?.StrRes?.Trim() ?? string.Empty;
    }
}
