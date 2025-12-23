using System;
using System.Numerics;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace DalamudACT
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        private const int CurrentVersion = 4;

        public bool Lock;
        public bool NoResize;
        public Vector2 WindowSize = Vector2.One;
        public Vector2 MainWindowPos = Vector2.Zero;
        public bool HasMainWindowPos = false;
        public Vector2 MiniWindowPos = Vector2.Zero;
        public bool HasMiniWindowPos = false;
        public Vector2 CardsWindowPos = Vector2.Zero;
        public bool HasCardsWindowPos = false;
        public bool HideName;
        public bool Mini;
        public int BGColor = 30;
        public bool delta = false;
        public bool autohide = false;
        public bool ClickThrough = false;

        public bool ShowRates = true;
        public bool HighlightSelf = true;
        public bool AutoCompact = true;
        public bool CompactMode = false;

        public bool ShowVerticalList = true;
        public bool CardsEnabled = false;
        public bool CardsPlacementMode = false;
        public int CardsPerLine = 1;
        public float CardsScale = 1f;

        // Backward-compat: older configs stored separate values per layout.
        [Obsolete("Use CardsPerLine")]
        public int CardsColumns = 1;
        [Obsolete("Use CardsPerLine")]
        public int CardsRows = 1;
        public int DisplayLayout = 0; // 0=独立名片列 1=独立名片行

        public float CardWidth = 260f;
        public float CardHeight = 64f;
        public float CardSpacing = 6f;

        public float CardColumnWidth = 240f;
        public float CardColumnHeight = 40f;
        public float CardColumnSpacing = 0f;
        public float CardRowWidth = 260f;
        public float CardRowHeight = 64f;
        public float CardRowSpacing = 6f;
        public int SortMode = 0; // 0=秒伤 1=总伤害 2=姓名
        public int TopN = 0; // 0=全部
        public int TableLayoutSeed = 0;

        public bool SaveData = false;
        public int SaveTime = 30;
        public int CalcTime = 30;

        // the below exist just to make saving less cumbersome

        [NonSerialized] private IDalamudPluginInterface? pluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;

            // Temporary toggle; always start off to avoid accidentally showing cards out of combat.
            CardsPlacementMode = false;
            if (CardsPerLine <= 0) CardsPerLine = 1;
            if (CardsScale <= 0) CardsScale = 1f;
            if (Version < CurrentVersion)
            {
                // v1: DisplayLayout 0=纵向列表 1=独立名片列
                if (Version <= 1)
                {
                    CardsEnabled = DisplayLayout == 1;
                    DisplayLayout = 0;
                    ShowVerticalList = !CardsEnabled;
                }

                if (Version <= 2)
                {
                    var usingDefaultV2Size = Math.Abs(CardWidth - 260f) < 0.01f &&
                                            Math.Abs(CardHeight - 64f) < 0.01f &&
                                            Math.Abs(CardSpacing - 6f) < 0.01f;

                    if (!usingDefaultV2Size)
                    {
                        CardColumnWidth = CardWidth;
                        CardColumnHeight = CardHeight;
                        CardColumnSpacing = CardSpacing;
                        CardRowWidth = CardWidth;
                        CardRowHeight = CardHeight;
                        CardRowSpacing = CardSpacing;
                    }
                }
                if (Version <= 3)
                {
#pragma warning disable CS0618 // legacy config migration
                    var legacy = DisplayLayout == 0 ? CardsColumns : CardsRows;
                    CardsPerLine = legacy <= 0 ? 1 : legacy;
#pragma warning restore CS0618
                }

                Version = CurrentVersion;
                Save();
            }
        }

        public void Save()
        {
            pluginInterface!.SavePluginConfig(this);
        }

        public int Version { get; set; }
    }
}
