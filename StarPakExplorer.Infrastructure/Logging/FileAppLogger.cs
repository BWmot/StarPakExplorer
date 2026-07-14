using StarPakExplorer.Application.Abstractions;

namespace StarPakExplorer.Infrastructure.Logging;

public sealed class FileAppLogger : IAppLogger
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB
    private const int MaxRotatedFiles = 10;

    private readonly string logDirectory;
    private readonly string logFilePath;
    private readonly object gate = new();

    public FileAppLogger()
    {
        logDirectory = Path.Combine(AppContext.BaseDirectory, "Logs");
        Directory.CreateDirectory(logDirectory);
        logFilePath = Path.Combine(logDirectory, "app.log");
    }

    public void Info(string message)
    {
        Write("INFO", message, null);
    }

    public void Warn(string message, Exception? exception = null)
    {
        Write("WARN", message, exception);
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
            try
            {
                RotateIfNeeded();
                File.AppendAllText(logFilePath, line + Environment.NewLine);
            }
            catch
            {
                // 日志写入失败不应影响主流程
            }
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(logFilePath))
        {
            return;
        }

        var fileInfo = new FileInfo(logFilePath);
        if (fileInfo.Length < MaxFileSizeBytes)
        {
            return;
        }

        // Rotate: rename current log to timestamped backup
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var rotatedPath = Path.Combine(logDirectory, $"app_{timestamp}.log");
        File.Move(logFilePath, rotatedPath);

        // Clean up old rotated files, keep only the newest N
        var rotatedFiles = Directory.GetFiles(logDirectory, "app_*.log")
            .OrderByDescending(f => f)
            .ToList();

        while (rotatedFiles.Count > MaxRotatedFiles)
        {
            var oldest = rotatedFiles[^1];
            rotatedFiles.RemoveAt(rotatedFiles.Count - 1);
            try { File.Delete(oldest); } catch { }
        }
    }
}
