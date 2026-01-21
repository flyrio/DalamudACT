using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Advanced_Combat_Tracker;

namespace ActMcpBridge;

public sealed class McpBridgePlugin : IActPluginV1
{
    private const string DefaultPipeName = "act-diemoe-mcp";
    private const int DefaultStatusTopCombatants = 20;

    private Label? pluginStatusText;
    private SynchronizationContext? uiContext;

    private readonly FixedSizeLogBuffer logBuffer = new(200);
    private PipeRpcServer? rpcServer;
    private string pipeName = DefaultPipeName;

    private Label? uiPipeLabel;
    private Label? uiServerLabel;

    public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
    {
        this.pluginStatusText = pluginStatusText;
        uiContext = SynchronizationContext.Current;
        pipeName = ReadPipeName();

        BuildUi(pluginScreenSpace);

        UpdatePluginStatus($"启动中… (pipe: {pipeName})");
        SafeWriteInfoLog($"[ACT.McpBridge] 启动中 pipe={pipeName}");

        rpcServer = new PipeRpcServer(pipeName, HandleRpcRequest, LogLine);
        rpcServer.Start();

        UpdateUiServerState(isRunning: true);
        UpdatePluginStatus($"已启动 (pipe: {pipeName})");
        SafeWriteInfoLog("[ACT.McpBridge] 已启动");
    }

    public void DeInitPlugin()
    {
        try
        {
            rpcServer?.Dispose();
        }
        catch (Exception e)
        {
            SafeWriteExceptionLog(e, "[ACT.McpBridge] DeInitPlugin dispose failed.");
        }

        rpcServer = null;
        UpdateUiServerState(isRunning: false);
        UpdatePluginStatus("已停止");
        SafeWriteInfoLog("[ACT.McpBridge] 已停止");
    }

    private static string ReadPipeName()
    {
        var env = Environment.GetEnvironmentVariable("ACT_MCP_PIPE");
        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim();

        return DefaultPipeName;
    }

    private void BuildUi(TabPage tab)
    {
        tab.Controls.Clear();

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            AutoSize = true,
            AutoScroll = true,
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        panel.Controls.Add(new Label { Text = "Pipe:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        uiPipeLabel = new Label { Text = pipeName, AutoSize = true, Anchor = AnchorStyles.Left };
        panel.Controls.Add(uiPipeLabel, 1, 0);

        panel.Controls.Add(new Label { Text = "服务状态:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        uiServerLabel = new Label { Text = "未启动", AutoSize = true, Anchor = AnchorStyles.Left };
        panel.Controls.Add(uiServerLabel, 1, 1);

        var copyButton = new Button { Text = "复制 Pipe 名称", AutoSize = true };
        copyButton.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(pipeName);
                SafeWriteInfoLog($"[ACT.McpBridge] 已复制 Pipe 名称: {pipeName}");
            }
            catch (Exception e)
            {
                SafeWriteExceptionLog(e, "[ACT.McpBridge] Clipboard copy failed.");
            }
        };
        panel.Controls.Add(copyButton, 1, 2);

        var testButton = new Button { Text = "写入测试日志", AutoSize = true };
        testButton.Click += (_, _) =>
        {
            try
            {
                SafeWriteInfoLog("[ACT.McpBridge] 测试日志：插件 UI 按钮触发。");
            }
            catch (Exception e)
            {
                SafeWriteExceptionLog(e, "[ACT.McpBridge] Test log failed.");
            }
        };
        panel.Controls.Add(testButton, 1, 3);

        var hint = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Text = "提示：MCP 客户端启动后会通过 stdio 拉起 ACT.McpServer，再通过 Named Pipe 与本插件通讯。",
        };
        panel.Controls.Add(hint, 0, 4);
        panel.SetColumnSpan(hint, 2);

        tab.Controls.Add(panel);
    }

    private void UpdatePluginStatus(string text)
    {
        if (pluginStatusText == null)
            return;

        void Update()
        {
            if (pluginStatusText != null)
                pluginStatusText.Text = $"ACT MCP Bridge: {text}";
        }

        if (uiContext != null)
            uiContext.Post(_ => Update(), null);
        else
            Update();
    }

    private void UpdateUiServerState(bool isRunning)
    {
        if (uiServerLabel == null)
            return;

        void Update()
        {
            if (uiServerLabel != null)
                uiServerLabel.Text = isRunning ? "运行中" : "未启动";
        }

        if (uiContext != null)
            uiContext.Post(_ => Update(), null);
        else
            Update();
    }

    private void LogLine(string line)
    {
        logBuffer.Add(line);
    }

    private Dictionary<string, object?> HandleRpcRequest(string method, Dictionary<string, object?>? @params)
    {
        switch (method)
        {
            case "ping":
                return new Dictionary<string, object?>();
            case "act/status":
                return BuildStatus();
            case "act/notify":
                HandleNotify(@params);
                return new Dictionary<string, object?>();
            case "act/log/tail":
                return HandleLogTail(@params);
            default:
                throw new PipeRpcException(-32601, $"Unknown method: {method}");
        }
    }

    private Dictionary<string, object?> BuildStatus()
    {
        var asm = Assembly.GetExecutingAssembly();
        var pluginVersion = asm.GetName().Version?.ToString() ?? "unknown";
        var pluginLocation = string.Empty;
        try
        {
            pluginLocation = asm.Location ?? string.Empty;
        }
        catch
        {
            // ignored
        }

        var actVersion = "unknown";
        try
        {
            actVersion = ActGlobals.oFormActMain?.ProductVersion ?? "unknown";
        }
        catch
        {
            // ignored
        }

        var status = new Dictionary<string, object?>
        {
            ["actVersion"] = actVersion,
            ["pluginVersion"] = pluginVersion,
            ["pipeName"] = pipeName,
            ["time"] = DateTimeOffset.Now.ToString("O"),
            ["pluginLocation"] = pluginLocation,
        };

        var encounter = BuildEncounterSnapshot(DefaultStatusTopCombatants);
        if (encounter != null)
            status["encounter"] = encounter;

        return status;
    }

    private void HandleNotify(Dictionary<string, object?>? @params)
    {
        var message = GetString(@params, "message") ?? string.Empty;
        message = message.Trim();
        if (message.Length == 0)
            throw new PipeRpcException(-32602, "Missing 'message'.");

        if (message.Length > 1000)
            message = message.Substring(0, 1000) + "…";

        if (TryHandleMcpCommand(message))
            return;

        var level = GetString(@params, "level")?.Trim().ToLowerInvariant() ?? "info";
        var prefix = "[ACT.McpBridge]";
        var text = $"{prefix} {message}";

        switch (level)
        {
            case "debug":
                SafeWriteDebugLog(text);
                break;
            case "warn":
            case "warning":
                SafeWriteInfoLog($"{prefix} [WARN] {message}");
                break;
            case "error":
                SafeWriteInfoLog($"{prefix} [ERROR] {message}");
                break;
            default:
                SafeWriteInfoLog(text);
                break;
        }
    }

    private bool TryHandleMcpCommand(string message)
    {
        // 通过 act_notify 触发一次性的调试输出（便于 MCP 客户端用 act_log_tail 读取）。
        // 格式：mcp:stats [top=N]
        if (!message.StartsWith("mcp:", StringComparison.OrdinalIgnoreCase))
            return false;

        var cmd = message.Substring("mcp:".Length).Trim();
        if (cmd.Length == 0)
            return false;

        if (cmd.StartsWith("stats", StringComparison.OrdinalIgnoreCase))
        {
            var top = ParseTopArg(cmd, DefaultStatusTopCombatants);
            DumpEncounterStatsToLog(top);
            return true;
        }

        return false;
    }

    private int ParseTopArg(string cmd, int defaultTop)
    {
        try
        {
            var parts = cmd.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (!part.StartsWith("top=", StringComparison.OrdinalIgnoreCase))
                    continue;

                var raw = part.Substring("top=".Length).Trim();
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    return Math.Max(1, Math.Min(200, parsed));
            }
        }
        catch
        {
            // ignored
        }

        return defaultTop;
    }

    private void DumpEncounterStatsToLog(int topCombatants)
    {
        try
        {
            var snapshot = BuildEncounterSnapshot(topCombatants);
            if (snapshot == null)
            {
                SafeWriteInfoLog("[ACT.McpBridge] EncounterStats: (no active encounter)");
                return;
            }

            var title = snapshot.TryGetValue("title", out var t) ? (t?.ToString() ?? "") : "";
            var zone = snapshot.TryGetValue("zone", out var z) ? (z?.ToString() ?? "") : "";
            var duration = snapshot.TryGetValue("durationS", out var d) ? (d?.ToString() ?? "") : "";

            SafeWriteInfoLog($"[ACT.McpBridge] EncounterStats: {title} ({zone}) {duration}");

            if (snapshot.TryGetValue("combatants", out var listObj) && listObj is List<object?> list && list.Count > 0)
            {
                var lines = new List<string>(Math.Min(list.Count, topCombatants));
                foreach (var item in list)
                {
                    if (item is not Dictionary<string, object?> c) continue;
                    var name = c.TryGetValue("name", out var n) ? (n?.ToString() ?? "") : "";
                    var damage = c.TryGetValue("damage", out var dmg) ? (dmg?.ToString() ?? "") : "";
                    var encdps = c.TryGetValue("encdps", out var edps) ? (edps?.ToString() ?? "") : "";
                    lines.Add($"[ACT.McpBridge]  - {name}: dmg={damage} encdps={encdps}");
                    if (lines.Count >= topCombatants) break;
                }

                foreach (var line in lines)
                    SafeWriteInfoLog(line);
            }
        }
        catch (Exception e)
        {
            SafeWriteExceptionLog(e, "[ACT.McpBridge] DumpEncounterStats failed.");
        }
    }

    private Dictionary<string, object?>? BuildEncounterSnapshot(int topCombatants)
    {
        return RunOnUiThread(() =>
        {
            try
            {
                var form = ActGlobals.oFormActMain;
                var zone = form?.ActiveZone;
                var encounter = zone?.ActiveEncounter;
                if (encounter == null)
                    return null;

                var snapshot = new Dictionary<string, object?>
                {
                    ["zone"] = zone?.ZoneName ?? string.Empty,
                    ["title"] = encounter.Title ?? string.Empty,
                    ["durationS"] = encounter.DurationS ?? string.Empty,
                    ["startTime"] = encounter.StartTime.ToString("O"),
                    ["endTime"] = encounter.EndTime.ToString("O"),
                };

                var combatants = new List<Dictionary<string, object?>>();
                foreach (var kv in encounter.Items)
                {
                    var c = kv.Value;
                    if (c == null) continue;

                    var combatantSnapshot = BuildCombatantSnapshot(c);
                    if (combatantSnapshot != null)
                        combatants.Add(combatantSnapshot);
                }

                if (combatants.Count > 1)
                {
                    combatants.Sort((a, b) =>
                    {
                        var ad = GetLong(a, "damage");
                        var bd = GetLong(b, "damage");
                        return bd.CompareTo(ad);
                    });
                }

                if (topCombatants > 0 && combatants.Count > topCombatants)
                    combatants = combatants.GetRange(0, topCombatants);

                snapshot["combatants"] = combatants.ConvertAll(static x => (object?)x);
                return snapshot;
            }
            catch
            {
                return null;
            }
        }, fallback: null);
    }

    private Dictionary<string, object?>? BuildCombatantSnapshot(CombatantData combatant)
    {
        try
        {
            var damage = GetLongMember(combatant, "Damage") ?? GetCombatantItemAsLong(combatant, "damage");
            var encdps = GetDoubleMember(combatant, "EncDPS") ?? GetCombatantItemAsDouble(combatant, "encdps");
            var dps = GetDoubleMember(combatant, "DPS") ?? GetCombatantItemAsDouble(combatant, "dps");

            return new Dictionary<string, object?>
            {
                ["name"] = combatant.Name ?? string.Empty,
                ["damage"] = damage ?? 0L,
                ["encdps"] = encdps ?? 0d,
                ["dps"] = dps ?? 0d,
            };
        }
        catch
        {
            return null;
        }
    }

    private static long? GetLongMember(object instance, string name)
    {
        try
        {
            var prop = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (prop == null) return null;
            var value = prop.GetValue(instance);
            if (value is long l) return l;
            if (value is int i) return i;
            if (value is double d) return (long)d;
            return ParseLong(value?.ToString());
        }
        catch
        {
            return null;
        }
    }

    private static double? GetDoubleMember(object instance, string name)
    {
        try
        {
            var prop = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (prop == null) return null;
            var value = prop.GetValue(instance);
            if (value is double d) return d;
            if (value is float f) return f;
            if (value is long l) return l;
            if (value is int i) return i;
            return ParseDouble(value?.ToString());
        }
        catch
        {
            return null;
        }
    }

    private static long? GetCombatantItemAsLong(CombatantData combatant, string keyName)
        => GetCombatantItem(combatant, keyName, ParseLong);

    private static double? GetCombatantItemAsDouble(CombatantData combatant, string keyName)
        => GetCombatantItem(combatant, keyName, ParseDouble);

    private static T? GetCombatantItem<T>(CombatantData combatant, string keyName, Func<string?, T?> parser) where T : struct
    {
        try
        {
            var itemsProp = combatant.GetType().GetProperty("Items", BindingFlags.Instance | BindingFlags.Public);
            if (itemsProp == null) return null;

            var itemsObj = itemsProp.GetValue(combatant);
            if (itemsObj is not System.Collections.IDictionary dict) return null;

            var normalized = keyName.Trim().ToLowerInvariant();
            foreach (System.Collections.DictionaryEntry entry in dict)
            {
                var k = entry.Key;
                if (k == null) continue;

                var kName = GetDictionaryKeyName(k);
                if (string.IsNullOrWhiteSpace(kName)) continue;
                if (!string.Equals(kName.Trim(), normalized, StringComparison.OrdinalIgnoreCase))
                    continue;

                return parser(entry.Value?.ToString());
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetDictionaryKeyName(object key)
    {
        if (key is string s) return s;

        try
        {
            var t = key.GetType();
            foreach (var propName in new[] { "Name", "Tag", "Text", "Label", "InternalName" })
            {
                var prop = t.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public);
                if (prop?.PropertyType != typeof(string)) continue;
                if (prop.GetValue(key) is string value && !string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }
        catch
        {
            // ignored
        }

        return key.ToString();
    }

    private T RunOnUiThread<T>(Func<T> func, T fallback)
    {
        try
        {
            if (uiContext == null)
                return func();

            if (SynchronizationContext.Current == uiContext)
                return func();

            T result = fallback;
            uiContext.Send(_ =>
            {
                try
                {
                    result = func();
                }
                catch
                {
                    result = fallback;
                }
            }, null);
            return result;
        }
        catch
        {
            return fallback;
        }
    }

    private static long GetLong(Dictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v) || v == null) return 0;
        if (v is long l) return l;
        if (v is int i) return i;
        if (v is double d) return (long)d;
        return ParseLong(v.ToString()) ?? 0;
    }

    private static long? ParseLong(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        raw = raw.Trim();
        if (raw is "-" or "∞") return null;

        raw = raw.Replace(",", "");
        var factor = 1d;
        if (raw.EndsWith("k", StringComparison.OrdinalIgnoreCase))
        {
            factor = 1000d;
            raw = raw.Substring(0, raw.Length - 1);
        }
        else if (raw.EndsWith("m", StringComparison.OrdinalIgnoreCase))
        {
            factor = 1000_000d;
            raw = raw.Substring(0, raw.Length - 1);
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return (long)Math.Round(parsed * factor);

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
            return (long)Math.Round(parsed * factor);

        return null;
    }

    private static double? ParseDouble(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        raw = raw.Trim();
        if (raw is "-" or "∞") return null;

        raw = raw.Replace(",", "");
        var factor = 1d;
        if (raw.EndsWith("k", StringComparison.OrdinalIgnoreCase))
        {
            factor = 1000d;
            raw = raw.Substring(0, raw.Length - 1);
        }
        else if (raw.EndsWith("m", StringComparison.OrdinalIgnoreCase))
        {
            factor = 1000_000d;
            raw = raw.Substring(0, raw.Length - 1);
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed * factor;

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
            return parsed * factor;

        return null;
    }

    private Dictionary<string, object?> HandleLogTail(Dictionary<string, object?>? @params)
    {
        var count = GetInt(@params, "count") ?? 50;
        count = Math.Max(1, Math.Min(200, count));

        var lines = logBuffer.Tail(count);
        return new Dictionary<string, object?>
        {
            ["lines"] = lines,
        };
    }

    private static string? GetString(Dictionary<string, object?>? dict, string key)
    {
        if (dict == null) return null;
        if (!dict.TryGetValue(key, out var value)) return null;
        return value?.ToString();
    }

    private static int? GetInt(Dictionary<string, object?>? dict, string key)
    {
        if (dict == null) return null;
        if (!dict.TryGetValue(key, out var value) || value == null) return null;

        if (value is int i) return i;
        if (value is long l) return checked((int)l);
        if (value is double d) return (int)d;

        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private void SafeWriteInfoLog(string text)
    {
        try
        {
            ActGlobals.oFormActMain?.WriteInfoLog(text);
        }
        catch
        {
            // ignored
        }

        LogLine(text);
    }

    private void SafeWriteDebugLog(string text)
    {
        try
        {
            ActGlobals.oFormActMain?.WriteDebugLog(text);
        }
        catch
        {
            // ignored
        }

        LogLine(text);
    }

    private void SafeWriteExceptionLog(Exception exception, string text)
    {
        try
        {
            ActGlobals.oFormActMain?.WriteExceptionLog(exception, text);
        }
        catch
        {
            // ignored
        }

        LogLine($"{text} {exception.GetType().Name}: {exception.Message}");
    }
}
