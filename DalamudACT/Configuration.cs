using System;
using System.Numerics;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace DalamudACT
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        private const int CurrentVersion = 9;

        public Vector2 CardsWindowPos = Vector2.Zero;
        public bool HasCardsWindowPos = false;
        public Vector2 SummaryWindowPos = Vector2.Zero;
        public bool HasSummaryWindowPos = false;
        public bool HideName;
        public bool ClickThrough = false;
        public float CardsBackgroundAlpha = 0.28f;
        public float SummaryBackgroundAlpha = 0.75f;

        public bool ShowRates = true;
        public bool HighlightSelf = true;

        public bool CardsEnabled = false;
        public bool SummaryEnabled = true;
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

            if (changed) Save();
        }

        public void Save()
        {
            pluginInterface!.SavePluginConfig(this);
        }

        public int Version { get; set; }
    }
}
