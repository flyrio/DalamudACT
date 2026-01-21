using System;
using System.IO;
using System.Threading.Tasks;

namespace ActMcpBridge.McpServer;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var config = ServerConfig.FromArgs(args);
        var pipeClient = new ActPipeRpcClient(config.PipeName, config.ConnectTimeoutMs, Console.Error);
        pipeClient.StartAutoConnect();

        var server = new StdioMcpServer(pipeClient, Console.Error);
        await server.RunAsync(Console.In, Console.Out).ConfigureAwait(false);

        pipeClient.Dispose();
        return 0;
    }
}

