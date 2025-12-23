using System;
using System.Numerics;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace DalamudACT
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public bool Lock;
        public bool NoResize;
        public Vector2 WindowSize = Vector2.One;
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
        }

        public void Save()
        {
            pluginInterface!.SavePluginConfig(this);
        }

        public int Version { get; set; }
    }
}
