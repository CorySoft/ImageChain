namespace ImageChain.Web.Logging;

public sealed class LogRingBuffer
{
    private const int MaxLines = 2000;
    private readonly object _lock = new();
    private readonly List<string> _lines = [];
    private long _total;

    public void Add(string line)
    {
        lock (_lock)
        {
            _lines.Add(line);
            _total++;
            if (_lines.Count > MaxLines)
                _lines.RemoveRange(0, _lines.Count - MaxLines);
        }
    }

    public IReadOnlyList<string> GetSince(long after)
    {
        lock (_lock)
        {
            if (_lines.Count == 0) return [];
            if (after < _startIndex) return [.. _lines];
            var offset = after - _startIndex;
            if (offset >= _lines.Count) return [];
            return _lines.Skip((int)offset).ToArray();
        }
    }

    private long _startIndex;

    public long Seq => _total;

    public void Clear()
    {
        lock (_lock)
        {
            _lines.Clear();
            _startIndex = _total;
        }
    }
}