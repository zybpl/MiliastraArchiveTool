using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace MiliastraArchiveTool.Infrastructure;

/// <summary>Append-only batch trace. Logging failures must never affect automation.</summary>
public sealed class WorkflowPerformanceTrace : IDisposable
{
    private static readonly AsyncLocal<WorkflowPerformanceTrace?> Active = new();
    private readonly WorkflowPerformanceTrace? _previous;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private bool _disposed;
    private long _sequence;
    private long _characters;
    public string FilePath { get; }

    public WorkflowPerformanceTrace(string? directory = null)
    {
        _previous = Active.Value;
        FilePath = Path.Combine(directory ?? ApplicationPaths.DiagnosticsDirectory,
            $"stage1-performance-{DateTime.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.jsonl");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            _writer = new StreamWriter(new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Active.Value = this;
        Event("Batch", "Start", AppContext.BaseDirectory);
    }

    public static void Event(string operation, string state, object? details = null) =>
        Active.Value?.Write(operation, state, details);

    public static IDisposable? Measure(string operation, object? details = null) =>
        Active.Value is { } trace ? new Measurement(trace, operation, details) : null;

    private void Write(string operation, string state, object? details = null, long? spanId = null, double? durationMs = null)
    {
        lock (_gate)
        {
            if (_disposed || _writer == null || _characters > 16 * 1024 * 1024) return;
            try
            {
                var line = JsonSerializer.Serialize(new { Timestamp = DateTimeOffset.Now, ElapsedMs = Math.Round(_elapsed.Elapsed.TotalMilliseconds, 2),
                    Sequence = ++_sequence, SpanId = spanId, Operation = operation, State = state, DurationMs = durationMs, Details = details });
                _writer.WriteLine(line);
                _characters += line.Length;
                if (_characters > 16 * 1024 * 1024)
                    _writer.WriteLine("{\"Operation\":\"TraceLimit\",\"State\":\"Stopped\",\"Details\":\"16 MiB character limit reached\"}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException or JsonException or NotSupportedException) { }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            Write("Batch", "End");
            _disposed = true;
            try { _writer?.Dispose(); } catch (IOException) { }
            _writer = null;
        }
        if (Active.Value == this) Active.Value = _previous;
    }

    private sealed class Measurement : IDisposable
    {
        private readonly WorkflowPerformanceTrace _trace;
        private readonly string _operation;
        private readonly Stopwatch _timer = Stopwatch.StartNew();
        private readonly long _id;
        private bool _disposed;
        public Measurement(WorkflowPerformanceTrace trace, string operation, object? details)
        {
            _trace = trace; _operation = operation;
            _id = Interlocked.Increment(ref _nextId);
            trace.Write(operation, "Start", details, _id);
        }
        private static long _nextId;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _trace.Write(_operation, "End", spanId: _id, durationMs: Math.Round(_timer.Elapsed.TotalMilliseconds, 2));
        }
    }
}
