using Xunit;

namespace SandboxServer.Tests;

public sealed class EventLogTests
{
    [Fact]
    public void BufferDropsOldestAtFiftyOne()
    {
        var log = new EventLogBuffer(50);
        for (int i = 0; i < 51; i++)
        {
            log.Add("e" + i);
        }

        IReadOnlyList<string> snapshot = log.Snapshot();
        Assert.Equal(50, snapshot.Count);
        Assert.Equal("e1", snapshot[0]);
        Assert.Equal("e50", snapshot[49]);
    }

    internal sealed class EventLogBuffer
    {
        private readonly int _capacity;
        private readonly List<string> _items = new();

        public EventLogBuffer(int capacity)
        {
            _capacity = capacity;
        }

        public void Add(string text)
        {
            _items.Add(text);
            if (_items.Count > _capacity)
            {
                _items.RemoveAt(0);
            }
        }

        public IReadOnlyList<string> Snapshot() => _items;
    }
}
