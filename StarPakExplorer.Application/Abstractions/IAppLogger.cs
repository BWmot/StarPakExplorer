namespace StarPakExplorer.Application.Abstractions;

public interface IAppLogger
{
    void Info(string message);
    void Warn(string message, Exception? exception = null);
    void Error(string message, Exception? exception = null);
}
