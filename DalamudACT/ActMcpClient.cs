using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace DalamudACT;

internal static class ActMcpClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    public static async Task<ActMcpEncounterSnapshot?> TryGetEncounterAsync(
        string pipeName,
        string? selfName,
        int connectTimeoutMs,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pipeName)) return null;

        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(Math.Clamp(connectTimeoutMs, 50, 5000));
            await pipe.ConnectAsync(connectCts.Token).ConfigureAwait(false);

            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            var request = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "act/status",
            };

            var requestJson = request.ToJsonString(JsonOptions);
            await writer.WriteLineAsync(requestJson).ConfigureAwait(false);

            var respLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(respLine)) return null;

            respLine = SanitizeNamedFloatingPointLiterals(respLine);
            var node = JsonNode.Parse(respLine) as JsonObject;
            var result = node?["result"] as JsonObject;
            var encounter = result?["encounter"] as JsonObject;
            if (encounter == null) return null;

            var zone = encounter["zone"]?.GetValue<string>() ?? string.Empty;
            var title = encounter["title"]?.GetValue<string>() ?? string.Empty;
            var startTimeMs = ParseIsoToUnixMs(encounter["startTime"]?.GetValue<string>());
            var endTimeMs = ParseIsoToUnixMs(encounter["endTime"]?.GetValue<string>());

            var combatants = new Dictionary<string, ActMcpCombatantSnapshot>(StringComparer.Ordinal);
            if (encounter["combatants"] is JsonArray list)
            {
                foreach (var item in list)
                {
                    if (item is not JsonObject c) continue;
                    var name = c["name"]?.GetValue<string>() ?? string.Empty;
                    if (string.Equals(name, "YOU", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(selfName))
                        name = selfName!;

                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var damage = GetLong(c["damage"]);
                    var dotDamage = GetLong(c["dotDamage"]);
                    var encdps = GetDouble(c["encdps"]);
                    var dps = GetDouble(c["dps"]);
                    combatants[name] = new ActMcpCombatantSnapshot(damage, dotDamage, encdps, dps);
                }
            }

            return new ActMcpEncounterSnapshot(zone, title, startTimeMs, endTimeMs, combatants);
        }
        catch
        {
            return null;
        }
    }

    private static long ParseIsoToUnixMs(string? iso8601)
    {
        if (string.IsNullOrWhiteSpace(iso8601)) return 0;
        return DateTimeOffset.TryParse(iso8601, out var dto) ? dto.ToUnixTimeMilliseconds() : 0;
    }

    private static long GetLong(JsonNode? node)
    {
        try
        {
            if (node is JsonValue v && v.TryGetValue<long>(out var l)) return l;
            if (node is JsonValue v2 && v2.TryGetValue<int>(out var i)) return i;
            return long.TryParse(node?.ToString(), out var parsed) ? parsed : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static double GetDouble(JsonNode? node)
    {
        try
        {
            if (node is JsonValue v && v.TryGetValue<double>(out var d)) return SanitizeDouble(d);
            if (node is JsonValue v2 && v2.TryGetValue<float>(out var f)) return SanitizeDouble(f);
            return double.TryParse(node?.ToString(), out var parsed) ? SanitizeDouble(parsed) : 0d;
        }
        catch
        {
            return 0d;
        }
    }

    private static double SanitizeDouble(double value)
        => double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;

    private static string SanitizeNamedFloatingPointLiterals(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;

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
}
