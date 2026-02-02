using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading;

namespace DalamudACT;

internal sealed class ActDllProbeResult
{
    public bool Success { get; init; }
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> LogLines { get; init; } = Array.Empty<string>();
}

internal static class ActDllBridge
{
    private sealed class ActAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly string[] searchDirectories;

        public ActAssemblyLoadContext(IEnumerable<string> searchDirectories)
            : base(isCollectible: true)
        {
            this.searchDirectories = searchDirectories
                .Where(static d => !string.IsNullOrWhiteSpace(d))
                .Select(static d => d.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name;
            if (string.IsNullOrWhiteSpace(name)) return null;

            foreach (var dir in searchDirectories)
            {
                try
                {
                    var dll = Path.Combine(dir, name + ".dll");
                    if (File.Exists(dll))
                        return LoadFromAssemblyPath(dll);

                    var exe = Path.Combine(dir, name + ".exe");
                    if (File.Exists(exe))
                        return LoadFromAssemblyPath(exe);
                }
                catch
                {
                    // ignore and continue probing other directories
                }
            }

            return null;
        }
    }

    public static ActDllProbeResult Probe(
        string actRoot,
        bool tryInitPlugin,
        string? ffxivPluginFileName = "FFXIV_ACT_Plugin.dll")
    {
        var lines = new List<string>(64);
        var summary = new StringBuilder();

        try
        {
            if (string.IsNullOrWhiteSpace(actRoot))
            {
                return new ActDllProbeResult
                {
                    Success = false,
                    Summary = "actRoot 为空。",
                    LogLines = new[] { "[ActDllBridge] actRoot 为空。" },
                };
            }

            actRoot = actRoot.Trim();
            if (!Directory.Exists(actRoot))
            {
                return new ActDllProbeResult
                {
                    Success = false,
                    Summary = $"目录不存在：{actRoot}",
                    LogLines = new[] { $"[ActDllBridge] 目录不存在：{actRoot}" },
                };
            }

            var actDllPath = Path.Combine(actRoot, "DLibs", "Advanced Combat Tracker.dll");
            var ffxivDllPath = Path.Combine(actRoot, "Plugins", ffxivPluginFileName ?? "FFXIV_ACT_Plugin.dll");

            lines.Add($"[ActDllBridge] actRoot={actRoot}");
            lines.Add($"[ActDllBridge] actDll={actDllPath}");
            lines.Add($"[ActDllBridge] ffxivActPlugin={ffxivDllPath}");

            if (!File.Exists(actDllPath))
                lines.Add("[ActDllBridge] WARN: 未找到 ACT 主程序集（DLibs/Advanced Combat Tracker.dll）。");
            if (!File.Exists(ffxivDllPath))
                lines.Add("[ActDllBridge] WARN: 未找到 FFXIV_ACT_Plugin.dll（Plugins/）。");

            var searchDirs = new[]
            {
                actRoot,
                Path.Combine(actRoot, "DLibs"),
                Path.Combine(actRoot, "Plugins"),
                Path.Combine(actRoot, "Plugins", "SideLoad"),
                Path.Combine(actRoot, "LauncherPlugins"),
            }.Where(Directory.Exists).ToArray();

            lines.Add("[ActDllBridge] SearchDirs:");
            foreach (var d in searchDirs)
                lines.Add($"[ActDllBridge]  - {d}");

            var alc = new ActAssemblyLoadContext(searchDirs);
            try
            {

            Assembly? actAsm = null;
            if (File.Exists(actDllPath))
            {
                try
                {
                    actAsm = alc.LoadFromAssemblyPath(actDllPath);
                    lines.Add($"[ActDllBridge] OK: loaded ACT asm={actAsm.GetName().Name} v{actAsm.GetName().Version}");
                }
                catch (Exception e)
                {
                    lines.Add($"[ActDllBridge] ERR: load ACT failed: {e.GetType().Name}: {e.Message}");
                }
            }

            // 预加载 SideLoad（尽量贴近 ACT 的运行环境）
            foreach (var side in new[]
                     {
                         "FFXIV_ACT_Plugin.Parse.dll",
                         "FFXIV_ACT_Plugin.Network.dll",
                         "FFXIV_ACT_Plugin.Memory.dll",
                         "FFXIV_ACT_Plugin.Logfile.dll",
                         "FFXIV_ACT_Plugin.Resource.dll",
                         "Machina.FFXIV.dll",
                     })
            {
                var p = Path.Combine(actRoot, "Plugins", "SideLoad", side);
                if (!File.Exists(p)) continue;
                try
                {
                    var asm = alc.LoadFromAssemblyPath(p);
                    lines.Add($"[ActDllBridge] OK: loaded SideLoad {asm.GetName().Name} v{asm.GetName().Version}");
                }
                catch (Exception e)
                {
                    lines.Add($"[ActDllBridge] WARN: load SideLoad {side} failed: {e.GetType().Name}: {e.Message}");
                }
            }

            Assembly? ffxivAsm = null;
            if (File.Exists(ffxivDllPath))
            {
                try
                {
                    ffxivAsm = alc.LoadFromAssemblyPath(ffxivDllPath);
                    lines.Add($"[ActDllBridge] OK: loaded FFXIV_ACT_Plugin asm={ffxivAsm.GetName().Name} v{ffxivAsm.GetName().Version}");
                }
                catch (Exception e)
                {
                    lines.Add($"[ActDllBridge] ERR: load FFXIV_ACT_Plugin failed: {e.GetType().Name}: {e.Message}");
                }
            }

            var iActPlugin = actAsm == null ? null : FindTypeSafe(actAsm, "Advanced_Combat_Tracker.IActPluginV1", lines);
            if (iActPlugin != null)
                lines.Add("[ActDllBridge] OK: found interface Advanced_Combat_Tracker.IActPluginV1");
            else
                lines.Add("[ActDllBridge] WARN: cannot find Advanced_Combat_Tracker.IActPluginV1 (ACT not loaded or type missing).");

            Type? pluginType = null;
            if (ffxivAsm != null)
            {
                var types = GetTypesSafe(ffxivAsm, lines);
                var candidates = iActPlugin == null
                    ? types
                        .Where(static t => t != null && t.FullName != null && t.FullName.Contains("FFXIV", StringComparison.OrdinalIgnoreCase))
                        .Where(static t => t is { IsAbstract: false, IsInterface: false })
                        .Select(static t => t!)
                        .Take(10)
                        .ToList()
                    : types
                        .Where(t => t != null && iActPlugin.IsAssignableFrom(t))
                        .Where(static t => t is { IsAbstract: false, IsInterface: false })
                        .Select(static t => t!)
                        .ToList();

                if (candidates.Count > 0)
                {
                    pluginType = candidates[0];
                    lines.Add($"[ActDllBridge] OK: candidate plugin type: {pluginType.FullName} (candidates={candidates.Count})");
                    if (candidates.Count > 1)
                    {
                        foreach (var t in candidates.Take(8))
                            lines.Add($"[ActDllBridge]  - {t.FullName}");
                    }
                }
                else
                {
                    lines.Add("[ActDllBridge] WARN: no plugin type candidates found in FFXIV_ACT_Plugin.");
                }
            }

            if (pluginType != null)
            {
                TryReportInitSignature(pluginType, lines);

                if (tryInitPlugin)
                {
                    lines.Add("[ActDllBridge] TRY: InitPlugin (high risk)...");
                    TryInitPluginViaReflection(pluginType, lines);
                }
            }

            summary.Append("ACT/FFXIV_ACT_Plugin 加载探测完成。");
            var ok = actAsm != null && ffxivAsm != null;
            return new ActDllProbeResult
            {
                Success = ok,
                Summary = ok ? summary.ToString() : "探测完成，但未能完整加载 ACT/FFXIV_ACT_Plugin（详见日志）。",
                LogLines = lines,
            };
            }
            finally
            {
                if (!tryInitPlugin)
                {
                    try { alc.Unload(); } catch { /* ignored */ }
                }
                else
                {
                    // InitPlugin 可能创建 WinForms/线程等对象；在游戏进程中做可回收卸载风险极高，先避免 Unload。
                    lines.Add("[ActDllBridge] NOTE: tryInitPlugin=true; skip AssemblyLoadContext.Unload().");
                }
            }
        }
        catch (Exception e)
        {
            lines.Add($"[ActDllBridge] FATAL: {e.GetType().Name}: {e.Message}");
            return new ActDllProbeResult
            {
                Success = false,
                Summary = "探测过程中发生异常（详见日志）。",
                LogLines = lines,
            };
        }
    }

    private static void TryReportInitSignature(Type pluginType, List<string> lines)
    {
        try
        {
            var init = pluginType.GetMethod("InitPlugin", BindingFlags.Public | BindingFlags.Instance);
            if (init == null)
            {
                lines.Add("[ActDllBridge] WARN: InitPlugin not found.");
                return;
            }

            var ps = init.GetParameters();
            var sig = string.Join(", ", ps.Select(static p => $"{p.ParameterType.FullName} {p.Name}"));
            lines.Add($"[ActDllBridge] InitPlugin signature: ({sig})");
        }
        catch (Exception e)
        {
            lines.Add($"[ActDllBridge] WARN: read InitPlugin signature failed: {e.GetType().Name}: {e.Message}");
        }
    }

    private static void TryInitPluginViaReflection(Type pluginType, List<string> lines)
    {
        // WinForms 需要 STA；且必须显式 Dispose 控件，避免 Finalizer 线程抛异常导致整个游戏进程崩溃。
        var threadLines = new List<string>(32);
        var thread = new Thread(() =>
        {
            object? instance = null;
            object? tab = null;
            object? label = null;

            try
            {
                instance = Activator.CreateInstance(pluginType);
                threadLines.Add("[ActDllBridge] OK: Activator.CreateInstance succeeded.");

                var init = pluginType.GetMethod("InitPlugin", BindingFlags.Public | BindingFlags.Instance);
                if (init == null)
                {
                    threadLines.Add("[ActDllBridge] ERR: InitPlugin not found; cannot init.");
                    return;
                }

                var ps = init.GetParameters();
                if (ps.Length != 2)
                {
                    threadLines.Add($"[ActDllBridge] ERR: InitPlugin params != 2 (actual={ps.Length}); skip.");
                    return;
                }

                var tabPageType = Type.GetType("System.Windows.Forms.TabPage, System.Windows.Forms", throwOnError: false);
                var labelType = Type.GetType("System.Windows.Forms.Label, System.Windows.Forms", throwOnError: false);
                if (tabPageType == null || labelType == null)
                {
                    threadLines.Add("[ActDllBridge] ERR: System.Windows.Forms types not available in current runtime; cannot call InitPlugin.");
                    return;
                }

                tab = Activator.CreateInstance(tabPageType);
                label = Activator.CreateInstance(labelType);
                if (tab == null || label == null)
                {
                    threadLines.Add("[ActDllBridge] ERR: failed to create TabPage/Label instances; cannot call InitPlugin.");
                    return;
                }

                init.Invoke(instance, new[] { tab, label });
                threadLines.Add("[ActDllBridge] OK: InitPlugin invoked (no exception).");
            }
            catch (TargetInvocationException tie)
            {
                var ie = tie.InnerException;
                if (ie != null)
                    threadLines.Add($"[ActDllBridge] ERR: InitPlugin threw: {ie.GetType().Name}: {ie.Message}");
                else
                    threadLines.Add($"[ActDllBridge] ERR: InitPlugin TargetInvocationException: {tie.Message}");
            }
            catch (Exception e)
            {
                threadLines.Add($"[ActDllBridge] ERR: InitPlugin invoke failed: {e.GetType().Name}: {e.Message}");
            }
            finally
            {
                try
                {
                    var deinit = pluginType.GetMethod("DeInitPlugin", BindingFlags.Public | BindingFlags.Instance);
                    if (deinit != null && instance != null)
                    {
                        deinit.Invoke(instance, Array.Empty<object>());
                        threadLines.Add("[ActDllBridge] OK: DeInitPlugin invoked.");
                    }
                }
                catch
                {
                    // ignore cleanup failures in PoC
                }

                try
                {
                    if (tab is IDisposable d1) d1.Dispose();
                    if (label is IDisposable d2) d2.Dispose();
                }
                catch
                {
                    // ignore dispose failures in PoC
                }

                try
                {
                    if (instance is IDisposable d3) d3.Dispose();
                }
                catch
                {
                    // ignore dispose failures in PoC
                }
            }
        });

        thread.IsBackground = true;
        try
        {
            thread.SetApartmentState(ApartmentState.STA);
        }
        catch (Exception e)
        {
            threadLines.Add($"[ActDllBridge] WARN: SetApartmentState(STA) failed: {e.GetType().Name}: {e.Message}");
        }

        thread.Start();

        if (!thread.Join(TimeSpan.FromSeconds(10)))
        {
            threadLines.Add("[ActDllBridge] ERR: InitPlugin timeout (>10s); abort wait (thread is background).");
        }

        lines.AddRange(threadLines);

        // 尽量在 Unload 前完成清理，避免残留对象延迟 Finalize 时引发崩溃。
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        catch
        {
            // ignore
        }
    }

    private static Type? FindTypeSafe(Assembly asm, string fullName, List<string> lines)
    {
        try
        {
            return asm.GetType(fullName, throwOnError: false, ignoreCase: false);
        }
        catch (Exception e)
        {
            lines.Add($"[ActDllBridge] WARN: GetType({fullName}) failed: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    private static IReadOnlyList<Type?> GetTypesSafe(Assembly asm, List<string> lines)
    {
        try
        {
            return asm.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            var loaded = e.Types?.Where(static t => t != null).ToArray() ?? Array.Empty<Type?>();
            lines.Add($"[ActDllBridge] WARN: GetTypes ReflectionTypeLoadException: loaded={loaded.Length} errors={e.LoaderExceptions?.Length ?? 0}");
            if (e.LoaderExceptions != null)
            {
                foreach (var ex in e.LoaderExceptions.Take(8))
                {
                    if (ex == null) continue;
                    lines.Add($"[ActDllBridge]  - LoaderException: {ex.GetType().Name}: {ex.Message}");
                }
            }

            return e.Types ?? Array.Empty<Type?>();
        }
        catch (Exception e)
        {
            lines.Add($"[ActDllBridge] WARN: GetTypes failed: {e.GetType().Name}: {e.Message}");
            return Array.Empty<Type?>();
        }
    }
}
