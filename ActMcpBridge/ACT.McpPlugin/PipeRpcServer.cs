using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ActMcpBridge;

internal sealed class PipeRpcServer : IDisposable
{
    private readonly string pipeName;
    private readonly Func<string, Dictionary<string, object?>?, Dictionary<string, object?>> handler;
    private readonly Action<string> log;

    private readonly JavaScriptSerializer serializer = new()
    {
        MaxJsonLength = 1024 * 1024,
    };

    private readonly object handlerGate = new();

    private readonly object gate = new();
    private CancellationTokenSource? cts;
    private Task? acceptLoop;
    private readonly HashSet<NamedPipeServerStream> servers = new();

    public PipeRpcServer(
        string pipeName,
        Func<string, Dictionary<string, object?>?, Dictionary<string, object?>> handler,
        Action<string> log)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
            throw new ArgumentException("pipeName is empty.", nameof(pipeName));

        this.pipeName = pipeName.Trim();
        this.handler = handler;
        this.log = log;
    }

    public void Start()
    {
        lock (gate)
        {
            if (cts != null)
                return;

            cts = new CancellationTokenSource();
            acceptLoop = Task.Run(() => RunAsync(cts.Token));
        }
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = CreateServerStream();
                lock (gate) servers.Add(server);

                await server.WaitForConnectionAsync().ConfigureAwait(false);

                var connectedPipe = server;
                _ = Task.Run(() => HandleClientAndDisposeAsync(connectedPipe, token));
                server = null;
            }
            catch (ObjectDisposedException)
            {
                // shutdown
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                log($"[ACT.McpBridge] Pipe server loop error: {e.GetType().Name}: {e.Message}");
                await Task.Delay(500, token).ConfigureAwait(false);
            }
            finally
            {
                if (server != null)
                {
                    try { server.Dispose(); } catch { /* ignored */ }
                    lock (gate) servers.Remove(server);
                }
            }
        }
    }

    private async Task HandleClientAndDisposeAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        try
        {
            await HandleClientAsync(pipe, token).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // shutdown / disconnect
        }
        catch (IOException)
        {
            // client disconnected
        }
        catch (Exception e)
        {
            log($"[ACT.McpBridge] Pipe client handler error: {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            try { pipe.Dispose(); } catch { /* ignored */ }
            lock (gate) servers.Remove(pipe);
        }
    }

    private NamedPipeServerStream CreateServerStream()
    {
        var security = CreatePipeSecurityForCurrentUser();
        const int maxInstances = NamedPipeServerStream.MaxAllowedServerInstances;
        if (security == null)
        {
            return new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                maxInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                4096,
                4096);
        }

        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            4096,
            4096,
            security);
    }

    private static PipeSecurity? CreatePipeSecurityForCurrentUser()
    {
        var security = new PipeSecurity();

        // Current user full control.
        var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User;
        if (sid == null)
            return null;

        security.AddAccessRule(new PipeAccessRule(
            sid,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return security;
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 4096, leaveOpen: true)
        {
            AutoFlush = true
        };

        while (!token.IsCancellationRequested && pipe.IsConnected)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line == null)
                return;

            if (line.Length == 0)
                continue;

            Dictionary<string, object?> response;
            try
            {
                response = HandleLine(line);
            }
            catch (Exception e)
            {
                response = new Dictionary<string, object?>
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = null,
                    ["error"] = new Dictionary<string, object?>
                    {
                        ["code"] = -32603,
                        ["message"] = "Internal error",
                        ["data"] = $"{e.GetType().Name}: {e.Message}",
                    }
                };
            }

            var json = serializer.Serialize(response);
            await writer.WriteLineAsync(json).ConfigureAwait(false);
        }
    }

    private Dictionary<string, object?> HandleLine(string line)
    {
        var parsed = serializer.DeserializeObject(line) as Dictionary<string, object?>;
        if (parsed == null)
            throw new PipeRpcException(-32600, "Invalid request.");

        parsed.TryGetValue("id", out var id);
        var method = parsed.TryGetValue("method", out var m) ? m?.ToString() : null;
        if (string.IsNullOrWhiteSpace(method))
            return Error(id, -32600, "Missing method.");

        Dictionary<string, object?>? @params = null;
        if (parsed.TryGetValue("params", out var p) && p is Dictionary<string, object?> dict)
            @params = dict;

        try
        {
            Dictionary<string, object?> result;
            lock (handlerGate)
                result = handler(method!, @params);
            return Ok(id, result);
        }
        catch (PipeRpcException e)
        {
            return Error(id, e.Code, e.Message, e.ErrorData);
        }
        catch (Exception e)
        {
            log($"[ACT.McpBridge] RPC handler error: {e.GetType().Name}: {e.Message}");
            return Error(id, -32603, "Internal error", $"{e.GetType().Name}: {e.Message}");
        }
    }

    private static Dictionary<string, object?> Ok(object? id, Dictionary<string, object?> result)
        => new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result,
        };

    private static Dictionary<string, object?> Error(object? id, int code, string message, object? data = null)
        => new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new Dictionary<string, object?>
            {
                ["code"] = code,
                ["message"] = message,
                ["data"] = data,
            },
        };

    public void Dispose()
    {
        CancellationTokenSource? source;
        Task? loop;
        NamedPipeServerStream[] allServers;

        lock (gate)
        {
            source = cts;
            loop = acceptLoop;
            cts = null;
            acceptLoop = null;
            allServers = servers.Count == 0
                ? Array.Empty<NamedPipeServerStream>()
                : new List<NamedPipeServerStream>(servers).ToArray();
            servers.Clear();
        }

        try
        {
            source?.Cancel();
        }
        catch
        {
            // ignored
        }

        try
        {
            foreach (var server in allServers)
            {
                try { server.Dispose(); } catch { /* ignored */ }
            }
        }
        catch
        {
            // ignored
        }

        try
        {
            loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignored
        }

        try
        {
            source?.Dispose();
        }
        catch
        {
            // ignored
        }
    }
}

internal sealed class PipeRpcException : Exception
{
    public int Code { get; }
    public object? ErrorData { get; }

    public PipeRpcException(int code, string message, object? data = null) : base(message)
    {
        Code = code;
        ErrorData = data;
    }
}
