using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace NissanConnectLib.Example;

internal class CustomConsoleFormatter : ConsoleFormatter
{
    public CustomConsoleFormatter() : base(nameof(CustomConsoleFormatter)) { }

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);

        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        textWriter.WriteLine($"[{DateTime.Now}] [{logEntry.LogLevel,11}] {message}");

        if (logEntry.Exception is not null)
        {
            textWriter.WriteLine($"[{DateTime.Now}] [{logEntry.LogLevel,11}] {logEntry.Exception.Message}");
        }
    }
}
