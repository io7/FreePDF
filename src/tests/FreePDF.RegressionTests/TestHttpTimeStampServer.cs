// A loopback HTTP endpoint for the signer's built-in time-stamp client: it answers one RFC 3161 POST through a
// TestTimeStampAuthority. Plain HTTP/1.1 over a TcpListener, so the tests need no web server. Original test code; MIT,
// see LICENSE.
#if NET8_0_OR_GREATER
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FreePDF.RegressionTests;

sealed class TestHttpTimeStampServer : IDisposable
{
    readonly TestTimeStampAuthority _authority;
    readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public TestHttpTimeStampServer(TestTimeStampAuthority authority)
    {
        _authority = authority;
        _listener.Start();
        Uri = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/tsa");
    }

    public Uri Uri { get; }

    /// <summary>The Content-Type of the request that was served.</summary>
    public string? ContentType { get; private set; }

    public async Task ServeOnceAsync()
    {
        using var client = await _listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(30));
        using var stream = client.GetStream();

        var head = await ReadHeadAsync(stream);
        var headers = head.Split("\r\n").Skip(1)
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
        ContentType = headers.GetValueOrDefault("Content-Type");
        var body = new byte[int.Parse(headers["Content-Length"])];
        await stream.ReadExactlyAsync(body);

        var reply = await _authority.RespondAsync(body);
        var responseHead = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: application/timestamp-reply\r\n" +
            $"Content-Length: {reply.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(responseHead);
        await stream.WriteAsync(reply);
    }

    // The request line and headers, up to the empty line.
    static async Task<string> ReadHeadAsync(NetworkStream stream)
    {
        var head = new List<byte>();
        var one = new byte[1];
        while (head.Count < 4 || head[^4] != '\r' || head[^3] != '\n' || head[^2] != '\r' || head[^1] != '\n')
        {
            if (await stream.ReadAsync(one) == 0)
                throw new IOException("The request ended before its headers did.");
            head.Add(one[0]);
        }
        return Encoding.ASCII.GetString(head.ToArray(), 0, head.Count - 4);
    }

    public void Dispose() => _listener.Stop();
}
#endif
