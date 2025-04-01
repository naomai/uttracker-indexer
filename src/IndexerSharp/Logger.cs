using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Naomai.UTT.Indexer;

public sealed class UTTLegacyFormatter : ConsoleFormatter, IDisposable
{
    static DateTime beginning = DateTime.Parse("1.01.1990 00:00:00");
    static DateTime lastLogItemTime = beginning;

    private readonly IDisposable? _optionsReloadToken;
    private UTTLegacyFormatterOptions _formatterOptions;

    private bool ConsoleColorFormattingEnabled =>
        _formatterOptions.ColorBehavior == LoggerColorBehavior.Enabled ||
        _formatterOptions.ColorBehavior == LoggerColorBehavior.Default &&
        System.Console.IsOutputRedirected == false;

    public UTTLegacyFormatter(IOptionsMonitor<UTTLegacyFormatterOptions> options) : base("UTT1LogFormatter")
    {
        (_optionsReloadToken, _formatterOptions) =
        (options.OnChange(ReloadLoggerOptions), options.CurrentValue);
    }

    private void ReloadLoggerOptions(UTTLegacyFormatterOptions options) =>
    _formatterOptions = options;


    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        string message =
            logEntry.Formatter?.Invoke(
                logEntry.State, logEntry.Exception);

        if (message is null)
        {
            return;
        }

        string tag = logEntry.Category;
        
        string dateFormatted = GetFormattedTimestamp();
        string colorCode = "", levelChar = "";
        LogLevel level = logEntry.LogLevel;

        switch (level)
        {
            case LogLevel.Error:
            case LogLevel.Critical:
                levelChar = "E";
                colorCode = "\x1B[1m\x1B[31m";
                break;
            case LogLevel.Warning:
                levelChar = "W";
                colorCode = "\x1B[1m\x1B[33m";
                break;
            case LogLevel.Information:
                levelChar = "I";
                colorCode = "\x1B[36m";
                break;
            case LogLevel.Debug:
                levelChar = "D";
                colorCode = "\x1B[35m";
                break;
        }
        string scopeString = MakeScopeString(scopeProvider);

        textWriter.WriteLine("[{1}] {0}{2} {3}{5}: {4}\u001b[0m", colorCode, dateFormatted, levelChar, tag, message, scopeString);
    }

    private static string GetFormattedTimestamp()
    {
        int logLastDay = (int)Math.Floor((lastLogItemTime - beginning).TotalDays);

        string dateFormatted;
        if (logLastDay == Math.Floor((DateTime.UtcNow - beginning).TotalDays))
        {

            dateFormatted = DateTime.UtcNow.ToString("HH:mm:ss");
        }
        else
        {
            dateFormatted = DateTime.UtcNow.ToString("MM-dd-yyyy HH:mm:ss");
        }
        lastLogItemTime = DateTime.UtcNow;
        return dateFormatted;
    }

    private static string MakeScopeString(IExternalScopeProvider? scopeProvider)
    {
        var scopes = new List<string>();
        var scopeString = "";

        scopeProvider.ForEachScope((scope, scopes) =>
        {
            if (scope is null)
            {
                return;
            }
            scopes.Add(scope.ToString());
        }, scopes);

        if (scopes.Count > 0)
        {
            scopeString = "(" + string.Join("|", scopes) + ")";
        }

        return scopeString;
    }

    public void Dispose()
    {

    }
}

public class UTTLegacyFormatterOptions : SimpleConsoleFormatterOptions
{
    public string? CustomPrefix { get; set; }
}
