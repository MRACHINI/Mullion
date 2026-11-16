using System.IO;

namespace Mullion;

/// <summary>Appends problems to mullion.log next to the executable. Never throws.</summary>
internal static class Log
{
    private const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(App.DataDirectory, "mullion.log");

    /// <summary>Set by starting Mullion with "--trace"; makes <see cref="Trace"/> write to the log.</summary>
    public static bool TraceEnabled { get; set; }

    /// <summary>Writes a step-by-step detail, only when tracing was asked for. For tracking down problems.</summary>
    public static void Trace(string message)
    {
        if (TraceEnabled)
        {
            Write("trace: " + message);
        }
    }

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            lock (Gate)
            {
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes)
                {
                    file.Delete();
                }

                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
                if (exception is not null)
                {
                    line += Environment.NewLine + exception;
                }

                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
