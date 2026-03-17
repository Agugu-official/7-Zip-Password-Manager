namespace _7_Zip_Password_Manager.Models;

public class LogEntry
{
    public DateTime Timestamp { get; init; }
    public string Message { get; init; } = string.Empty;
    public LogLevel Level { get; init; }

    public string FormattedTime => Timestamp.ToString("HH:mm:ss");
}
