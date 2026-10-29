using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NtftEnhanced.Proxy;

public interface IProxyHost
{
    bool IsRunning { get; }
    int Port { get; }
    Task StartAsync(Func<string, bool> isBlocked, Action onBlocked);
    Task StopAsync();
}

public sealed class LocalProxy : IProxyHost
{
    private readonly object _gate = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public bool IsRunning { get; private set; }
    public int Port { get; private set; }

    public Task StartAsync(Func<string, bool> isBlocked, Action onBlocked)
    {
        lock (_gate)
        {
            if (IsRunning)
                return Task.CompletedTask;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _acceptLoop = Task.Run(() => AcceptLoop(_listener, isBlocked, onBlocked, token));
            IsRunning = true;
            return Task.CompletedTask;
        }
    }

    public async Task StopAsync()
    {
        TcpListener? listener;
        CancellationTokenSource? cts;
        Task? loop;
        lock (_gate)
        {
            listener = _listener;
            cts = _cts;
            loop = _acceptLoop;
            _listener = null;
            _cts = null;
            _acceptLoop = null;
            IsRunning = false;
        }

        try
        {
            cts?.Cancel();
            listener?.Stop();
        }
        catch (SocketException)
        {
        }

        if (loop is not null)
        {
            try
            {
                await loop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
            {
            }
        }
        cts?.Dispose();
    }

    private static async Task AcceptLoop(TcpListener listener, Func<string, bool> isBlocked, Action onBlocked, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                break;
            }
            _ = Task.Run(() => Handle(client, isBlocked, onBlocked, token));
        }
    }

    private static async Task Handle(TcpClient client, Func<string, bool> isBlocked, Action onBlocked, CancellationToken token)
    {
        using (client)
        {
            client.ReceiveTimeout = 5000;
            client.SendTimeout = 5000;
            using var stream = client.GetStream();
            var header = await ReadHeaderAsync(stream, token).ConfigureAwait(false);
            if (header is null || !ConnectRequest.TryGetHost(header, out var host))
            {
                await WriteAsync(stream, "HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\nConnection: close\r\n\r\n", token).ConfigureAwait(false);
                return;
            }

            if (isBlocked(host))
            {
                onBlocked();
                await WriteAsync(stream, "HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n", token).ConfigureAwait(false);
                return;
            }

            if (!header.StartsWith("CONNECT ", StringComparison.OrdinalIgnoreCase))
            {
                await WriteAsync(stream, "HTTP/1.1 405 Method Not Allowed\r\nContent-Length: 0\r\nConnection: close\r\n\r\n", token).ConfigureAwait(false);
                return;
            }

            var port = PortOf(header) ?? 443;
            TcpClient remote;
            try
            {
                remote = new TcpClient();
                await remote.ConnectAsync(host, port, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                await WriteAsync(stream, "HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\nConnection: close\r\n\r\n", token).ConfigureAwait(false);
                return;
            }

            using (remote)
            {
                await WriteAsync(stream, "HTTP/1.1 200 Connection Established\r\n\r\n", token).ConfigureAwait(false);
                using var remoteStream = remote.GetStream();
                var left = CopyAsync(stream, remoteStream, token);
                var right = CopyAsync(remoteStream, stream, token);
                await Task.WhenAny(left, right).ConfigureAwait(false);
            }
        }
    }

    private static int? PortOf(string header)
    {
        var lineEnd = header.IndexOf("\r\n", StringComparison.Ordinal);
        var first = lineEnd >= 0 ? header[..lineEnd] : header;
        var parts = first.Split(' ');
        if (parts.Length < 2)
            return null;
        var value = parts[1];
        if (value.StartsWith('['))
        {
            var close = value.IndexOf(']');
            if (close >= 0 && close + 2 < value.Length && int.TryParse(value[(close + 2)..], out var bracketPort))
                return bracketPort;
            return null;
        }
        var colon = value.LastIndexOf(':');
        return colon > 0 && int.TryParse(value[(colon + 1)..], out var port) ? port : null;
    }

    private static async Task<string?> ReadHeaderAsync(NetworkStream stream, CancellationToken token)
    {
        var buffer = new byte[8192];
        var used = 0;
        while (used < buffer.Length)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer.AsMemory(used), token).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return null;
            }
            if (read == 0)
                break;
            used += read;
            var text = Encoding.ASCII.GetString(buffer, 0, used);
            if (text.Contains("\r\n\r\n", StringComparison.Ordinal))
                return text;
        }
        return null;
    }

    private static Task WriteAsync(NetworkStream stream, string text, CancellationToken token) =>
        stream.WriteAsync(Encoding.ASCII.GetBytes(text), token).AsTask();

    private static async Task CopyAsync(Stream from, Stream to, CancellationToken token)
    {
        var buffer = new byte[8192];
        try
        {
            while (!token.IsCancellationRequested)
            {
                var read = await from.ReadAsync(buffer, token).ConfigureAwait(false);
                if (read == 0)
                    break;
                await to.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }
}
