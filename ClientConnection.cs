using System.Net.Sockets;

namespace SandboxServer;

/// <summary>
/// One TCP session. This is not a player. A player id is assigned only after
/// a successful JOIN. Framing and send serialization live here; game rules do not.
/// </summary>
internal sealed class ClientConnection
{
    private const int ReadBufferSize = 1024;

    private readonly TcpClient _client;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly string _remote;
    private NetworkStream? _stream;
    private int _cleanupStarted;
    private int _playerId;
    private string? _currentWorldName;

    public ClientConnection(TcpClient client)
    {
        _client = client;
        _remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
    }

    public string Remote => _remote;

    public int? PlayerId
    {
        get
        {
            int id = Volatile.Read(ref _playerId);
            return id == 0 ? null : id;
        }
    }

    public bool IsCleanupStarted => Volatile.Read(ref _cleanupStarted) != 0;

    public void AssignPlayer(int playerId)
    {
        Volatile.Write(ref _playerId, playerId);
    }

    public string? CurrentWorldName => _currentWorldName;

    public void SetCurrentWorldName(string worldName)
    {
        _currentWorldName = worldName;
    }

    public string? ClearCurrentWorldName()
    {
        return Interlocked.Exchange(ref _currentWorldName, null);
    }

    public bool TryBeginCleanup()
    {
        return Interlocked.Exchange(ref _cleanupStarted, 1) == 0;
    }

    public async Task RunAsync(
        Func<string, CancellationToken, Task> onLine,
        CancellationToken cancellationToken)
    {
        ServerHost.Log($"Connected {_remote}");

        try
        {
            _stream = _client.GetStream();
            var pending = new List<byte>(ReadBufferSize);
            var buffer = new byte[ReadBufferSize];

            while (!cancellationToken.IsCancellationRequested && !IsCleanupStarted)
            {
                int read;
                try
                {
                    read = await _stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (read == 0)
                {
                    break;
                }

                for (int i = 0; i < read; i++)
                {
                    if (pending.Count >= Protocol.MaxLineBytes)
                    {
                        ServerHost.Log($"Oversized line from {_remote}; closing.");
                        return;
                    }

                    byte b = buffer[i];
                    if (b != (byte)'\n')
                    {
                        pending.Add(b);
                        continue;
                    }

                    if (pending.Count > 0 && pending[^1] == (byte)'\r')
                    {
                        pending.RemoveAt(pending.Count - 1);
                    }

                    string message;
                    try
                    {
                        message = Protocol.Utf8.GetString(pending.ToArray());
                    }
                    catch (ArgumentException)
                    {
                        ServerHost.Log($"Invalid UTF-8 from {_remote}; closing.");
                        return;
                    }

                    pending.Clear();

                    if (message.Length == 0)
                    {
                        continue;
                    }

                    await onLine(message, cancellationToken);
                    if (IsCleanupStarted)
                    {
                        return;
                    }
                }
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Close();
            ServerHost.Log($"Disconnected {_remote}");
        }
    }

    public async Task SendLineAsync(string line, CancellationToken cancellationToken)
    {
        NetworkStream? stream = _stream;
        if (stream is null || IsCleanupStarted)
        {
            throw new IOException("Connection is closed.");
        }

        byte[] bytes = Protocol.EncodeLine(line);
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (IsCleanupStarted)
            {
                throw new IOException("Connection is closed.");
            }

            await stream.WriteAsync(bytes, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Close()
    {
        try
        {
            _client.Close();
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
