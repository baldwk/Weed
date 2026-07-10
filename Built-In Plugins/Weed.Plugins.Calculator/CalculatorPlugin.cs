using System.Globalization;
using Weed.Abstractions;
using IQueryProvider = Weed.Abstractions.IQueryProvider;

namespace Weed.Plugins.Calculator;

public sealed class CalculatorPlugin : IWeedPlugin, IQueryProvider, ICommandHandler, IPluginSettingsProvider
{
    public const string PluginId = "weed.calculator";
    private readonly ICalculatorEngine _engine;
    private IWeedHost? _host;

    public string ProviderId => "calculator";

    public CalculatorPlugin()
        : this(new ExpressionCalculatorEngine())
    {
    }

    internal CalculatorPlugin(ICalculatorEngine engine)
    {
        _engine = engine;
    }

    public static WeedPluginManifest Manifest => new()
    {
        Id = PluginId,
        Name = "Calculator",
        Version = "0.1.0",
        SdkVersion = "0.1",
        Icon = "assets/plugins/calculator.png",
        Activations =
        [
            new PluginActivationManifest
            {
                Type = "implicitQuery",
                Provider = "calculator"
            }
        ],
        Permissions =
        [
            "clipboard.write",
            "window.paste"
        ]
    };

    public ValueTask InitializeAsync(IWeedHost host, CancellationToken cancellationToken)
    {
        _host = host;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public IReadOnlyList<PluginSettingDefinition> GetSettings() => [];

    public ValueTask<IReadOnlyList<WeedResult>> QueryAsync(QueryContext context, CancellationToken cancellationToken)
    {
        var expression = context.NormalizedText;
        if (!LooksLikeExpression(expression))
        {
            return ValueTask.FromResult<IReadOnlyList<WeedResult>>([]);
        }

        try
        {
            var value = _engine.Evaluate(expression);
            var resultText = Format(value, Math.Clamp(_host?.Settings.GetPluginSetting(PluginId, "decimalPrecision", 10) ?? 10, 0, 16));
            return ValueTask.FromResult<IReadOnlyList<WeedResult>>(
            [
                new WeedResult
                {
                    Id = $"calc-{expression}",
                    PluginId = PluginId,
                    Title = $"{context.RawText.Trim()} = {resultText}",
                    Subtitle = "Enter copies result",
                    Icon = WeedIcon.FromPath(PluginIconPath()),
                    MatchScore = 30,
                    DefaultCommand = "calculator.copyResult",
                    Actions =
                    [
                        new WeedAction { Command = "calculator.copyResult", Title = "Copy result", Shortcut = "Enter" },
                        new WeedAction { Command = "calculator.pasteResult", Title = "Paste result" },
                        new WeedAction { Command = "calculator.copyEquation", Title = "Copy equation" }
                    ],
                    Data = new Dictionary<string, string>
                    {
                        ["expression"] = context.RawText.Trim(),
                        ["result"] = resultText
                    }
                }
            ]);
        }
        catch
        {
            return ValueTask.FromResult<IReadOnlyList<WeedResult>>([]);
        }
    }

    public async ValueTask<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (_host is null)
        {
            return CommandResult.Failed("Calculator is not initialized.");
        }

        if (!context.Data.TryGetValue("result", out var result))
        {
            return CommandResult.Failed("Calculator result is missing.");
        }

        switch (context.Command)
        {
            case "calculator.copyResult":
                await _host.Clipboard.SetTextAsync(result, cancellationToken);
                return CommandResult.Ok(message: "Copied result.");
            case "calculator.pasteResult":
                await _host.Clipboard.PasteTextAsync(result, cancellationToken);
                return CommandResult.Ok(message: "Pasted result.");
            case "calculator.copyEquation":
                var expression = context.Data.GetValueOrDefault("expression", "");
                await _host.Clipboard.SetTextAsync($"{expression} = {result}", cancellationToken);
                return CommandResult.Ok(message: "Copied equation.");
            default:
                return CommandResult.Failed($"Unknown calculator command: {context.Command}");
        }
    }

    private static bool LooksLikeExpression(string expression)
    {
        if (expression.Length == 0)
        {
            return false;
        }

        return expression.Any(char.IsDigit) &&
               expression.Any(ch => "+-*/^()%!".Contains(ch)) ||
               expression.StartsWith("sqrt(", StringComparison.OrdinalIgnoreCase) ||
               expression.StartsWith("abs(", StringComparison.OrdinalIgnoreCase) ||
               expression.StartsWith("sin(", StringComparison.OrdinalIgnoreCase) ||
               expression.StartsWith("cos(", StringComparison.OrdinalIgnoreCase) ||
               expression.StartsWith("tan(", StringComparison.OrdinalIgnoreCase) ||
               expression.StartsWith("ln(", StringComparison.OrdinalIgnoreCase) ||
               StartsWithLogFunctionCall(expression) ||
               expression.StartsWith("round(", StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWithLogFunctionCall(string expression)
    {
        if (!expression.StartsWith("log", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var position = 3;
        while (position < expression.Length && char.IsDigit(expression[position]))
        {
            position++;
        }

        return position < expression.Length && expression[position] == '(';
    }

    private static string Format(double value, int precision)
    {
        if (Math.Abs(value - Math.Round(value)) < 0.0000000001)
        {
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        }

        return precision == 0
            ? Math.Round(value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0." + new string('#', precision), CultureInfo.InvariantCulture);
    }

    private static string PluginIconPath() =>
        Path.Combine(AppContext.BaseDirectory, "assets", "plugins", "calculator.png");
}

internal interface ICalculatorEngine
{
    double Evaluate(string expression);
}

internal sealed class ExpressionCalculatorEngine : ICalculatorEngine
{
    public double Evaluate(string expression) => ExpressionParser.Evaluate(expression);
}

internal sealed class ExpressionParser
{
    private readonly string _text;
    private int _position;

    private ExpressionParser(string text)
    {
        _text = text.Replace(" ", "", StringComparison.Ordinal);
    }

    public static double Evaluate(string text)
    {
        var parser = new ExpressionParser(text);
        var value = parser.ParseExpression();
        if (parser._position != parser._text.Length)
        {
            throw new FormatException("Unexpected token.");
        }

        return value;
    }

    private double ParseExpression()
    {
        var value = ParseTerm();
        while (Match('+') || Match('-'))
        {
            var op = _text[_position - 1];
            var right = ParseTerm();
            value = op == '+' ? value + right : value - right;
        }

        return value;
    }

    private double ParseTerm()
    {
        var value = ParsePower();
        while (true)
        {
            if (Peek() == '*' && PeekNext() == '*')
            {
                return value;
            }

            if (Match('*'))
            {
                value *= ParsePower();
            }
            else if (Match('/'))
            {
                value /= ParsePower();
            }
            else if (Match('%'))
            {
                value %= ParsePower();
            }
            else
            {
                return value;
            }
        }
    }

    private double ParsePower()
    {
        var value = ParseUnary();
        if (MatchPowerOperator())
        {
            value = Math.Pow(value, ParsePower());
        }

        return value;
    }

    private double ParseUnary()
    {
        if (Match('+'))
        {
            return ParseUnary();
        }

        if (Match('-'))
        {
            return -ParseUnary();
        }

        return ParsePostfix();
    }

    private double ParsePostfix()
    {
        var value = ParsePrimary();
        while (true)
        {
            if (Match('!'))
            {
                value = Factorial(value);
                continue;
            }

            if (Peek() == '%' && IsPostfixPercent())
            {
                _position++;
                value /= 100d;
                continue;
            }

            return value;
        }
    }

    private double ParsePrimary()
    {
        if (Match('('))
        {
            var value = ParseExpression();
            Require(')');
            return value;
        }

        if (char.IsLetter(Peek()))
        {
            var name = ParseIdentifier();
            if (name.Equals("pi", StringComparison.OrdinalIgnoreCase))
            {
                return Math.PI;
            }

            if (name.Equals("e", StringComparison.OrdinalIgnoreCase))
            {
                return Math.E;
            }

            Require('(');
            var argument = ParseExpression();
            Require(')');
            return EvaluateFunction(name, argument);
        }

        return ParseNumber();
    }

    private static double EvaluateFunction(string name, double argument)
    {
        var normalizedName = name.ToLowerInvariant();
        return normalizedName switch
        {
            "sqrt" => Math.Sqrt(argument),
            "abs" => Math.Abs(argument),
            "sin" => Math.Sin(argument),
            "cos" => Math.Cos(argument),
            "tan" => Math.Tan(argument),
            "round" => Math.Round(argument),
            "ln" => Math.Log(argument),
            "log" => Math.Log10(argument),
            _ when TryGetLogBase(normalizedName, out var logBase) => Math.Log(argument, logBase),
            _ => throw new FormatException($"Unknown function {name}.")
        };
    }

    private double ParseNumber()
    {
        var start = _position;
        while (char.IsDigit(Peek()) || Peek() == '.')
        {
            _position++;
        }

        if (start == _position)
        {
            throw new FormatException("Number expected.");
        }

        return double.Parse(_text[start.._position], CultureInfo.InvariantCulture);
    }

    private string ParseIdentifier()
    {
        var start = _position;
        while (char.IsLetterOrDigit(Peek()))
        {
            _position++;
        }

        return _text[start.._position];
    }

    private static bool TryGetLogBase(string name, out double logBase)
    {
        logBase = 0d;
        const string prefix = "log";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.Length == prefix.Length)
        {
            return false;
        }

        var suffix = name[prefix.Length..];
        if (!suffix.All(char.IsDigit))
        {
            return false;
        }

        if (!long.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedBase) ||
            parsedBase <= 0 ||
            parsedBase == 1)
        {
            throw new FormatException("Log base must be greater than zero and not equal to one.");
        }

        logBase = parsedBase;
        return true;
    }

    private static double Factorial(double value)
    {
        var rounded = Math.Round(value);
        if (value < 0 ||
            Math.Abs(value - rounded) > 0.0000000001 ||
            rounded > 170)
        {
            throw new FormatException("Factorial is only supported for non-negative integers up to 170.");
        }

        var result = 1d;
        for (var i = 2; i <= (int)rounded; i++)
        {
            result *= i;
        }

        return result;
    }

    private char Peek() => _position < _text.Length ? _text[_position] : '\0';

    private char PeekNext() => _position + 1 < _text.Length ? _text[_position + 1] : '\0';

    private bool IsPostfixPercent()
    {
        var next = PeekNext();
        return next == '\0' || next is ')' or '+' or '-' or '*' or '/' or '^' or '%';
    }

    private bool MatchPowerOperator()
    {
        if (Match('^'))
        {
            return true;
        }

        if (Peek() == '*' && PeekNext() == '*')
        {
            _position += 2;
            return true;
        }

        return false;
    }

    private bool Match(char ch)
    {
        if (Peek() != ch)
        {
            return false;
        }

        _position++;
        return true;
    }

    private void Require(char ch)
    {
        if (!Match(ch))
        {
            throw new FormatException($"Expected {ch}.");
        }
    }
}
