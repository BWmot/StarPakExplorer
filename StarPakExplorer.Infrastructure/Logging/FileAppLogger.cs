using StarPakExplorer.Application.Abstractions;

namespace StarPakExplorer.Infrastructure.Logging;

public sealed class FileAppLogger : IAppLogger
{
    private readonly string logFilePath;
    private readonly object gate = new();

    public FileAppLogger()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var logDirectory = Path.Combine(root, "StarPakExplorer", "Logs");
        Directory.CreateDirectory(logDirectory);
        logFilePath = Path.Combine(logDirectory, "app.log");
    }

    public void Info(string message)
    {
        Write("INFO", message, null);
    }

    public void Error(string message, Exception? exception = null)
    {
        Write("ERROR", message, exception);
    }

    private void Write(string level, string message, Exception? exception)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} [{level}] {message}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        lock (gate)
        {
            File.AppendAllText(logFilePath, line + Environment.NewLine);
        }
    }
}
