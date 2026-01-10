// ACT 插件入口与事件 Hook，负责战斗事件采集与统计更新。
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Hooking;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DalamudACT.Struct;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Action = Lumina.Excel.Sheets.Action;
using EventKind = DalamudACT.Struct.ACTBattle.EventKind;

namespace DalamudACT
{
    public class ACT : IDalamudPlugin
    {
        public string Name => "伤害统计";

        public object SyncRoot { get; } = new();

        public Configuration Configuration;
        private PluginUI PluginUi;

        public Dictionary<uint, IDalamudTextureWrap?> Icon = new();
        public List<ACTBattle> Battles = new(5);
        private ExcelSheet<TerritoryType> terrySheet;
        internal IFontHandle? CardsFontHandle;
        internal IFontHandle? SummaryFontHandle;
        private bool wasInCombat;

        private delegate void ReceiveAbilityDelegate(int sourceId, nint sourceCharacter, nint pos,
            nint effectHeader, nint effectArray, nint effectTrail);

        private Hook<ReceiveAbilityDelegate> ReceiveAbilityHook;

        // 更新：移除 arg8，与 JobBars 保持一致
        private delegate void ActorControlSelfDelegate(
            uint entityId,
            uint id,
            uint arg0,
            uint arg1,
            uint arg2,
            uint arg3,
            uint arg4,
            uint arg5,
            uint arg6,
            uint arg7,
            ulong targetId,
            byte a10);

        private Hook<ActorControlSelfDelegate> ActorControlSelfHook;

        private delegate void CastDelegate(uint sourceId, nint sourceCharacter);

        private Hook<CastDelegate> CastHook;


        #region OPcode & Hook functions

        private unsafe void Ability(nint headPtr, nint effectPtr, nint targetPtr, uint sourceId, int length)
        {
            DalamudApi.Log.Verbose($"-----------------------Ability{length}:{sourceId:X}------------------------------");

            var originalSourceId = sourceId;
            if (sourceId > 0x40000000 && sourceId != 0xE0000000)
                sourceId = ACTBattle.ResolveOwner(sourceId);

            if (sourceId == 0xE0000000 && originalSourceId != 0xE0000000) return;
            if (sourceId is > 0x40000000 or 0x0) return;

            var header = Marshal.PtrToStructure<Header>(headPtr);
            var effect = (EffectEntry*)effectPtr;
            var target = (ulong*)targetPtr;

            lock (SyncRoot)
            {
                for (var i = 0; i < length; i++)
                {
                    DalamudApi.Log.Verbose(
                        $"{*target:X} effect:{effect->type}:{effect->param0}:{effect->param1}:{effect->param2}:{effect->param3}:{effect->param4}:{effect->param5}");
                    if (*target == 0x0) break;

                    for (var j = 0; j < 8; j++)
                    {
                        if (effect->type is 3 or 5 or 6) // Damage / Blocked / Parried
                        {
                            var damage = (uint)effect->param0;
                            if ((effect->param5 & 0x40) != 0)
                                damage += (uint)(effect->param4 * 65536);

                            var targetId = ((effect->param5 & 0x80) != 0) ? sourceId : (uint)*target;
                            var critDh = (byte)(effect->param1 & 0x60);

                            DalamudApi.Log.Verbose($"EffectEntry:{effect->type},{sourceId:X}:{targetId:X}:{header.actionId},{damage}");
                            Battles[^1].AddEvent(EventKind.Damage, sourceId, targetId, header.actionId, damage, critDh);
                        }

                        effect++;
                    }

                    target++;
                }
            }

            DalamudApi.Log.Verbose("------------------------END------------------------------");
        }

        private void StartCast(uint source, nint ptr)
        {
            var data = Marshal.PtrToStructure<ActorCast>(ptr);
            CastHook.Original(source, ptr);
            if (source > 0x40000000) return;
            DalamudApi.Log.Verbose(
                $"Cast:{source:X}:{data.skillType}:{data.action_id}:{data.cast_time}:{data.flag}:{data.unknown_2:X}:{data.unknown_3}");
            if (data.skillType == 1 && Potency.SkillPot.ContainsKey(data.action_id))
                lock (SyncRoot)
                    if (Battles[^1].DataDic.TryGetValue(source, out _))
                        Battles[^1].AddSS(source, data.cast_time, data.action_id);
        }

        // 更新：移除 arg8 参数
        private void ReceiveActorControlSelf(
            uint entityId,
            uint type,
            uint arg0,
            uint arg1,
            uint arg2,
            uint arg3,
            uint arg4,
            uint arg5,
            uint arg6,
            uint arg7,
            ulong targetId,
            byte a10)
        {
            // 更新：移除 arg8
            ActorControlSelfHook.Original(entityId, type, arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7, targetId, a10);

            if (type == (uint)ActorControlCategory.Death && entityId < 0x40000000)
            {
                lock (SyncRoot)
                    Battles[^1].AddEvent(EventKind.Death, entityId, arg0, 0, 0);
                DalamudApi.Log.Verbose($"{entityId:X} killed by {arg0:X}");
                return;
            }

            var dotTarget = entityId;
            if (dotTarget < 0x40000000 && targetId > 0 && targetId <= uint.MaxValue)
                dotTarget = (uint)targetId;
            if (dotTarget < 0x40000000) return;

            var sourceId = arg2;
            if (sourceId > 0x40000000 && sourceId != 0xE0000000)
                sourceId = ACTBattle.ResolveOwner(sourceId);

            if (type is (uint)ActorControlCategory.DoT)
            {
                lock (SyncRoot)
                {
                    if ((sourceId == 0 || sourceId > 0x40000000) && arg0 != 0 &&
                        Battles[^1].TryResolveDotSource(dotTarget, arg0, out var resolvedSource))
                        sourceId = resolvedSource;

                    DalamudApi.Log.Verbose($"Dot:{arg0} from {sourceId:X} ticked {arg1} damage on {dotTarget:X}");
                    if (sourceId == 0 || sourceId > 0x40000000)
                    {
                        Battles[^1].AddEvent(EventKind.Damage, 0xE0000000, dotTarget, 0, arg1, countHit: false);
                        return;
                    }

                    Battles[^1].AddDotTick(sourceId, arg1);
                    if (arg0 != 0 && Potency.BuffToAction.TryGetValue(arg0, out arg0))
                    {
                        Battles[^1].AddEvent(EventKind.Damage, sourceId, dotTarget, arg0, arg1, countHit: false);
                    }
                    else
                    {
                        // Prefer attributing the tick to the reported source (and avoid id=0 double-counting in AddEvent).
                        Battles[^1].AddDotDamage(sourceId, arg1);
                    }
                }
            }
        }

        private unsafe void ReceiveAbilityEffect(int sourceId, IntPtr sourceCharacter, IntPtr pos, IntPtr effectHeader,
            IntPtr effectArray, IntPtr effectTrail)
        {
            var targetCount = *(byte*)(effectHeader + 0x21);
            switch (targetCount)
            {
                case <= 1:
                    Ability(effectHeader, effectArray, effectTrail, (uint)sourceId, 1);
                    break;
                case <= 8 and > 1:
                    Ability(effectHeader, effectArray, effectTrail, (uint)sourceId, 8);
                    break;
                case > 8 and <= 16:
                    Ability(effectHeader, effectArray, effectTrail, (uint)sourceId, 16);
                    break;
                case > 16 and <= 24:
                    Ability(effectHeader, effectArray, effectTrail, (uint)sourceId, 24);
                    break;
                case > 24 and <= 32:
                    Ability(effectHeader, effectArray, effectTrail, (uint)sourceId, 32);
                    break;
            }

            ReceiveAbilityHook.Original(sourceId, sourceCharacter, pos, effectHeader, effectArray, effectTrail);
        }

        #endregion

        private void CheckTime()
        {
            var inCombat = DalamudApi.Conditions.Any(ConditionFlag.InCombat);
            var now = DateTimeOffset.Now.ToUnixTimeSeconds();
            lock (SyncRoot)
            {
                // Combat ended: freeze the last battle and keep it for browsing.
                if (wasInCombat && !inCombat)
                {
                    Battles[^1].EndTime = now;
                    Battles[^1].ActiveDots.Clear();
                    ACTBattle.ClearOwnerCache();
                    if (Battles.Count == 5)
                    {
                        Battles.RemoveAt(0);
                    }

                    Battles.Add(new ACTBattle(0, 0));
                }

                if (DalamudApi.ObjectTable.LocalPlayer != null && inCombat)
                {
                    if (Battles[^1].StartTime is 0) Battles[^1].StartTime = now;
                    Battles[^1].EndTime = now;
                    Battles[^1].Zone = GetPlaceName();
                }

                wasInCombat = inCombat;
            }
        }

        private string GetPlaceName()
        {
            var result = "Unknown";
            var excel = terrySheet.GetRow(DalamudApi.ClientState.TerritoryType);
            if (!excel.ContentFinderCondition.Value.Name.IsEmpty)
            {
                return excel.ContentFinderCondition.Value.Name.ExtractText();
            }
            else
            {
                if (!excel.PlaceName.Value.Name.IsEmpty)
                {
                    return excel.PlaceName.Value.Name.ExtractText();
                }
                else if (!excel.PlaceNameRegion.Value.Name.IsEmpty)
                {
                    return excel.PlaceNameRegion.Value.Name.ExtractText();
                }
                else if (!excel.PlaceNameZone.Value.Name.IsEmpty)
                {
                    return excel.PlaceNameZone.Value.Name.ExtractText();
                }
            }
            return result;
        }

        private void Update(IFramework framework)
        {
            CheckTime();
        }


        public ACT(IDalamudPluginInterface pluginInterface)
        {
            DalamudApi.Initialize(pluginInterface);
            terrySheet = DalamudApi.GameData.GetExcelSheet<TerritoryType>()!;
            ACTBattle.ActionSheet = DalamudApi.GameData.GetExcelSheet<Action>()!;

            for (uint i = 62100; i <= 62100 + 42; i++)
                Icon.Add(i - 62100, DalamudApi.TextureProvider.GetFromGameIcon(new GameIconLookup(i)).RentAsync().Result);

            Icon.Add(99, DalamudApi.TextureProvider.GetFromGameIcon(new GameIconLookup(103)).RentAsync().Result); //LB

            Battles.Add(new ACTBattle(0, 0));

            Configuration = DalamudApi.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Configuration.Initialize(DalamudApi.PluginInterface);

            try
            {
                ((Dalamud.Interface.UiBuilder)DalamudApi.PluginInterface.UiBuilder).RunWhenUiPrepared<int>(() =>
                {
                    RefreshCardsFont();
                    RefreshSummaryFont();
                    return 0;
                });
            }
            catch
            {
                // If UiBuilder isn't prepared yet, fall back to best-effort at runtime via settings changes.
            }

            #region Hook
            {
                // ReceiveAbility 签名保持不变
                ReceiveAbilityHook = DalamudApi.Interop.HookFromAddress<ReceiveAbilityDelegate>(
                    DalamudApi.SigScanner.ScanText("40 55 56 57 41 54 41 55 41 56 41 57 48 8D AC 24 ?? ?? ?? ??"),
                    ReceiveAbilityEffect);
                ReceiveAbilityHook.Enable();

                // ActorControlSelf - 签名可能需要更新，请根据实际版本调整
                ActorControlSelfHook = DalamudApi.Interop.HookFromAddress<ActorControlSelfDelegate>(
                    DalamudApi.SigScanner.ScanText("E8 ?? ?? ?? ?? 0F B7 0B 83 E9 64"),
                    ReceiveActorControlSelf);
                ActorControlSelfHook.Enable();

                // Cast 签名保持不变
                CastHook = DalamudApi.Interop.HookFromAddress<CastDelegate>(
                    DalamudApi.SigScanner.ScanText("40 53 57 48 81 EC ?? ?? ?? ?? 48 8B FA 8B D1"),
                    StartCast);
                CastHook.Enable();
            }
            #endregion

            DalamudApi.Framework.Update += Update;

            PluginUi = new PluginUI(this);

            DalamudApi.Commands.AddHandler("/act", new CommandInfo(OnCommand)
            {
                HelpMessage = "/act 开关独立名片\n/act config 打开设置窗口\n/act prev 查看上一场\n/act next 查看下一场\n/act clear 清空战斗记录"
            });

            DalamudApi.PluginInterface.UiBuilder.Draw += DrawUI;
            DalamudApi.PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
        }

        internal void RefreshCardsFont()
        {
            var uiBuilder = (Dalamud.Interface.UiBuilder)DalamudApi.PluginInterface.UiBuilder;
            var atlas = uiBuilder.FontAtlas;

            CardsFontHandle?.Dispose();
            CardsFontHandle = null;

            var scale = Math.Clamp(Configuration.CardsScale, 0.5f, 2.0f);
            if (Math.Abs(scale - 1f) >= 0.01f)
            {
                var style = new GameFontStyle(GameFontFamily.Axis, Dalamud.Interface.UiBuilder.DefaultFontSizePt * scale);
                CardsFontHandle = atlas.NewGameFontHandle(style);
            }

            // In newer Dalamud, BuildFontsOnNextFrame is not allowed when AutoRebuildMode is Async.
            if (string.Equals(atlas.AutoRebuildMode.ToString(), "Async", StringComparison.OrdinalIgnoreCase))
                _ = atlas.BuildFontsAsync();
            else
                atlas.BuildFontsOnNextFrame();
        }

        internal void RefreshSummaryFont()
        {
            var uiBuilder = (Dalamud.Interface.UiBuilder)DalamudApi.PluginInterface.UiBuilder;
            var atlas = uiBuilder.FontAtlas;

            SummaryFontHandle?.Dispose();
            SummaryFontHandle = null;

            var scale = Math.Clamp(Configuration.SummaryScale, 0.5f, 2.0f);
            if (Math.Abs(scale - 1f) >= 0.01f)
            {
                var style = new GameFontStyle(GameFontFamily.Axis, Dalamud.Interface.UiBuilder.DefaultFontSizePt * scale);
                SummaryFontHandle = atlas.NewGameFontHandle(style);
            }

            // In newer Dalamud, BuildFontsOnNextFrame is not allowed when AutoRebuildMode is Async.
            if (string.Equals(atlas.AutoRebuildMode.ToString(), "Async", StringComparison.OrdinalIgnoreCase))
                _ = atlas.BuildFontsAsync();
            else
                atlas.BuildFontsOnNextFrame();
        }

        public void Disable()
        {
            ActorControlSelfHook.Disable();
            ReceiveAbilityHook.Disable();
            CastHook.Disable();
        }

        public void Dispose()
        {
            DalamudApi.Framework.Update -= Update;
            CardsFontHandle?.Dispose();
            SummaryFontHandle?.Dispose();
            PluginUi?.Dispose();
            foreach (var (id, texture) in Icon) texture?.Dispose();
            Disable();
            ActorControlSelfHook.Dispose();
            ReceiveAbilityHook.Dispose();
            CastHook.Dispose();
        }

        private void OnCommand(string command, string args)
        {
            switch (args)
            {
                case null or "":
                    Configuration.CardsEnabled = !Configuration.CardsEnabled;
                    Configuration.Save();
                    PluginUi.cardsWindow.IsOpen = Configuration.CardsEnabled;
                    PluginUi.summaryWindow.IsOpen = Configuration.CardsEnabled && Configuration.SummaryEnabled;
                    break;
                case "config":
                    PluginUi.configWindow.IsOpen = !PluginUi.configWindow.IsOpen;
                    break;
                case "prev":
                    PluginUi.cardsWindow.NudgeHistory(1);
                    break;
                case "next":
                    PluginUi.cardsWindow.NudgeHistory(-1);
                    break;
                case "clear":
                    PluginUi.cardsWindow.ClearBattleHistory();
                    break;
            }
        }

        private void DrawUI()
        {
            PluginUi.WindowSystem.Draw();
        }

        public void DrawConfigUI()
        {
            PluginUi.configWindow.IsOpen = !PluginUi.configWindow.IsOpen;
        }
    }
}
