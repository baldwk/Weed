using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Weed.Plugins.Toolbox;

internal static class ToolboxTransforms
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions FormattedJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };
    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    private static readonly string[] ZonedDateFormats =
    [
        "O",
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
        "yyyy-MM-dd HH:mm:ssK",
        "yyyy-MM-dd HH:mm:ss.FFFFFFFK"
    ];

    private static readonly string[] LocalDateFormats =
    [
        "yyyy-MM-dd",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF"
    ];

    public static string Base64Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    public static string Base64Decode(string value)
    {
        var bytes = Convert.FromBase64String(value);
        return StrictUtf8.GetString(bytes);
    }

    public static string UrlEncode(string value) => Uri.EscapeDataString(value);

    public static string UrlDecode(string value) => Uri.UnescapeDataString(value);

    public static string Hash(string algorithm, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = algorithm switch
        {
            "sha256" => SHA256.HashData(bytes),
            "sha512" => SHA512.HashData(bytes),
            "sha1" => SHA1.HashData(bytes),
            "md5" => MD5.HashData(bytes),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unknown hash algorithm.")
        };
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string FormatJson(string value, bool indented)
    {
        using var document = JsonDocument.Parse(value, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow
        });
        return JsonSerializer.Serialize(
            document.RootElement,
            indented ? FormattedJsonOptions : CompactJsonOptions);
    }

    public static bool TryParseUnixTimestamp(string value, out DateTimeOffset timestamp, out string? error)
    {
        timestamp = default;
        error = null;
        var digits = value.StartsWith("-", StringComparison.Ordinal) ? value[1..] : value;
        if (digits.Length is not (10 or 13) || !digits.All(char.IsDigit) ||
            !long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var numeric))
        {
            return false;
        }

        try
        {
            timestamp = digits.Length == 13
                ? DateTimeOffset.FromUnixTimeMilliseconds(numeric)
                : DateTimeOffset.FromUnixTimeSeconds(numeric);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            error = "Timestamp is outside the supported date range.";
            return false;
        }
    }

    public static bool LooksNumeric(string value)
    {
        var digits = value.StartsWith("-", StringComparison.Ordinal) ? value[1..] : value;
        return digits.Length > 0 && digits.All(char.IsDigit);
    }

    public static bool TryParseDate(string value, out DateTimeOffset timestamp)
    {
        var styles = DateTimeStyles.AllowWhiteSpaces;
        if (DateTimeOffset.TryParseExact(value, ZonedDateFormats, CultureInfo.InvariantCulture, styles, out timestamp))
        {
            return true;
        }

        return DateTimeOffset.TryParseExact(
            value,
            LocalDateFormats,
            CultureInfo.InvariantCulture,
            styles | DateTimeStyles.AssumeLocal,
            out timestamp);
    }

    public static string FormatLocalTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);

    public static string FormatUtcTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'", CultureInfo.InvariantCulture);
}
