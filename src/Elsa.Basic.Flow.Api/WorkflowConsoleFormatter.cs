using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Elsa.Basic.Flow.Api;

/// <summary>
/// Console formatter that makes workflow logs easier to follow when debugging
/// a single workflow run. Highlights [WorkflowName] tags with per-workflow
/// colours, indents sub-workflows, dims Elsa/framework internals.
/// </summary>
public sealed partial class WorkflowConsoleFormatter : ConsoleFormatter
{
    public const string FormatterName = "workflow";

    // ── ANSI escape codes ────────────────────────────────────────────────────
    private const string Reset      = "\x1b[0m";
    private const string Bold       = "\x1b[1m";
    private const string Dim        = "\x1b[2m";
    private const string Gray       = "\x1b[90m";
    private const string White      = "\x1b[97m";
    private const string Green      = "\x1b[32m";
    private const string Yellow     = "\x1b[33m";
    private const string Red        = "\x1b[31m";
    private const string BoldCyan   = "\x1b[1;36m";
    private const string BoldGreen  = "\x1b[1;32m";

    // Workflow colours + indentation — add entries here for new workflows
    private static readonly Dictionary<string, (string Color, string Indent)> WorkflowStyles = new()
    {
        ["FulfilmentWorkflow"]  = (BoldCyan,   ""),
        ["PreparationWorkflow"] = (BoldGreen,  "    "),
    };

    [GeneratedRegex(@"^\[(\w+)\]\s*", RegexOptions.Compiled)]
    private static partial Regex WorkflowTagPattern();

    public WorkflowConsoleFormatter() : base(FormatterName) { }

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception is null)
            return;

        var level    = logEntry.LogLevel;
        var category = logEntry.Category;

        // ── Timestamp ────────────────────────────────────────────────────────
        textWriter.Write($"{Gray}{DateTimeOffset.Now:HH:mm:ss.fff}{Reset}  ");

        // ── Level badge ──────────────────────────────────────────────────────
        textWriter.Write(LevelBadge(level));
        textWriter.Write("  ");

        // ── Dim framework / Elsa-internal categories ─────────────────────────
        var isInternal = category.StartsWith("Elsa.")
                      && !category.StartsWith("Elsa.Basic.Flow");
        var isFramework = category.StartsWith("Microsoft.")
                       || category.StartsWith("System.");

        if (isInternal || isFramework)
        {
            textWriter.WriteLine($"{Dim}{message}{Reset}");
            if (logEntry.Exception is not null)
                textWriter.WriteLine($"{Dim}{logEntry.Exception}{Reset}");
            return;
        }

        // ── Parse [WorkflowName] or [SomeTag] prefix from message ─────────────
        var match = WorkflowTagPattern().Match(message);
        if (match.Success)
        {
            var tag  = match.Groups[1].Value;
            var rest = message[match.Length..];

            var (color, indent) = WorkflowStyles.TryGetValue(tag, out var style)
                ? style
                : (Bold, "  ");

            textWriter.Write(indent);
            textWriter.Write($"{color}[{tag}]{Reset}  ");
            textWriter.Write(MessageColor(level));
            textWriter.Write(rest);
            textWriter.Write(Reset);
        }
        else
        {
            textWriter.Write($"{White}{message}{Reset}");
        }

        if (logEntry.Exception is not null)
            textWriter.WriteLine($"\n  {Red}{logEntry.Exception}{Reset}");

        textWriter.WriteLine();
    }

    private static string LevelBadge(LogLevel level) => level switch
    {
        LogLevel.Trace       => $"{Dim}TRC{Reset}",
        LogLevel.Debug       => $"{Dim}DBG{Reset}",
        LogLevel.Information => $"{Green}INF{Reset}",
        LogLevel.Warning     => $"{Yellow}WRN{Reset}",
        LogLevel.Error       => $"{Red}ERR{Reset}",
        LogLevel.Critical    => $"{Bold}{Red}CRT{Reset}",
        _                    => "   "
    };

    private static string MessageColor(LogLevel level) => level switch
    {
        LogLevel.Warning  => Yellow,
        LogLevel.Error    => Red,
        LogLevel.Critical => $"{Bold}{Red}",
        _                 => White
    };
}
