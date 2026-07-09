using Weed.Abstractions;
using IQueryProvider = Weed.Abstractions.IQueryProvider;

namespace Weed.Plugins.Emoji;

public sealed class EmojiPlugin : IWeedPlugin, IQueryProvider, ICommandHandler, IPluginSettingsProvider
{
    public const string PluginId = "weed.emoji";
    private static readonly EmojiEntry[] Entries =
    [
        new("grinning", "😀", "Smileys", ["grin", "smile", "happy", "face"]),
        new("smile", "😄", "Smileys", ["happy", "joy", "laugh"]),
        new("smiley", "😃", "Smileys", ["happy", "face"]),
        new("laughing", "😆", "Smileys", ["lol", "haha", "satisfied"]),
        new("joy", "😂", "Smileys", ["tears", "laugh", "funny"]),
        new("rofl", "🤣", "Smileys", ["rolling", "laugh"]),
        new("wink", "😉", "Smileys", ["flirt", "face"]),
        new("blush", "😊", "Smileys", ["smile", "shy"]),
        new("heart eyes", "😍", "Smileys", ["love", "crush"]),
        new("star struck", "🤩", "Smileys", ["stars", "excited"]),
        new("thinking", "🤔", "Smileys", ["hmm", "question"]),
        new("neutral face", "😐", "Smileys", ["meh"]),
        new("cry", "😢", "Smileys", ["sad", "tear"]),
        new("sob", "😭", "Smileys", ["cry", "sad"]),
        new("angry", "😠", "Smileys", ["mad"]),
        new("thumbs up", "👍", "People", ["like", "approve", "+1"]),
        new("thumbs down", "👎", "People", ["dislike", "-1"]),
        new("clap", "👏", "People", ["applause", "bravo"]),
        new("pray", "🙏", "People", ["please", "thanks", "hope"]),
        new("muscle", "💪", "People", ["strong", "flex"]),
        new("eyes", "👀", "People", ["look", "watch"]),
        new("writing hand", "✍️", "People", ["write", "pen"]),
        new("wave", "👋", "People", ["hello", "bye"]),
        new("ok hand", "👌", "People", ["ok", "perfect"]),
        new("point right", "👉", "People", ["right", "finger"]),
        new("red heart", "❤️", "Symbols", ["heart", "love"]),
        new("orange heart", "🧡", "Symbols", ["heart", "love"]),
        new("yellow heart", "💛", "Symbols", ["heart", "love"]),
        new("green heart", "💚", "Symbols", ["heart", "love"]),
        new("blue heart", "💙", "Symbols", ["heart", "love"]),
        new("purple heart", "💜", "Symbols", ["heart", "love"]),
        new("sparkles", "✨", "Symbols", ["stars", "shine"]),
        new("fire", "🔥", "Symbols", ["hot", "lit"]),
        new("check mark", "✅", "Symbols", ["done", "yes", "success"]),
        new("cross mark", "❌", "Symbols", ["no", "fail", "x"]),
        new("warning", "⚠️", "Symbols", ["alert", "caution"]),
        new("question", "❓", "Symbols", ["help"]),
        new("exclamation", "❗", "Symbols", ["bang", "important"]),
        new("rocket", "🚀", "Objects", ["launch", "ship", "deploy"]),
        new("hourglass", "⌛", "Objects", ["time", "wait"]),
        new("alarm clock", "⏰", "Objects", ["time", "reminder"]),
        new("light bulb", "💡", "Objects", ["idea", "hint"]),
        new("gear", "⚙️", "Objects", ["settings", "cog"]),
        new("wrench", "🔧", "Objects", ["tool", "fix"]),
        new("hammer", "🔨", "Objects", ["build", "tool"]),
        new("package", "📦", "Objects", ["box", "ship"]),
        new("memo", "📝", "Objects", ["note", "write"]),
        new("clipboard", "📋", "Objects", ["copy", "paste"]),
        new("calendar", "📅", "Objects", ["date", "schedule"]),
        new("paperclip", "📎", "Objects", ["attach"]),
        new("link", "🔗", "Objects", ["url", "chain"]),
        new("lock", "🔒", "Objects", ["secure", "private"]),
        new("key", "🔑", "Objects", ["password", "secret"]),
        new("magnifying glass", "🔍", "Objects", ["search", "find"]),
        new("file folder", "📁", "Objects", ["folder", "directory"]),
        new("page", "📄", "Objects", ["file", "document"]),
        new("computer", "💻", "Objects", ["laptop", "code"]),
        new("phone", "📱", "Objects", ["mobile"]),
        new("camera", "📷", "Objects", ["photo", "image"]),
        new("bug", "🐛", "Animals", ["issue", "debug"]),
        new("cat", "🐱", "Animals", ["pet"]),
        new("dog", "🐶", "Animals", ["pet"]),
        new("sun", "☀️", "Nature", ["weather", "bright"]),
        new("moon", "🌙", "Nature", ["night"]),
        new("cloud", "☁️", "Nature", ["weather"]),
        new("rainbow", "🌈", "Nature", ["color"]),
        new("coffee", "☕", "Food", ["drink", "cafe"]),
        new("pizza", "🍕", "Food", ["food"]),
        new("cake", "🎂", "Food", ["birthday"]),
        new("beer", "🍺", "Food", ["drink"]),
        new("soccer", "⚽", "Activities", ["football", "sports"]),
        new("trophy", "🏆", "Activities", ["award", "win"]),
        new("medal", "🏅", "Activities", ["award"]),
        new("party popper", "🎉", "Activities", ["celebrate", "congrats"]),
        new("gift", "🎁", "Activities", ["present"]),
        new("music", "🎵", "Activities", ["song"]),
        new("car", "🚗", "Travel", ["auto"]),
        new("airplane", "✈️", "Travel", ["flight", "plane"]),
        new("train", "🚆", "Travel", ["rail"]),
        new("house", "🏠", "Travel", ["home"]),
        new("globe", "🌍", "Travel", ["world", "earth"])
    ];

    private IWeedHost? _host;

    public string ProviderId => "emoji";

    public static WeedPluginManifest Manifest => new()
    {
        Id = PluginId,
        Name = "Emoji Search",
        Version = "0.1.0",
        SdkVersion = "0.1",
        Activations =
        [
            new PluginActivationManifest
            {
                Type = "keyword",
                Keyword = "emoji",
                Command = "emoji.search"
            }
        ],
        Permissions =
        [
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
            Key = "maxResults",
            Label = "Maximum results",
            Kind = PluginSettingKind.Integer,
            DefaultValue = "30",
            Min = 5,
            Max = 100
        },
        new()
        {
            Key = "copyFormat",
            Label = "Default copy format",
            Kind = PluginSettingKind.Select,
            DefaultValue = "emoji",
            Options =
            [
                new PluginSettingOption { Value = "emoji", Label = "Emoji" },
                new PluginSettingOption { Value = "shortcode", Label = "Shortcode" },
                new PluginSettingOption { Value = "name", Label = "Name" }
            ]
        }
    ];

    public ValueTask<IReadOnlyList<WeedResult>> QueryAsync(QueryContext context, CancellationToken cancellationToken)
    {
        var query = Normalize(context.NormalizedText);
        var maxResults = Math.Clamp(_host?.Settings.GetPluginSetting(PluginId, "maxResults", 30) ?? 30, 5, 100);
        var defaultCopyFormat = _host?.Settings.GetPluginSetting(PluginId, "copyFormat", "emoji") ?? "emoji";

        var matches = Entries
            .Select((entry, index) => (Entry: entry, Score: Score(entry, query), Index: index))
            .Where(item => query.Length == 0 || item.Score > 0)
            .OrderByDescending(item => query.Length == 0 ? 1 : item.Score)
            .ThenBy(item => item.Index)
            .Take(maxResults)
            .Select(item => ToResult(item.Entry, query.Length == 0 ? 18 : item.Score, defaultCopyFormat))
            .ToArray();

        return ValueTask.FromResult<IReadOnlyList<WeedResult>>(matches);
    }

    public async ValueTask<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (_host is null)
        {
            return CommandResult.Failed("Emoji Search is not initialized.");
        }

        var value = context.Command switch
        {
            "emoji.copy" => context.Data.GetValueOrDefault("emoji"),
            "emoji.copyShortcode" => context.Data.GetValueOrDefault("shortcode"),
            "emoji.copyName" => context.Data.GetValueOrDefault("name"),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(value))
        {
            return CommandResult.Failed($"Unknown emoji command: {context.Command}");
        }

        await _host.Clipboard.SetTextAsync(value, cancellationToken);
        return CommandResult.Ok(message: $"Copied {value}.");
    }

    private static WeedResult ToResult(EmojiEntry entry, double score, string defaultCopyFormat)
    {
        var defaultCommand = defaultCopyFormat.ToLowerInvariant() switch
        {
            "shortcode" => "emoji.copyShortcode",
            "name" => "emoji.copyName",
            _ => "emoji.copy"
        };

        return new WeedResult
        {
            Id = $"emoji-{entry.Shortcode}",
            PluginId = PluginId,
            Title = $"{entry.Value}  {entry.Name}",
            Subtitle = $"{entry.Category} - :{entry.Shortcode}:",
            Icon = WeedIcon.FromGlyph(entry.Value),
            MatchScore = Math.Clamp(score, 1, 30),
            DefaultCommand = defaultCommand,
            Actions =
            [
                new WeedAction { Command = "emoji.copy", Title = "Copy emoji", Shortcut = "Enter" },
                new WeedAction { Command = "emoji.copyShortcode", Title = "Copy shortcode" },
                new WeedAction { Command = "emoji.copyName", Title = "Copy name" }
            ],
            Data = new Dictionary<string, string>
            {
                ["emoji"] = entry.Value,
                ["shortcode"] = $":{entry.Shortcode}:",
                ["name"] = entry.Name,
                ["category"] = entry.Category
            }
        };
    }

    private static double Score(EmojiEntry entry, string query)
    {
        if (query.Length == 0)
        {
            return 18;
        }

        var name = Normalize(entry.Name);
        var shortcode = Normalize(entry.Shortcode);
        var category = Normalize(entry.Category);

        if (shortcode.Equals(query, StringComparison.OrdinalIgnoreCase) ||
            name.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 30;
        }

        if (entry.Aliases.Any(alias => Normalize(alias).Equals(query, StringComparison.OrdinalIgnoreCase)))
        {
            return 28;
        }

        if (shortcode.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 24;
        }

        if (entry.Aliases.Any(alias => Normalize(alias).StartsWith(query, StringComparison.OrdinalIgnoreCase)))
        {
            return 22;
        }

        if (shortcode.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 16;
        }

        if (entry.Aliases.Any(alias => Normalize(alias).Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            return 14;
        }

        return category.Contains(query, StringComparison.OrdinalIgnoreCase) ? 8 : 0;
    }

    private static string Normalize(string value) =>
        value.Trim().Trim(':').Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();

    private sealed record EmojiEntry(string Name, string Value, string Category, IReadOnlyList<string> Aliases)
    {
        public string Shortcode => Name.Replace(' ', '_').ToLowerInvariant();
    }
}
