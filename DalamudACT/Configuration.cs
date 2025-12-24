using System;
using System.Numerics;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace DalamudACT
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        private const int CurrentVersion = 6;

        public Vector2 CardsWindowPos = Vector2.Zero;
        public bool HasCardsWindowPos = false;
        public bool HideName;
        public bool ClickThrough = false;

        public bool ShowRates = true;
        public bool HighlightSelf = true;

        public bool CardsEnabled = false;
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
            if (CardsPerLine <= 0) CardsPerLine = 1;
            if (CardsScale <= 0) CardsScale = 1f;
            if (Version < CurrentVersion)
            {
                // v1: DisplayLayout 0=纵向列表 1=独立名片列
                if (Version <= 1)
                {
                    CardsEnabled = DisplayLayout == 1;
                    DisplayLayout = 0;
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
