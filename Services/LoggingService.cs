using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;

namespace WireFox.Services
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Success
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public LogLevel Level { get; set; } = LogLevel.Info;
        public string Category { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Details { get; set; }

        public string FormattedTime => Timestamp.ToString("HH:mm:ss.fff");

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"[{FormattedTime}] [{Level.ToString().ToUpperInvariant()}] [{Category}] {Message}");
            if (!string.IsNullOrEmpty(Details))
            {
                sb.Append($" | Details: {Details}");
            }
            return sb.ToString();
        }
    }

    public class LoggingService
    {
        private static readonly Lazy<LoggingService> _instance = new(() => new LoggingService());
        public static LoggingService Instance => _instance.Value;

        private readonly object _lock = new();
        private readonly string _logFilePath;
        private const int MaxLogEntries = 500;

        public ObservableCollection<LogEntry> Entries { get; } = new();

        public event Action<LogEntry>? LogAdded;

        private LoggingService()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string appFolder = Path.Combine(localAppData, "WireFox");
                Directory.CreateDirectory(appFolder);
                _logFilePath = Path.Combine(appFolder, "app.log");
            }
            catch
            {
                _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.log");
            }
        }

        public string LogFilePath => _logFilePath;

        public void Log(LogLevel level, string category, string message, string? details = null)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Category = category,
                Message = message,
                Details = details
            };

            // Write to file
            try
            {
                string line = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{entry.Level.ToString().ToUpperInvariant()}] [{entry.Category}] {entry.Message}";
                if (!string.IsNullOrEmpty(entry.Details))
                {
                    line += $"\n  {entry.Details.Replace("\n", "\n  ")}";
                }
                line += Environment.NewLine;

                lock (_lock)
                {
                    File.AppendAllText(_logFilePath, line);
                }
            }
            catch { }

            // Write to Debug output
            System.Diagnostics.Debug.WriteLine(entry.ToString());

            // Dispatch to UI collection
            var app = System.Windows.Application.Current;
            if (app != null)
            {
                app.Dispatcher.BeginInvoke(() =>
                {
                    Entries.Add(entry);
                    while (Entries.Count > MaxLogEntries)
                    {
                        Entries.RemoveAt(0);
                    }
                    LogAdded?.Invoke(entry);
                });
            }
        }

        public void Debug(string category, string message, string? details = null) => Log(LogLevel.Debug, category, message, details);
        public void Info(string category, string message, string? details = null) => Log(LogLevel.Info, category, message, details);
        public void Warning(string category, string message, string? details = null) => Log(LogLevel.Warning, category, message, details);
        public void Error(string category, string message, string? details = null) => Log(LogLevel.Error, category, message, details);
        public void Error(string category, string message, Exception ex) => Log(LogLevel.Error, category, $"{message}: {ex.Message}", ex.ToString());
        public void Success(string category, string message, string? details = null) => Log(LogLevel.Success, category, message, details);

        public void Clear()
        {
            var app = System.Windows.Application.Current;
            if (app != null)
            {
                app.Dispatcher.Invoke(() => Entries.Clear());
            }
            else
            {
                Entries.Clear();
            }
        }
    }
}
