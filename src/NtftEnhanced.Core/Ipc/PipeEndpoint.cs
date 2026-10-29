using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace NtftEnhanced.Ipc;

public static class PipeEndpoint
{
    public static async Task ServeAsync(string pipeName, Func<string, string> handle, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = Create(pipeName);
            }
            catch (IOException)
            {
                await Task.Delay(200, token).ConfigureAwait(false);
                continue;
            }

            using (server)
            {
                try
                {
                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                    using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true);
                    using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
                    var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(line))
                        continue;
                    string response;
                    try
                    {
                        response = line.Length > 32_768
                            ? "{\"ok\":false,\"message\":\"İstek çok uzun.\"}"
                            : handle(line);
                    }
                    catch (Exception)
                    {
                        response = "{\"ok\":false,\"message\":\"İşlem tamamlanamadı. Servis açık kaldı.\"}";
                    }
                    await writer.WriteLineAsync(response.AsMemory(), token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException or IOException)
                {
                    if (token.IsCancellationRequested)
                        break;
                }
            }
        }
    }

    public static async Task<string?> RequestAsync(string pipeName, string line, CancellationToken token)
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(3000, token).ConfigureAwait(false);
        using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, leaveOpen: true);
        using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(line.AsMemory(), token).ConfigureAwait(false);
        return await reader.ReadLineAsync(token).ConfigureAwait(false);
    }

    private static NamedPipeServerStream Create(string pipeName)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            security);
    }
}
