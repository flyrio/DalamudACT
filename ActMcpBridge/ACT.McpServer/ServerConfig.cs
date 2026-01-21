using System;

namespace ActMcpBridge.McpServer;

internal sealed class ServerConfig
{
    public string PipeName { get; }
    public int ConnectTimeoutMs { get; }

    private ServerConfig(string pipeName, int connectTimeoutMs)
    {
        PipeName = pipeName;
        ConnectTimeoutMs = connectTimeoutMs;
    }

    public static ServerConfig FromArgs(string[] args)
    {
        var pipeName = Environment.GetEnvironmentVariable("ACT_MCP_PIPE");
        var timeoutMsRaw = Environment.GetEnvironmentVariable("ACT_MCP_CONNECT_TIMEOUT_MS");

        var connectTimeoutMs = 800;
        if (int.TryParse(timeoutMsRaw, out var parsedTimeout) && parsedTimeout > 0)
            connectTimeoutMs = parsedTimeout;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--pipe", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                pipeName = args[++i];
                continue;
            }

            if (string.Equals(arg, "--connect-timeout-ms", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var v) && v > 0)
                    connectTimeoutMs = v;
            }
        }

        if (string.IsNullOrWhiteSpace(pipeName))
            pipeName = "act-diemoe-mcp";

        return new ServerConfig(pipeName.Trim(), connectTimeoutMs);
    }
}

