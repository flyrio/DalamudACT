using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace ActMcpBridge.McpServer;

internal sealed class ActPipeRpcClient : IDisposable
{
    private readonly int connectTimeoutMs;
    private readonly TextWriter log;

    private readonly object gate = new();
    private NamedPipeClientStream? pipe;
    private StreamReader? reader;
    private StreamWriter? writer;
    private int nextId = 1;

    private CancellationTokenSource? autoConnectCts;
    private Task? autoConnectLoop;

    public string PipeName { get; }

    public ActPipeRpcClient(string pipeName, int connectTimeoutMs, TextWriter log)
    {
        PipeName = pipeName;
        this.connectTimeoutMs = Math.Max(50, connectTimeoutMs);
        this.log = log;
    }

    public void StartAutoConnect()
    {
        lock (gate)
        {
            if (autoConnectCts != null)
                return;

            autoConnectCts = new CancellationTokenSource();
            autoConnectLoop = Task.Run(() => AutoConnectLoopAsync(autoConnectCts.Token));
        }
    }

    private async Task AutoConnectLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (IsConnected())
                {
                    await Task.Delay(1500, token).ConfigureAwait(false);
                    continue;
                }

                TryConnectOnce();
                await Task.Delay(1500, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                await log.WriteLineAsync($"[ACT.McpServer] AutoConnect failed: {e.Message}").ConfigureAwait(false);
                await Task.Delay(2000, token).ConfigureAwait(false);
            }
        }
    }

    private bool IsConnected()
    {
        lock (gate)
        {
            return pipe != null && pipe.IsConnected;
        }
    }

    private void TryConnectOnce()
    {
        lock (gate)
        {
            if (pipe != null && pipe.IsConnected)
                return;
        }

        var client = new NamedPipeClientStream(
            ".",
            PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            client.Connect(connectTimeoutMs);
            var r = new StreamReader(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
            var w = new StreamWriter(client, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 4096, leaveOpen: true)
            {
                AutoFlush = true
            };

            lock (gate)
            {
                DisposeConnection_NoLock();
                pipe = client;
                reader = r;
                writer = w;
            }

            _ = log.WriteLineAsync($"[ACT.McpServer] Connected to pipe: {PipeName}");
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task<JsonObject> CallAsync(string method, JsonObject? @params)
    {
        if (string.IsNullOrWhiteSpace(method))
            throw new ArgumentException("method is empty.", nameof(method));

        try
        {
            if (!IsConnected())
                TryConnectOnce();
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"无法连接到 ACT 插件 Pipe: {PipeName} ({e.Message})", e);
        }

        JsonObject request;
        string requestJson;
        int id;
        StreamReader r;
        StreamWriter w;
        NamedPipeClientStream p;

        lock (gate)
        {
            if (pipe == null || writer == null || reader == null || !pipe.IsConnected)
                throw new InvalidOperationException($"Pipe not connected: {PipeName}");

            p = pipe;
            r = reader;
            w = writer;
            id = nextId++;

            request = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
            };
            if (@params != null)
                request["params"] = @params;

            requestJson = request.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }

        try
        {
            await w.WriteLineAsync(requestJson).ConfigureAwait(false);
            var respLine = await r.ReadLineAsync().ConfigureAwait(false);
            if (respLine == null)
                throw new IOException("Pipe closed.");

            respLine = SanitizeNamedFloatingPointLiterals(respLine);
            var node = JsonNode.Parse(respLine) as JsonObject;
            if (node == null)
                throw new IOException("Invalid response JSON.");

            var error = node["error"] as JsonObject;
            if (error != null)
            {
                var msg = error["message"]?.GetValue<string>() ?? "error";
                var data = error["data"]?.ToJsonString();
                throw new InvalidOperationException($"{msg}{(string.IsNullOrWhiteSpace(data) ? "" : $": {data}")}");
            }

            var result = node["result"] as JsonObject;
            return result ?? new JsonObject();
        }
        catch
        {
            lock (gate)
            {
                if (pipe == p)
                    DisposeConnection_NoLock();
            }
            throw;
        }
    }

    private static string SanitizeNamedFloatingPointLiterals(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;

        // System.Text.Json 的 JsonNode/JsonDocument 不支持 NaN/Infinity（非标准 JSON 数字）。
        // ACT 插件侧序列化在极端情况下可能输出这些 token；这里做“仅在非字符串区域”的安全替换。
        if (json.IndexOf("NaN", StringComparison.Ordinal) < 0 &&
            json.IndexOf("Infinity", StringComparison.Ordinal) < 0)
            return json;

        var sb = new StringBuilder(json.Length);
        var inString = false;
        var escaped = false;

        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];

            if (inString)
            {
                sb.Append(c);
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                    inString = false;

                continue;
            }

            if (c == '"')
            {
                inString = true;
                sb.Append(c);
                continue;
            }

            var span = json.AsSpan(i);

            if (span.StartsWith("NaN", StringComparison.Ordinal))
            {
                sb.Append('0');
                i += 2;
                continue;
            }

            if (span.StartsWith("Infinity", StringComparison.Ordinal))
            {
                sb.Append('0');
                i += 7;
                continue;
            }

            if (span.StartsWith("-Infinity", StringComparison.Ordinal) ||
                span.StartsWith("+Infinity", StringComparison.Ordinal))
            {
                sb.Append('0');
                i += 8;
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private void DisposeConnection_NoLock()
    {
        try { writer?.Dispose(); } catch { /* ignored */ }
        try { reader?.Dispose(); } catch { /* ignored */ }
        try { pipe?.Dispose(); } catch { /* ignored */ }

        writer = null;
        reader = null;
        pipe = null;
    }

    public void Dispose()
    {
        CancellationTokenSource? cts;
        Task? loop;

        lock (gate)
        {
            cts = autoConnectCts;
            loop = autoConnectLoop;
            autoConnectCts = null;
            autoConnectLoop = null;
        }

        try { cts?.Cancel(); } catch { /* ignored */ }
        try { loop?.Wait(TimeSpan.FromSeconds(1)); } catch { /* ignored */ }
        try { cts?.Dispose(); } catch { /* ignored */ }

        lock (gate)
        {
            DisposeConnection_NoLock();
        }
    }
}
