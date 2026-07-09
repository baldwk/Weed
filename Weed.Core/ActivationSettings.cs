using Weed.Abstractions;

namespace Weed.Core;

public static class ActivationSettings
{
    public static string KeywordSettingKey(PluginActivationManifest activation)
    {
        var command = string.IsNullOrWhiteSpace(activation.Command)
            ? "command"
            : TextNormalizer.ToIdFragment(activation.Command);
        var keyword = string.IsNullOrWhiteSpace(activation.Keyword)
            ? "keyword"
            : TextNormalizer.ToIdFragment(activation.Keyword);
        return $"activation.keyword.{command}.{keyword}";
    }

    public static string EffectiveKeyword(IWeedSettings settings, string pluginId, PluginActivationManifest activation)
    {
        var fallback = TextNormalizer.Normalize(activation.Keyword ?? string.Empty);
        var configured = settings.GetPluginSetting(pluginId, KeywordSettingKey(activation), fallback);
        return NormalizeKeyword(configured, fallback);
    }

    public static string NormalizeKeyword(string value, string fallback)
    {
        var normalized = TextNormalizer.Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return fallback;
        }

        var firstSpace = normalized.IndexOf(' ');
        return firstSpace >= 0 ? normalized[..firstSpace] : normalized;
    }
}
