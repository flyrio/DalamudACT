using System;
using System.Numerics;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace DalamudACT
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        private const int CurrentVersion = 22;

        public Vector2 CardsWindowPos = Vector2.Zero;
        public bool HasCardsWindowPos = false;
        public Vector2 SummaryWindowPos = Vector2.Zero;
        public bool HasSummaryWindowPos = false;
        public Vector2 LauncherWindowPos = Vector2.Zero;
        public bool HasLauncherWindowPos = false;
        public bool HideName;
        public bool ClickThrough = false;
        public float CardsBackgroundAlpha = 0.28f;
        public float SummaryBackgroundAlpha = 0.75f;
        public float SummaryScale = 1f;
        public bool SummaryUseCustomBackgroundColor = false;
        public Vector4 SummaryBackgroundColor = new(0.12f, 0.12f, 0.12f, 1f);

        public bool ShowRates = true;
        public bool HighlightSelf = true;

        public bool CardsEnabled = false;
        public bool SummaryEnabled = true;
        public bool LauncherEnabled = true;
        public float LauncherButtonSize = 32f;
        public bool LauncherUseImage = false;
        public string LauncherButtonImagePath = string.Empty;
        public bool CardsPlacementMode = false;
        public int CardsPerLine = 1;
        public float CardsScale = 1f;
        public int DisplayLayout = 0; // 0=独立名片列 1=独立名片行

        public float CardColumnWidth = 240f;
        public float CardColumnHeight = 40f;
        public float CardColumnSpacing = 0f;
        public float CardRowWidth = 260f;
        public float CardRowHeight = 64f;
        public float CardRowSpacing = 6f;
        public int SortMode = 0; // 0=秒伤 1=总伤害 2=姓名
        public int TopN = 0; // 0=全部

        // 0=ENCDPS(按战斗时长) 1=DPS(按个人活跃时长)
        public int DpsTimeMode = 0;

        // 对齐 ACT：遭遇结束判定超时（毫秒）。过短会在 boss 转阶段/脱战瞬间误分段。
        public int EncounterTimeoutMs = 30000;

        // DoT 采集与诊断
        public bool EnableEnhancedDotCapture = false;
        public bool EnableDotDiagnostics = false;

        // 备选：ACTLike 归因（DoT/召唤物）
        public bool EnableActLikeAttribution = false;

        // 对齐/调试：战斗结束自动导出 battle-stats.jsonl（用于与 ACT 对齐）
        public bool AutoExportBattleStatsOnEnd = true;

        // 对齐/调试：战斗结束自动导出 dot-debug.log（DoTStats + DotDump），用于离线对照 DoT 归因差异
        public bool AutoExportDotDumpOnEnd = false;
        public int AutoExportDotDumpMax = 200;

        // 对齐：从 ACT MCP Pipe 拉取遭遇快照（用于对齐总伤害/ENCDPS）
        public bool EnableActMcpSync = true;
        public bool PreferActMcpTotals = true;
        public string ActMcpPipeName = "act-diemoe-mcp";

        // 实验/高风险：在游戏进程内尝试加载 ACT / FFXIV_ACT_Plugin DLL（仅用于 PoC 探测）
        public bool EnableActDllBridgeExperimental = false;
        public string ActDieMoeRoot = @"C:\Program Files (x86)\宝宝轮椅\ACT.DieMoe";

        // the below exist just to make saving less cumbersome

        [NonSerialized] private IDalamudPluginInterface? pluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;

            // Temporary toggle; always start off to avoid accidentally showing cards out of combat.
            CardsPlacementMode = false;
            var changed = false;

            if (CardsPerLine <= 0)
            {
                CardsPerLine = 1;
                changed = true;
            }

            if (CardsScale <= 0)
            {
                CardsScale = 1f;
                changed = true;
            }

            if (SummaryScale <= 0)
            {
                SummaryScale = 1f;
                changed = true;
            }

            const float minCardWidth = 160f;
            if (CardColumnWidth < minCardWidth)
            {
                CardColumnWidth = minCardWidth;
                changed = true;
            }

            if (CardRowWidth < minCardWidth)
            {
                CardRowWidth = minCardWidth;
                changed = true;
            }

            CardsBackgroundAlpha = Math.Clamp(CardsBackgroundAlpha, 0f, 1f);
            SummaryBackgroundAlpha = Math.Clamp(SummaryBackgroundAlpha, 0f, 1f);
            LauncherButtonSize = Math.Clamp(LauncherButtonSize, 16f, 200f);
            LauncherButtonImagePath ??= string.Empty;
            ActMcpPipeName ??= "act-diemoe-mcp";
            ActDieMoeRoot ??= @"C:\Program Files (x86)\宝宝轮椅\ACT.DieMoe";
            SummaryScale = Math.Clamp(SummaryScale, 0.5f, 2.0f);
            SummaryBackgroundColor = new Vector4(
                Math.Clamp(SummaryBackgroundColor.X, 0f, 1f),
                Math.Clamp(SummaryBackgroundColor.Y, 0f, 1f),
                Math.Clamp(SummaryBackgroundColor.Z, 0f, 1f),
                1f);

            if (Version < CurrentVersion)
            {
                if (Version < 8)
                {
                    SummaryEnabled = true;
                }

                if (Version < 9)
                {
                    CardsBackgroundAlpha = 0.28f;
                    SummaryBackgroundAlpha = 0.75f;
                }

                if (Version < 10)
                {
                    LauncherEnabled = true;
                }

                if (Version < 11)
                {
                    LauncherButtonSize = 32f;
                }

                if (Version < 12)
                {
                    LauncherUseImage = false;
                    LauncherButtonImagePath = string.Empty;
                }

                if (Version < 13)
                {
                    SummaryScale = 1f;
                    SummaryUseCustomBackgroundColor = false;
                    SummaryBackgroundColor = new Vector4(0.12f, 0.12f, 0.12f, 1f);
                }

                if (Version < 14)
                {
                    DpsTimeMode = 0;
                }

                if (Version < 15)
                {
                    EnableEnhancedDotCapture = false;
                    EnableDotDiagnostics = false;
                }

                if (Version < 16)
                {
                    AutoExportBattleStatsOnEnd = true;
                }

                if (Version < 17)
                {
                    EnableActMcpSync = false;
                    PreferActMcpTotals = true;
                    ActMcpPipeName = "act-diemoe-mcp";
                }

                if (Version < 18)
                {
                    EncounterTimeoutMs = 30000;
                }

                if (Version < 19)
                {
                    EnableActDllBridgeExperimental = false;
                    ActDieMoeRoot = @"C:\Program Files (x86)\宝宝轮椅\ACT.DieMoe";
                }

                if (Version < 20)
                {
                    // 默认启用：用于修正 DoT 归因/去重/明细，对齐 ACT 的常见口径。
                    EnableEnhancedDotCapture = true;
                }

                if (Version < 21)
                {
                    // 用户目标：尽可能与 ACT 一致。默认启用 ACT MCP 同步（存在时自动对齐；不存在则自动回退本地统计）。
                    EnableActMcpSync = true;
                    PreferActMcpTotals = true;
                }

                if (Version < 22)
                {
                    // 调试/对齐：为了便于离线核对 DoT，战斗结束自动导出 DotDump/DoTStats（可在设置里关闭）。
                    AutoExportDotDumpOnEnd = true;
                    AutoExportDotDumpMax = 200;
                }

                // v1: DisplayLayout 0=纵向列表 1=独立名片列
                if (Version <= 1)
                {
                    CardsEnabled = DisplayLayout == 1;
                    DisplayLayout = 0;
                    changed = true;
                }

                Version = CurrentVersion;
                Save();
                changed = false;
            }

            if (DpsTimeMode is < 0 or > 1)
            {
                DpsTimeMode = 0;
                changed = true;
            }

            EncounterTimeoutMs = Math.Clamp(EncounterTimeoutMs, 1000, 120000);
            AutoExportDotDumpMax = Math.Clamp(AutoExportDotDumpMax, 20, 2000);

            if (changed) Save();
        }

        public void Save()
        {
            pluginInterface!.SavePluginConfig(this);
        }

        public int Version { get; set; }
    }
}
