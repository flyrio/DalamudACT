using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace ActMcpBridge.McpServer;

internal sealed class StdioMcpServer
{
    private const string ProtocolVersion = "2024-11-05";

    private readonly ActPipeRpcClient pipe;
    private readonly TextWriter log;
    private bool clientInitialized;

    public StdioMcpServer(ActPipeRpcClient pipe, TextWriter log)
    {
        this.pipe = pipe;
        this.log = log;
    }

    public async Task RunAsync(TextReader input, TextWriter output)
    {
        while (true)
        {
            var line = await input.ReadLineAsync().ConfigureAwait(false);
            if (line == null)
                return;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(line);
            }
            catch (Exception e)
            {
                await log.WriteLineAsync($"[ACT.McpServer] Invalid JSON: {e.Message}").ConfigureAwait(false);
                continue;
            }

            if (node is not JsonObject msg)
                continue;

            var method = msg["method"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(method))
                continue;

            var idNode = msg["id"];
            if (idNode == null)
            {
                HandleNotification(method);
                continue;
            }

            JsonObject response;
            try
            {
                response = await HandleRequestAsync(method, idNode, msg["params"]).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                response = Error(idNode, -32603, "Internal error", $"{e.GetType().Name}: {e.Message}");
            }

            await WriteMessageAsync(output, response).ConfigureAwait(false);
        }
    }

    private void HandleNotification(string method)
    {
        if (string.Equals(method, "notifications/initialized", StringComparison.OrdinalIgnoreCase))
            clientInitialized = true;
    }

    private async Task<JsonObject> HandleRequestAsync(string method, JsonNode id, JsonNode? @params)
    {
        if (string.Equals(method, "ping", StringComparison.OrdinalIgnoreCase))
            return Ok(id, new JsonObject());

        if (string.Equals(method, "initialize", StringComparison.OrdinalIgnoreCase))
            return Ok(id, BuildInitializeResult(@params as JsonObject));

        if (!clientInitialized && !string.Equals(method, "tools/list", StringComparison.OrdinalIgnoreCase))
        {
            // Spec allows ping before initialized; keep behavior strict for other methods.
            await log.WriteLineAsync("[ACT.McpServer] Client has not sent notifications/initialized yet.").ConfigureAwait(false);
        }

        if (string.Equals(method, "tools/list", StringComparison.OrdinalIgnoreCase))
            return Ok(id, BuildToolsListResult());

        if (string.Equals(method, "tools/call", StringComparison.OrdinalIgnoreCase))
            return Ok(id, await HandleToolsCallAsync(@params as JsonObject).ConfigureAwait(false));

        return Error(id, -32601, $"Method not found: {method}");
    }

    private static JsonObject BuildInitializeResult(JsonObject? requestParams)
    {
        var requestedVersion = requestParams?["protocolVersion"]?.GetValue<string>() ?? ProtocolVersion;
        var version = string.Equals(requestedVersion, ProtocolVersion, StringComparison.OrdinalIgnoreCase)
            ? requestedVersion
            : ProtocolVersion;

        var asm = Assembly.GetExecutingAssembly().GetName();
        var serverVersion = asm.Version?.ToString() ?? "0.0.0";

        return new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject
                {
                    ["listChanged"] = false,
                }
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "ACT.McpServer",
                ["version"] = serverVersion,
            }
        };
    }

    private static JsonObject BuildToolsListResult()
    {
        var tools = new JsonArray
        {
            Tool(
                "act_status",
                "获取 ACT/插件状态（版本、pipe 名称、时间等）",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject(),
                }),
            Tool(
                "act_notify",
                "向 ACT 主界面日志输出一条文本",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["message"] = new JsonObject { ["type"] = "string" },
                        ["level"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JsonArray { "info", "debug", "warn", "error" },
                        },
                    },
                    ["required"] = new JsonArray { "message" },
                }),
            Tool(
                "act_log_tail",
                "读取插件侧最近日志（内存缓冲）",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["count"] = new JsonObject
                        {
                            ["type"] = "integer",
                            ["minimum"] = 1,
                            ["maximum"] = 200,
                        },
                    }
                }),
        };

        return new JsonObject
        {
            ["tools"] = tools,
        };
    }

    private async Task<JsonObject> HandleToolsCallAsync(JsonObject? requestParams)
    {
        if (requestParams == null)
            return ToolError("Missing params.");

        var name = requestParams["name"]?.GetValue<string>();
        var args = requestParams["arguments"] as JsonObject;

        if (string.IsNullOrWhiteSpace(name))
            return ToolError("Missing tool name.");

        try
        {
            return name switch
            {
                "act_status" => await ToolActStatusAsync().ConfigureAwait(false),
                "act_notify" => await ToolActNotifyAsync(args).ConfigureAwait(false),
                "act_log_tail" => await ToolActLogTailAsync(args).ConfigureAwait(false),
                _ => ToolError($"Unknown tool: {name}"),
            };
        }
        catch (Exception e)
        {
            return ToolError($"{e.GetType().Name}: {e.Message}");
        }
    }

    private async Task<JsonObject> ToolActStatusAsync()
    {
        var result = await pipe.CallAsync("act/status", null).ConfigureAwait(false);
        var actVersion = result["actVersion"]?.GetValue<string>() ?? "unknown";
        var pluginVersion = result["pluginVersion"]?.GetValue<string>() ?? "unknown";
        var pipeName = result["pipeName"]?.GetValue<string>() ?? pipe.PipeName;
        var time = result["time"]?.GetValue<string>() ?? "";

        var lines = new List<string>
        {
            $"ACT: {actVersion}",
            $"插件: {pluginVersion}",
            $"Pipe: {pipeName}",
            $"时间: {time}",
        };

        if (result["encounter"] is JsonObject encounter)
        {
            var zone = encounter["zone"]?.GetValue<string>() ?? "";
            var title = encounter["title"]?.GetValue<string>() ?? "";
            var durationS = encounter["durationS"]?.GetValue<string>() ?? "";

            if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(zone) || !string.IsNullOrWhiteSpace(durationS))
                lines.Add($"遭遇: {title} ({zone}) {durationS}");

            if (encounter["combatants"] is JsonArray combatants && combatants.Count > 0)
            {
                var max = 8;
                for (var i = 0; i < combatants.Count && i < max; i++)
                {
                    if (combatants[i] is not JsonObject c) continue;
                    var name = c["name"]?.GetValue<string>() ?? "";
                    var damage = c["damage"]?.ToString() ?? "0";
                    var encdps = c["encdps"]?.ToString() ?? "0";
                    lines.Add($"  - {name}: dmg={damage} encdps={encdps}");
                }

                if (combatants.Count > max)
                    lines.Add($"  …({combatants.Count} combatants, showing {max})");
            }
        }
        else
        {
            lines.Add("遭遇: (none)");
        }

        return ToolText(string.Join("\n", lines));
    }

    private async Task<JsonObject> ToolActNotifyAsync(JsonObject? args)
    {
        var message = args?["message"]?.GetValue<string>() ?? string.Empty;
        var level = args?["level"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(message))
            return ToolError("message 不能为空。");

        var p = new JsonObject
        {
            ["message"] = message,
        };

        if (!string.IsNullOrWhiteSpace(level))
            p["level"] = level!;

        await pipe.CallAsync("act/notify", p).ConfigureAwait(false);
        return ToolText("已写入 ACT 日志。");
    }

    private async Task<JsonObject> ToolActLogTailAsync(JsonObject? args)
    {
        var count = 50;
        if (args?["count"] is JsonValue v && v.TryGetValue<int>(out var parsed))
            count = parsed;
        if (count is < 1 or > 200) count = 50;

        var p = new JsonObject { ["count"] = count };
        var result = await pipe.CallAsync("act/log/tail", p).ConfigureAwait(false);
        var lines = result["lines"] as JsonArray;

        var text = lines == null
            ? "(no logs)"
            : string.Join("\n", lines.Select(item =>
            {
                if (item is JsonValue jv && jv.TryGetValue<string>(out var s))
                    return s;
                return item?.ToString() ?? string.Empty;
            }));

        return ToolText(text);
    }

    private static JsonObject Tool(string name, string description, JsonObject inputSchema)
        => new()
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = inputSchema,
        };

    private static JsonObject ToolText(string text)
        => new()
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text,
                }
            },
            ["isError"] = false,
        };

    private static JsonObject ToolError(string message)
        => new()
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = message,
                }
            },
            ["isError"] = true,
        };

    private static JsonObject Ok(JsonNode id, JsonNode result)
        => new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["result"] = result,
        };

    private static JsonObject Error(JsonNode id, int code, string message, JsonNode? data = null)
    {
        var err = new JsonObject
        {
            ["code"] = code,
            ["message"] = message,
        };
        if (data != null) err["data"] = data;

        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["error"] = err,
        };
    }

    private static async Task WriteMessageAsync(TextWriter output, JsonObject message)
    {
        var json = message.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false,
        });
        await output.WriteLineAsync(json).ConfigureAwait(false);
        await output.FlushAsync().ConfigureAwait(false);
    }
}
