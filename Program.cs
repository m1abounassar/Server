using System.Net;

namespace SandboxServer;

internal static class Program
{
    private const int DefaultPort = 7777;

    private static async Task<int> Main(string[] args)
    {
        if (!TryParseListenOptions(args, out IPAddress host, out int port, out string? error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            Console.WriteLine("Shutdown requested.");
            cts.Cancel();
        };

        try
        {
            ContentRuntime.EnsureLoaded();
            Console.WriteLine($"Content pack schema {ContentRuntime.SchemaVersion} revision {ContentRuntime.ContentRevision}.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var serverHost = new ServerHost(host, port);
        await serverHost.RunAsync(cts.Token);
        return 0;
    }

    private static bool TryParseListenOptions(
        string[] args,
        out IPAddress host,
        out int port,
        out string? error)
    {
        host = IPAddress.Loopback;
        port = DefaultPort;
        error = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--host":
                    if (i + 1 >= args.Length || !IPAddress.TryParse(args[i + 1], out IPAddress? parsedHost))
                    {
                        error = "Expected --host <IPv4-or-IPv6-address>.";
                        return false;
                    }

                    host = parsedHost;
                    i++;
                    break;

                case "--port":
                    if (i + 1 >= args.Length
                        || !int.TryParse(args[i + 1], out port)
                        || port is < 1 or > 65535)
                    {
                        error = "Expected --port <1-65535>.";
                        return false;
                    }

                    i++;
                    break;

                default:
                    error = $"Unknown argument: {args[i]}";
                    return false;
            }
        }

        return true;
    }
}
