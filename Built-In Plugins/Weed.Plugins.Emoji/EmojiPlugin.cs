using System.Globalization;
using System.Text;
using Weed.Abstractions;
using IQueryProvider = Weed.Abstractions.IQueryProvider;

namespace Weed.Plugins.Emoji;

public sealed class EmojiPlugin : IWeedPlugin, IQueryProvider, ICommandHandler, IPluginSettingsProvider
{
    public const string PluginId = "weed.emoji";
    private const string EmojiResourceName = "Weed.Plugins.Emoji.emoji-test.txt";
    private static readonly Lazy<IReadOnlyList<EmojiEntry>> Entries = new(LoadEntries);
    private static readonly EmojiEntry[] FallbackEntries =
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

    private static readonly (string Name, string[] Aliases)[] AliasRules =
    [
        ("grinning face", ["grin", "smile", "happy", "face"]),
        ("beaming face", ["laughing", "satisfied", "happy"]),
        ("face with tears of joy", ["joy", "tears", "laugh", "funny"]),
        ("rolling on the floor laughing", ["rofl", "rolling", "laugh"]),
        ("winking face", ["wink", "flirt"]),
        ("smiling face with smiling eyes", ["blush", "smile", "shy"]),
        ("smiling face with heart-eyes", ["heart eyes", "love", "crush"]),
        ("star-struck", ["stars", "excited"]),
        ("thinking face", ["thinking", "hmm", "question"]),
        ("thumbs up", ["like", "approve", "+1"]),
        ("thumbs down", ["dislike", "-1"]),
        ("clapping hands", ["clap", "applause", "bravo"]),
        ("folded hands", ["pray", "please", "thanks", "hope"]),
        ("flexed biceps", ["muscle", "strong", "flex"]),
        ("waving hand", ["wave", "hello", "bye"]),
        ("ok hand", ["ok", "perfect"]),
        ("backhand index pointing right", ["point right", "right", "finger"]),
        ("red heart", ["heart", "love"]),
        ("sparkles", ["stars", "shine"]),
        ("fire", ["hot", "lit"]),
        ("check mark", ["done", "yes", "success"]),
        ("cross mark", ["no", "fail", "x"]),
        ("warning", ["alert", "caution"]),
        ("question mark", ["question", "help"]),
        ("exclamation mark", ["exclamation", "bang", "important"]),
        ("rocket", ["launch", "ship", "deploy"]),
        ("hourglass", ["time", "wait"]),
        ("alarm clock", ["time", "reminder"]),
        ("light bulb", ["idea", "hint"]),
        ("gear", ["settings", "cog"]),
        ("wrench", ["tool", "fix"]),
        ("hammer", ["build", "tool"]),
        ("package", ["box", "ship"]),
        ("memo", ["note", "write"]),
        ("clipboard", ["copy", "paste"]),
        ("calendar", ["date", "schedule"]),
        ("paperclip", ["attach"]),
        ("link", ["url", "chain"]),
        ("locked", ["lock", "secure", "private"]),
        ("key", ["password", "secret"]),
        ("magnifying glass", ["search", "find"]),
        ("file folder", ["folder", "directory"]),
        ("page facing up", ["page", "file", "document"]),
        ("laptop", ["computer", "code"]),
        ("mobile phone", ["phone", "mobile"]),
        ("camera", ["photo", "image"]),
        ("lady beetle", ["bug", "issue", "debug"]),
        ("hot beverage", ["coffee", "drink", "cafe"]),
        ("pizza", ["food"]),
        ("birthday cake", ["cake", "birthday"]),
        ("beer mug", ["beer", "drink"]),
        ("soccer ball", ["soccer", "football", "sports"]),
        ("trophy", ["award", "win"]),
        ("sports medal", ["medal", "award"]),
        ("party popper", ["celebrate", "congrats"]),
        ("wrapped gift", ["gift", "present"]),
        ("musical note", ["music", "song"]),
        ("automobile", ["car", "auto"]),
        ("airplane", ["flight", "plane"]),
        ("locomotive", ["train", "rail"]),
        ("house", ["home"]),
        ("globe", ["world", "earth"])
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

        var matches = Entries.Value
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
            Id = $"emoji-{entry.CodepointSlug}",
            PluginId = PluginId,
            Title = entry.Name,
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
                ["category"] = entry.Category,
                ["subcategory"] = entry.Subcategory
            }
        };
    }

    private static EmojiEntry[] LoadEntries()
    {
        using var stream = typeof(EmojiPlugin).Assembly.GetManifestResourceStream(EmojiResourceName);
        if (stream is null)
        {
            return FallbackEntries;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var entries = new List<EmojiEntry>();
        var category = "Emoji";
        var subcategory = string.Empty;

        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("# group: ", StringComparison.Ordinal))
            {
                category = line["# group: ".Length..].Trim();
                subcategory = string.Empty;
                continue;
            }

            if (line.StartsWith("# subgroup: ", StringComparison.Ordinal))
            {
                subcategory = line["# subgroup: ".Length..].Trim();
                continue;
            }

            var entry = ParseEmojiLine(line, category, subcategory);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries.Count == 0 ? FallbackEntries : entries.ToArray();
    }

    private static EmojiEntry? ParseEmojiLine(string line, string category, string subcategory)
    {
        var semicolon = line.IndexOf(';');
        var hash = line.IndexOf('#');
        if (semicolon <= 0 || hash <= semicolon)
        {
            return null;
        }

        var status = line[(semicolon + 1)..hash].Trim();
        if (!status.Equals("fully-qualified", StringComparison.Ordinal))
        {
            return null;
        }

        var payload = line[(hash + 1)..].Trim();
        var emojiEnd = payload.IndexOf(' ');
        if (emojiEnd <= 0)
        {
            return null;
        }

        var emoji = payload[..emojiEnd];
        var nameStart = payload.IndexOf(' ', emojiEnd + 1);
        if (nameStart <= emojiEnd)
        {
            return null;
        }

        var name = payload[(nameStart + 1)..].Trim();
        if (name.Length == 0)
        {
            return null;
        }

        var codepoints = line[..semicolon].Trim();
        return new EmojiEntry(
            name,
            emoji,
            category,
            BuildAliases(name, category, subcategory),
            subcategory,
            ToCodepointSlug(codepoints));
    }

    private static IReadOnlyList<string> BuildAliases(string name, string category, string subcategory)
    {
        var normalizedName = Normalize(name);
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ToShortcode(name),
            category,
            subcategory
        };

        foreach (var (ruleName, ruleAliases) in AliasRules)
        {
            var normalizedRule = Normalize(ruleName);
            if (normalizedName.Equals(normalizedRule, StringComparison.Ordinal) ||
                normalizedName.Contains(normalizedRule, StringComparison.Ordinal))
            {
                foreach (var alias in ruleAliases)
                {
                    aliases.Add(alias);
                }
            }
        }

        if (normalizedName.Contains("heart", StringComparison.Ordinal))
        {
            aliases.Add("love");
        }

        aliases.Remove(string.Empty);
        return aliases.ToArray();
    }

    private static double Score(EmojiEntry entry, string query)
    {
        if (query.Length == 0)
        {
            return 18;
        }

        if (entry.SearchShortcode.Equals(query, StringComparison.Ordinal) ||
            entry.SearchName.Equals(query, StringComparison.Ordinal))
        {
            return 30;
        }

        if (entry.SearchAliases.Contains(query, StringComparer.Ordinal))
        {
            return 28;
        }

        if (entry.SearchShortcode.StartsWith(query, StringComparison.Ordinal) ||
            entry.SearchName.StartsWith(query, StringComparison.Ordinal))
        {
            return 24;
        }

        if (entry.SearchAliases.Any(alias => alias.StartsWith(query, StringComparison.Ordinal)))
        {
            return 22;
        }

        if (entry.SearchShortcode.Contains(query, StringComparison.Ordinal) ||
            entry.SearchName.Contains(query, StringComparison.Ordinal))
        {
            return 16;
        }

        if (entry.SearchAliases.Any(alias => alias.Contains(query, StringComparison.Ordinal)))
        {
            return 14;
        }

        return entry.SearchCategory.Contains(query, StringComparison.Ordinal) ||
               entry.SearchSubcategory.Contains(query, StringComparison.Ordinal)
            ? 8
            : 0;
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var source = value.Trim().Trim(':').Replace('_', ' ').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(source.Length);
        var previousWasSpace = true;

        for (var i = 0; i < source.Length; i++)
        {
            var ch = source[i];
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                previousWasSpace = false;
                continue;
            }

            if ((ch == '+' || ch == '-') && IsSignPart(source, i))
            {
                builder.Append(ch);
                previousWasSpace = false;
                continue;
            }

            if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    private static bool IsSignPart(string value, int index) =>
        (index > 0 && char.IsDigit(value[index - 1])) ||
        (index + 1 < value.Length && char.IsDigit(value[index + 1]));

    private static string ToShortcode(string name)
    {
        var shortcode = Normalize(name)
            .Replace("+", "plus", StringComparison.Ordinal)
            .Replace("-", "minus", StringComparison.Ordinal)
            .Replace(' ', '_')
            .Trim('_');

        return shortcode.Length == 0 ? "emoji" : shortcode;
    }

    private static string ToCodepointSlug(string codepoints) =>
        string.Join('-', codepoints
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.ToLowerInvariant()));

    private static string ToCodepointSlugFromEmoji(string value) =>
        string.Join('-', value
            .EnumerateRunes()
            .Select(rune => rune.Value.ToString("x", CultureInfo.InvariantCulture)));

    private sealed record EmojiEntry
    {
        public EmojiEntry(
            string name,
            string value,
            string category,
            IReadOnlyList<string> aliases,
            string subcategory = "",
            string codepointSlug = "")
        {
            Name = name;
            Value = value;
            Category = category;
            Subcategory = subcategory;
            Aliases = aliases;
            Shortcode = ToShortcode(name);
            CodepointSlug = string.IsNullOrWhiteSpace(codepointSlug)
                ? ToCodepointSlugFromEmoji(value)
                : codepointSlug;
            SearchName = Normalize(name);
            SearchShortcode = Normalize(Shortcode);
            SearchCategory = Normalize(category);
            SearchSubcategory = Normalize(subcategory);
            SearchAliases = aliases
                .Select(Normalize)
                .Where(alias => alias.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        public string Name { get; }

        public string Value { get; }

        public string Category { get; }

        public string Subcategory { get; }

        public IReadOnlyList<string> Aliases { get; }

        public string Shortcode { get; }

        public string CodepointSlug { get; }

        public string SearchName { get; }

        public string SearchShortcode { get; }

        public string SearchCategory { get; }

        public string SearchSubcategory { get; }

        public IReadOnlyList<string> SearchAliases { get; }
    }
}
