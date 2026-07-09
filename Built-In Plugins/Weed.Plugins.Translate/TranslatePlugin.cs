using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Weed.Abstractions;
using IQueryProvider = Weed.Abstractions.IQueryProvider;

namespace Weed.Plugins.Translate;

public sealed class TranslatePlugin : IWeedPlugin, IQueryProvider, ICommandHandler, IPluginSettingsProvider
{
    public const string PluginId = "weed.translate";
    private readonly ITranslationClient _client;
    private IWeedHost? _host;

    public TranslatePlugin()
        : this(new HttpTranslationClient())
    {
    }

    public TranslatePlugin(ITranslationClient client)
    {
        _client = client;
    }

    public string ProviderId => "translate";

    public static WeedPluginManifest Manifest => new()
    {
        Id = PluginId,
        Name = "Translator",
        Version = "0.1.0",
        SdkVersion = "0.1",
        Activations =
        [
            new PluginActivationManifest
            {
                Type = "keyword",
                Keyword = "tr",
                Command = "translate.search"
            },
            new PluginActivationManifest
            {
                Type = "keyword",
                Keyword = "translate",
                Command = "translate.search"
            }
        ],
        Permissions =
        [
            "network",
            "clipboard.write"
        ]
    };

    public ValueTask InitializeAsync(IWeedHost host, CancellationToken cancellationToken)
    {
        _host = host;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public IReadOnlyList<PluginSettingDefinition> GetSettings() =>
    [
        new()
        {
            Key = "provider",
            Label = "Provider",
            Kind = PluginSettingKind.Select,
            DefaultValue = "google",
            Options =
            [
                new PluginSettingOption { Value = "google", Label = "Google Translate" },
                new PluginSettingOption { Value = "baidu", Label = "Baidu Translate" }
            ]
        },
        new()
        {
            Key = "defaultSourceLanguage",
            Label = "Default source language",
            Kind = PluginSettingKind.Text,
            DefaultValue = "auto",
            Description = "Use auto, en, zh-CN, ja, and other provider language codes."
        },
        new()
        {
            Key = "defaultTargetLanguage",
            Label = "Default target language",
            Kind = PluginSettingKind.Text,
            DefaultValue = "zh-CN",
            Description = "Used for unlabeled text by default."
        },
        new()
        {
            Key = "secondaryTargetLanguage",
            Label = "Secondary target language",
            Kind = PluginSettingKind.Text,
            DefaultValue = "en",
            Description = "Used when the provider detects that text is already in the default target language."
        },
        new()
        {
            Key = "queryDelayMilliseconds",
            Label = "Query delay",
            Kind = PluginSettingKind.Integer,
            DefaultValue = "500",
            Min = 0,
            Max = 5000,
            Description = "Wait this many milliseconds before calling the translation API."
        },
        new()
        {
            Key = "googleBaseUrl",
            Label = "Google base URL",
            Kind = PluginSettingKind.Text,
            DefaultValue = "https://translate.googleapis.com"
        },
        new()
        {
            Key = "baiduAppId",
            Label = "Baidu app ID",
            Kind = PluginSettingKind.Text,
            DefaultValue = ""
        },
        new()
        {
            Key = "baiduSecretKey",
            Label = "Baidu secret key",
            Kind = PluginSettingKind.Text,
            DefaultValue = ""
        },
        new()
        {
            Key = "baiduBaseUrl",
            Label = "Baidu base URL",
            Kind = PluginSettingKind.Text,
            DefaultValue = "https://fanyi-api.baidu.com/api/trans/vip/translate"
        },
        new()
        {
            Key = "proxyMode",
            Label = "Proxy mode",
            Kind = PluginSettingKind.Select,
            DefaultValue = "system",
            Options =
            [
                new PluginSettingOption { Value = "system", Label = "System proxy" },
                new PluginSettingOption { Value = "none", Label = "No proxy" },
                new PluginSettingOption { Value = "custom", Label = "Custom proxy" }
            ]
        },
        new()
        {
            Key = "proxyUrl",
            Label = "Proxy URL",
            Kind = PluginSettingKind.Text,
            DefaultValue = ""
        }
    ];

    public async ValueTask<IReadOnlyList<WeedResult>> QueryAsync(QueryContext context, CancellationToken cancellationToken)
    {
        if (_host is null)
        {
            return [];
        }

        var input = context.RawText.Trim();
        if (input.Length == 0)
        {
            return [];
        }

        var settings = TranslateSettings.FromHost(_host.Settings);
        var request = TranslateQuery.Parse(input, settings);
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return [];
        }

        if (settings.QueryDelayMilliseconds > 0)
        {
            await Task.Delay(settings.QueryDelayMilliseconds, cancellationToken);
        }

        try
        {
            var response = await _client.TranslateAsync(request, settings, cancellationToken);
            if (ShouldTranslateToSecondary(request, response, settings, out var secondaryRequest))
            {
                response = await _client.TranslateAsync(secondaryRequest, settings, cancellationToken);
                request = secondaryRequest;
            }

            return
            [
                ToResult(request, response)
            ];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _host.Logger.Warn($"Translation failed: {ex.Message}");
            return
            [
                ErrorResult(request, settings.Provider, ex.Message)
            ];
        }
    }

    public async ValueTask<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (_host is null)
        {
            return CommandResult.Failed("Translator is not initialized.");
        }

        switch (context.Command)
        {
            case "translate.copy":
                return await CopyAsync(context, "translatedText", "Copied translation.", cancellationToken);
            case "translate.copyPair":
                return await CopyPairAsync(context, cancellationToken);
            case "translate.swap":
                return Swap(context);
            case "translate.noop":
                return CommandResult.Ok(CommandBehavior.KeepLauncherOpen, context.Data.GetValueOrDefault("error") ?? "Translation unavailable.");
            default:
                return CommandResult.Failed($"Unknown translate command: {context.Command}");
        }
    }

    private async ValueTask<CommandResult> CopyAsync(
        CommandContext context,
        string key,
        string message,
        CancellationToken cancellationToken)
    {
        if (!context.Data.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return CommandResult.Failed("Translation text is missing.");
        }

        await _host!.Clipboard.SetTextAsync(text, cancellationToken);
        return CommandResult.Ok(message: message);
    }

    private async ValueTask<CommandResult> CopyPairAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (!context.Data.TryGetValue("sourceText", out var source) ||
            !context.Data.TryGetValue("translatedText", out var translated))
        {
            return CommandResult.Failed("Translation text is missing.");
        }

        await _host!.Clipboard.SetTextAsync($"{source}{Environment.NewLine}{translated}", cancellationToken);
        return CommandResult.Ok(message: "Copied source and translation.");
    }

    private static CommandResult Swap(CommandContext context)
    {
        if (!context.Data.TryGetValue("sourceLanguage", out var source) ||
            !context.Data.TryGetValue("targetLanguage", out var target) ||
            !context.Data.TryGetValue("sourceText", out var text))
        {
            return CommandResult.Failed("Translation query is missing.");
        }

        if (source.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return CommandResult.Ok(CommandBehavior.KeepLauncherOpen, "Cannot swap from auto-detected source.");
        }

        return new CommandResult
        {
            Succeeded = true,
            Behavior = CommandBehavior.ShowLauncher,
            InitialQuery = $"tr {target} {source} {text}"
        };
    }

    private static WeedResult ToResult(TranslationRequest request, TranslationResponse response) => new()
    {
        Id = $"translate-{StableHash($"{request.SourceLanguage}:{request.TargetLanguage}:{request.Text}:{response.TranslatedText}")}",
        PluginId = PluginId,
        Title = response.TranslatedText,
        Subtitle = $"{request.SourceLanguage} -> {request.TargetLanguage} via {response.Provider}",
        Icon = WeedIcon.FromGlyph("T"),
        MatchScore = 30,
        DefaultCommand = "translate.copy",
        Actions =
        [
            new WeedAction { Command = "translate.copy", Title = "Copy translation", Shortcut = "Enter" },
            new WeedAction { Command = "translate.copyPair", Title = "Copy source and translation" },
            new WeedAction { Command = "translate.swap", Title = "Swap languages" }
        ],
        Data = new Dictionary<string, string>
        {
            ["sourceText"] = request.Text,
            ["translatedText"] = response.TranslatedText,
            ["sourceLanguage"] = request.SourceLanguage,
            ["targetLanguage"] = request.TargetLanguage,
            ["provider"] = response.Provider,
            ["detailText"] = $"{request.Text}{Environment.NewLine}{response.TranslatedText}",
            ["displayLayout"] = "detail"
        }
    };

    private static WeedResult ErrorResult(TranslationRequest request, string provider, string message) => new()
    {
        Id = $"translate-error-{StableHash($"{provider}:{request.Text}:{message}")}",
        PluginId = PluginId,
        Title = "Translation unavailable",
        Subtitle = message,
        Icon = WeedIcon.FromGlyph("!"),
        MatchScore = 12,
        DefaultCommand = "translate.noop",
        Actions =
        [
            new WeedAction { Command = "translate.noop", Title = "Keep open", Shortcut = "Enter" }
        ],
        Data = new Dictionary<string, string>
        {
            ["sourceText"] = request.Text,
            ["sourceLanguage"] = request.SourceLanguage,
            ["targetLanguage"] = request.TargetLanguage,
            ["provider"] = provider,
            ["error"] = message
        }
    };

    private static string StableHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static bool ShouldTranslateToSecondary(
        TranslationRequest request,
        TranslationResponse response,
        TranslateSettings settings,
        out TranslationRequest secondaryRequest)
    {
        secondaryRequest = request;
        if (request.ExplicitLanguagePair)
        {
            return false;
        }

        var secondaryTarget = TranslateQuery.NormalizeLanguageOrDefault(settings.SecondaryTargetLanguage, "en");
        if (LanguagesMatch(secondaryTarget, request.TargetLanguage))
        {
            return false;
        }

        var detectedSource = TranslateQuery.NormalizeLanguageOrDefault(
            response.DetectedSourceLanguage ?? string.Empty,
            request.SourceLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase) ? string.Empty : request.SourceLanguage);
        if (string.IsNullOrWhiteSpace(detectedSource))
        {
            return false;
        }

        if (!LanguagesMatch(detectedSource, request.TargetLanguage))
        {
            return false;
        }

        secondaryRequest = new TranslationRequest(detectedSource, secondaryTarget, request.Text);
        return true;
    }

    private static bool LanguagesMatch(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return LanguageFamily(left).Equals(LanguageFamily(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string LanguageFamily(string language)
    {
        var normalized = language.Trim().ToLowerInvariant();
        return normalized switch
        {
            "auto" => "auto",
            "cht" or "zh" or "zh-cn" or "zh-hans" or "zh-tw" or "zh-hant" => "zh",
            "jp" or "ja" => "ja",
            "kor" or "ko" => "ko",
            _ => normalized.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? normalized
        };
    }
}

public interface ITranslationClient
{
    ValueTask<TranslationResponse> TranslateAsync(
        TranslationRequest request,
        TranslateSettings settings,
        CancellationToken cancellationToken);
}

public sealed record TranslationRequest(
    string SourceLanguage,
    string TargetLanguage,
    string Text)
{
    public bool ExplicitLanguagePair { get; init; }
}

public sealed record TranslationResponse(
    string TranslatedText,
    string Provider,
    string? DetectedSourceLanguage = null);

public sealed record TranslateSettings(
    string Provider,
    string DefaultSourceLanguage,
    string DefaultTargetLanguage,
    string SecondaryTargetLanguage,
    int QueryDelayMilliseconds,
    string GoogleBaseUrl,
    string BaiduAppId,
    string BaiduSecretKey,
    string BaiduBaseUrl,
    string ProxyMode,
    string ProxyUrl)
{
    public static TranslateSettings FromHost(IWeedSettings settings) => new(
        settings.GetPluginSetting(TranslatePlugin.PluginId, "provider", "google").Trim().ToLowerInvariant(),
        settings.GetPluginSetting(TranslatePlugin.PluginId, "defaultSourceLanguage", "auto").Trim(),
        settings.GetPluginSetting(TranslatePlugin.PluginId, "defaultTargetLanguage", "zh-CN").Trim(),
        settings.GetPluginSetting(TranslatePlugin.PluginId, "secondaryTargetLanguage", "en").Trim(),
        Math.Clamp(settings.GetPluginSetting(TranslatePlugin.PluginId, "queryDelayMilliseconds", 500), 0, 5000),
        settings.GetPluginSetting(TranslatePlugin.PluginId, "googleBaseUrl", "https://translate.googleapis.com").Trim(),
        settings.GetPluginSetting(TranslatePlugin.PluginId, "baiduAppId", string.Empty).Trim(),
        settings.GetPluginSetting(TranslatePlugin.PluginId, "baiduSecretKey", string.Empty).Trim(),
        settings.GetPluginSetting(TranslatePlugin.PluginId, "baiduBaseUrl", "https://fanyi-api.baidu.com/api/trans/vip/translate").Trim(),
        settings.GetPluginSetting(TranslatePlugin.PluginId, "proxyMode", "system").Trim().ToLowerInvariant(),
        settings.GetPluginSetting(TranslatePlugin.PluginId, "proxyUrl", string.Empty).Trim());
}

public static class TranslateQuery
{
    public static TranslationRequest Parse(string input, TranslateSettings settings)
    {
        var parts = input.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 && LooksLikeLanguageCode(parts[0]) && LooksLikeLanguageCode(parts[1]))
        {
            return new TranslationRequest(
                NormalizeLanguage(parts[0]),
                NormalizeLanguage(parts[1]),
                string.Join(' ', parts.Skip(2)))
            {
                ExplicitLanguagePair = true
            };
        }

        var sourceLanguage = NormalizeLanguageOrDefault(settings.DefaultSourceLanguage, "auto");
        var targetLanguage = NormalizeLanguageOrDefault(settings.DefaultTargetLanguage, "zh-CN");

        return new TranslationRequest(sourceLanguage, targetLanguage, input);
    }

    private static bool LooksLikeLanguageCode(string value)
    {
        if (value.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var parts = value.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length is 1 or 2 &&
               parts.All(part => part.Length is >= 2 and <= 8 && part.All(char.IsLetter));
    }

    private static string NormalizeLanguage(string language)
    {
        if (language.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return "auto";
        }

        var parts = language.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return language;
        }

        return parts.Length == 1
            ? parts[0].ToLowerInvariant()
            : $"{parts[0].ToLowerInvariant()}-{parts[1].ToUpperInvariant()}";
    }

    public static string NormalizeLanguageOrDefault(string language, string fallback) =>
        NormalizeLanguage(string.IsNullOrWhiteSpace(language) ? fallback : language);
}

public sealed class HttpTranslationClient : ITranslationClient
{
    public async ValueTask<TranslationResponse> TranslateAsync(
        TranslationRequest request,
        TranslateSettings settings,
        CancellationToken cancellationToken)
    {
        return settings.Provider switch
        {
            "baidu" => await TranslateWithBaiduAsync(request, settings, cancellationToken),
            _ => await TranslateWithGoogleAsync(request, settings, cancellationToken)
        };
    }

    private static async Task<TranslationResponse> TranslateWithGoogleAsync(
        TranslationRequest request,
        TranslateSettings settings,
        CancellationToken cancellationToken)
    {
        using var http = CreateHttpClient(settings);
        var baseUrl = string.IsNullOrWhiteSpace(settings.GoogleBaseUrl)
            ? "https://translate.googleapis.com"
            : settings.GoogleBaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/translate_a/single?client=gtx&sl={Uri.EscapeDataString(request.SourceLanguage)}&tl={Uri.EscapeDataString(request.TargetLanguage)}&dt=t&q={Uri.EscapeDataString(request.Text)}";
        using var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var translated = ReadGoogleTranslation(json.RootElement);
        if (string.IsNullOrWhiteSpace(translated))
        {
            throw new InvalidOperationException("Google Translate returned an empty response.");
        }

        var detected = json.RootElement.ValueKind == JsonValueKind.Array &&
                       json.RootElement.GetArrayLength() > 2 &&
                       json.RootElement[2].ValueKind == JsonValueKind.String
            ? json.RootElement[2].GetString()
            : null;
        return new TranslationResponse(translated, "Google Translate", detected);
    }

    private static string ReadGoogleTranslation(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var sentence in root[0].EnumerateArray())
        {
            if (sentence.ValueKind == JsonValueKind.Array &&
                sentence.GetArrayLength() > 0 &&
                sentence[0].ValueKind == JsonValueKind.String)
            {
                builder.Append(sentence[0].GetString());
            }
        }

        return builder.ToString();
    }

    private static async Task<TranslationResponse> TranslateWithBaiduAsync(
        TranslationRequest request,
        TranslateSettings settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.BaiduAppId) ||
            string.IsNullOrWhiteSpace(settings.BaiduSecretKey))
        {
            throw new InvalidOperationException("Baidu Translate requires app ID and secret key.");
        }

        using var http = CreateHttpClient(settings);
        var endpoint = string.IsNullOrWhiteSpace(settings.BaiduBaseUrl)
            ? "https://fanyi-api.baidu.com/api/trans/vip/translate"
            : settings.BaiduBaseUrl.Trim();
        var salt = RandomNumberGenerator.GetInt32(int.MaxValue).ToString(CultureInfo.InvariantCulture);
        var sign = Md5Hex($"{settings.BaiduAppId}{request.Text}{salt}{settings.BaiduSecretKey}");
        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("q", request.Text),
            new KeyValuePair<string, string>("from", BaiduLanguage(request.SourceLanguage)),
            new KeyValuePair<string, string>("to", BaiduLanguage(request.TargetLanguage)),
            new KeyValuePair<string, string>("appid", settings.BaiduAppId),
            new KeyValuePair<string, string>("salt", salt),
            new KeyValuePair<string, string>("sign", sign)
        ]);
        using var response = await http.PostAsync(endpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (json.RootElement.TryGetProperty("error_code", out var errorCode))
        {
            var code = errorCode.GetString() ?? "unknown";
            var message = json.RootElement.TryGetProperty("error_msg", out var errorMessage)
                ? errorMessage.GetString()
                : "Baidu Translate returned an error.";
            throw new InvalidOperationException($"{code}: {message}");
        }

        var translated = ReadBaiduTranslation(json.RootElement);
        if (string.IsNullOrWhiteSpace(translated))
        {
            throw new InvalidOperationException("Baidu Translate returned an empty response.");
        }

        var detected = json.RootElement.TryGetProperty("from", out var from) && from.ValueKind == JsonValueKind.String
            ? from.GetString()
            : null;
        return new TranslationResponse(translated, "Baidu Translate", detected);
    }

    private static HttpClient CreateHttpClient(TranslateSettings settings)
    {
        var handler = new HttpClientHandler();
        if (settings.ProxyMode.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            handler.UseProxy = false;
        }
        else if (settings.ProxyMode.Equals("custom", StringComparison.OrdinalIgnoreCase) &&
                 !string.IsNullOrWhiteSpace(settings.ProxyUrl))
        {
            handler.Proxy = new WebProxy(settings.ProxyUrl);
            handler.UseProxy = true;
        }

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
    }

    private static string ReadBaiduTranslation(JsonElement root)
    {
        if (!root.TryGetProperty("trans_result", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var lines = new List<string>();
        foreach (var result in results.EnumerateArray())
        {
            if (result.TryGetProperty("dst", out var dst) && dst.ValueKind == JsonValueKind.String)
            {
                lines.Add(dst.GetString() ?? string.Empty);
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BaiduLanguage(string language) =>
        language.ToLowerInvariant() switch
        {
            "auto" => "auto",
            "zh" or "zh-cn" or "zh-hans" => "zh",
            "zh-tw" or "zh-hant" => "cht",
            "ja" => "jp",
            "ko" => "kor",
            _ => language
        };

    private static string Md5Hex(string text) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
