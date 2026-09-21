using System.Net;
using System.Net.Sockets;
using Xunit;

namespace SandboxServer.Tests;

public sealed class ChatTests
{
    [Fact]
    public void RejectsEmptyOverlongAndNewlines()
    {
        Assert.False(ChatRules.TryNormalize("   ", out _));
        Assert.False(ChatRules.TryNormalize("hello\nworld", out _));
        Assert.False(ChatRules.TryNormalize(new string('a', ChatConfig.MaxMessageLength + 1), out _));
        Assert.True(ChatRules.TryNormalize("hello world", out string? text));
        Assert.Equal("hello world", text);
    }

    [Fact]
    public void ParsesRestOfLine()
    {
        Assert.True(Protocol.TryParseClientLine("CHAT hello there", out ClientCommand? command, out _));
        Assert.Equal("hello there", Assert.IsType<ChatCommand>(command).Text);
        Assert.Equal("CHAT 7 Alice hello there", Protocol.Chat(7, "Alice", "hello there"));
        Assert.False(Protocol.TryParseClientLine("CHAT", out _, out string? error));
        Assert.Equal("invalid_command", error);
        Assert.False(Protocol.TryParseClientLine("CHAT hello\tthere", out _, out error));
        Assert.Equal("invalid_chat", error);
    }

    [Fact]
    public async Task ChatIsWorldScopedAndRejectedOutsideWorld()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var host = new ServerHost(IPAddress.Loopback, 0);
        Task run = host.RunAsync(cts.Token);
        try
        {
            await WaitForPortAsync(host, cts.Token);
            await using var alice = await LineClient.ConnectAsync(host.ListenPort, cts.Token);
            await using var bob = await LineClient.ConnectAsync(host.ListenPort, cts.Token);
            await using var charlie = await LineClient.ConnectAsync(host.ListenPort, cts.Token);

            await alice.SendAsync("JOIN Alice");
            string aliceWelcome = await alice.WaitForAsync(line => line.StartsWith("WELCOME ", StringComparison.Ordinal), cts.Token);
            int aliceId = int.Parse(aliceWelcome.Split(' ')[1]);
            await bob.SendAsync("JOIN Bob");
            await charlie.SendAsync("JOIN Charlie");
            await bob.WaitForAsync(line => line.StartsWith("WELCOME ", StringComparison.Ordinal), cts.Token);
            await charlie.WaitForAsync(line => line.StartsWith("WELCOME ", StringComparison.Ordinal), cts.Token);

            await alice.SendAsync("CHAT too soon");
            string notInWorld = await alice.WaitForAsync(line => line.StartsWith("ERROR ", StringComparison.Ordinal), cts.Token);
            Assert.Equal("ERROR not_in_world", notInWorld);

            await alice.SendAsync("ENTER START");
            await bob.SendAsync("ENTER START");
            await charlie.SendAsync("ENTER TEST");
            await alice.WaitForAsync(line => line.StartsWith("WORLD_ENTERED ", StringComparison.Ordinal), cts.Token);
            await bob.WaitForAsync(line => line.StartsWith("WORLD_ENTERED ", StringComparison.Ordinal), cts.Token);
            await charlie.WaitForAsync(line => line.StartsWith("WORLD_ENTERED ", StringComparison.Ordinal), cts.Token);

            alice.IgnoreUntilNow();
            bob.IgnoreUntilNow();
            charlie.IgnoreUntilNow();

            await alice.SendAsync("CHAT hello there");
            string aliceChat = await alice.WaitForAsync(IsChat, cts.Token);
            string bobChat = await bob.WaitForAsync(IsChat, cts.Token);
            Assert.Equal("CHAT " + aliceId + " Alice hello there", aliceChat);
            Assert.Equal("CHAT " + aliceId + " Alice hello there", bobChat);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                using var shortCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
                await charlie.WaitForAsync(IsChat, shortCts.Token);
            });
        }
        finally
        {
            cts.Cancel();
            try
            {
                await run;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private static bool IsChat(string line) => line.StartsWith("CHAT ", StringComparison.Ordinal);

    private static async Task WaitForPortAsync(ServerHost host, CancellationToken cancellationToken)
    {
        for (int i = 0; i < 200 && host.ListenPort == 0; i++)
        {
            await Task.Delay(10, cancellationToken);
        }

        Assert.True(host.ListenPort > 0);
    }

    private sealed class LineClient : IAsyncDisposable
    {
        private readonly TcpClient _tcp;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private readonly List<string> _inbox = new();

        private LineClient(TcpClient tcp, StreamReader reader, StreamWriter writer)
        {
            _tcp = tcp;
            _reader = reader;
            _writer = writer;
        }

        public static async Task<LineClient> ConnectAsync(int port, CancellationToken cancellationToken)
        {
            var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
            NetworkStream stream = tcp.GetStream();
            var reader = new StreamReader(stream, Protocol.Utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            var writer = new StreamWriter(stream, Protocol.Utf8, bufferSize: 1024, leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\n"
            };
            return new LineClient(tcp, reader, writer);
        }

        public Task SendAsync(string line)
        {
            return _writer.WriteLineAsync(line);
        }

        public void IgnoreUntilNow()
        {
            _inbox.Clear();
        }

        public async Task<string> WaitForAsync(Func<string, bool> match, CancellationToken cancellationToken)
        {
            for (int i = 0; i < _inbox.Count; i++)
            {
                if (match(_inbox[i]))
                {
                    string hit = _inbox[i];
                    _inbox.RemoveAt(i);
                    return hit;
                }
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await _reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    throw new IOException("disconnected");
                }

                if (match(line))
                {
                    return line;
                }

                _inbox.Add(line);
            }

            throw new OperationCanceledException(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            _writer.Dispose();
            _reader.Dispose();
            _tcp.Dispose();
            await Task.CompletedTask;
        }
    }
}
