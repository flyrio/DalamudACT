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
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
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
        private readonly DotEventCapture dotCapture;
        private bool wasInCombat;

        private unsafe delegate void ReceiveAbilityDelegate(
            uint sourceId,
            nint sourceCharacter,
            nint pos,
            ActionEffectHandler.Header* effectHeader,
            ActionEffectHandler.TargetEffects* effectArray,
            GameObjectId* effectTrail);

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

        private unsafe void Ability(ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targets, uint sourceId)
        {
            var length = header->NumTargets;
            if (length == 0) return;

            DalamudApi.Log.Verbose($"-----------------------Ability{length}:{sourceId:X}------------------------------");

            var eventTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var originalSourceId = sourceId;
            if (sourceId > 0x40000000 && sourceId != 0xE0000000)
            {
                sourceId = ACTBattle.ResolveOwner(sourceId);
                if (sourceId == 0xE0000000 && originalSourceId != 0xE0000000 && Configuration.EnableActLikeAttribution)
                {
                    ACTBattle.WarmOwnerCacheFromObjectTable(eventTimeMs);
                    sourceId = ACTBattle.ResolveOwner(originalSourceId);
                }
            }

            if (sourceId == 0xE0000000 && originalSourceId != 0xE0000000) return;
            if (sourceId is > 0x40000000 or 0x0) return;

            var actionId = header->SpellId != 0 ? (uint)header->SpellId : header->ActionId;

            lock (SyncRoot)
            {
                for (var i = 0; i < length; i++)
                {
                    var targetId = (uint)(targets[i] & uint.MaxValue);
                    if (targetId == 0) continue;

                    for (var j = 0; j < 8; j++)
                    {
                        ref var effect = ref effects[i].Effects[j];
                        if (effect.Type is 3 or 5 or 6) // Damage / Blocked / Parried
                        {
                            var damage = (uint)effect.Value | ((uint)effect.Param3 << 16);

                            var critDh = (byte)(effect.Param0 & 0x60);

                            Battles[^1].AddEvent(EventKind.Damage, sourceId, targetId, actionId, damage, critDh, eventTimeMs: eventTimeMs);
                        }
                    }
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
                var eventTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                lock (SyncRoot)
                    Battles[^1].AddEvent(EventKind.Death, entityId, arg0, 0, 0, eventTimeMs: eventTimeMs);
                DalamudApi.Log.Verbose($"{entityId:X} killed by {arg0:X}");
                return;
            }

            var dotTarget = entityId;

            // DoT 事件的目标可能在 entityId 或 targetId 中；优先使用有效的 BattleNpc EntityId。
            uint resolvedTarget = 0;
            if (targetId > 0 && targetId <= uint.MaxValue)
            {
                var candidate = (uint)targetId;
                if (candidate is > 0x40000000 and not 0xE0000000)
                    resolvedTarget = candidate;
            }

            if (resolvedTarget == 0 && dotTarget is > 0x40000000 and not 0xE0000000)
                resolvedTarget = dotTarget;

            if (resolvedTarget == 0) return;
            dotTarget = resolvedTarget;

            var sourceId = arg2;

            if (type is (uint)ActorControlCategory.DoT)
            {
                var eventTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                dotCapture.Enqueue(new DotTickEvent(
                    eventTimeMs,
                    sourceId,
                    dotTarget,
                    buffId: arg0,
                    damage: arg1,
                    DotTickChannel.LegacyActorControlSelf));
            }
        }

        private unsafe void ReceiveAbilityEffect(
            uint sourceId,
            nint sourceCharacter,
            nint pos,
            ActionEffectHandler.Header* effectHeader,
            ActionEffectHandler.TargetEffects* effectArray,
            GameObjectId* effectTrail)
        {
            ReceiveAbilityHook.Original(sourceId, sourceCharacter, pos, effectHeader, effectArray, effectTrail);

            if (effectHeader->NumTargets > 0)
                Ability(effectHeader, effectArray, effectTrail, sourceId);
        }

        #endregion

        private void CheckTime()
        {
            var inCombat = DalamudApi.Conditions.Any(ConditionFlag.InCombat);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lock (SyncRoot)
            {
                // Combat ended: freeze the last battle and keep it for browsing.
                if (wasInCombat && !inCombat)
                {
                    var battle = Battles[^1];
                    if (battle.LastEventTime > 0)
                        battle.EndTime = battle.LastEventTime;
                    else if (battle.StartTime > 0)
                        battle.EndTime = battle.StartTime;
                    else
                        battle.EndTime = 0;

                    battle.ActiveDots.Clear();
                    ACTBattle.ClearOwnerCache();
                    if (Battles.Count == 5)
                    {
                        Battles.RemoveAt(0);
                    }

                    Battles.Add(new ACTBattle(0, 0));
                    dotCapture.ResetTransientCaches();
                }

                if (DalamudApi.ObjectTable.LocalPlayer != null && inCombat)
                {
                    var battle = Battles[^1];
                    if (battle.StartTime != 0)
                        battle.EndTime = now;
                    battle.Zone = GetPlaceName();
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
            if (Configuration.EnableActLikeAttribution)
                ACTBattle.WarmOwnerCacheFromObjectTable();
            lock (SyncRoot)
                dotCapture.FlushInto(Battles[^1]);
            CheckTime();
        }


        public ACT(IDalamudPluginInterface pluginInterface)
        {
            DalamudApi.Initialize(pluginInterface);
            terrySheet = DalamudApi.GameData.GetExcelSheet<TerritoryType>()!;
            ACTBattle.ActionSheet = DalamudApi.GameData.GetExcelSheet<Action>()!;
            ACTBattle.StatusSheet = DalamudApi.GameData.GetExcelSheet<Status>()!;

            for (uint i = 62100; i <= 62100 + 42; i++)
                Icon.Add(i - 62100, DalamudApi.TextureProvider.GetFromGameIcon(new GameIconLookup(i)).RentAsync().Result);

            Icon.Add(99, DalamudApi.TextureProvider.GetFromGameIcon(new GameIconLookup(103)).RentAsync().Result); //LB

            Battles.Add(new ACTBattle(0, 0));

            Configuration = DalamudApi.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Configuration.Initialize(DalamudApi.PluginInterface);
            dotCapture = new DotEventCapture(Configuration);

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
            unsafe
            {
                // ReceiveAbility：使用 FFXIVClientStructs 提供的签名，减少版本漂移
                ReceiveAbilityHook = DalamudApi.Interop.HookFromSignature<ReceiveAbilityDelegate>(
                    ActionEffectHandler.Addresses.Receive.String,
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
                 HelpMessage = "/act 开关独立名片\n/act config 打开设置窗口\n/act prev 查看上一场\n/act next 查看下一场\n/act clear 清空战斗记录\n/act dotstats [log|file] 输出 DoT 采集统计（调试）\n/act dotdump [log|file] [all] [N] 输出最近 DoT tick 事件（调试）"
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
             var trimmed = args?.Trim();
             if (string.IsNullOrEmpty(trimmed))
             {
                 Configuration.CardsEnabled = !Configuration.CardsEnabled;
                 Configuration.Save();
                 PluginUi.cardsWindow.IsOpen = Configuration.CardsEnabled;
                 PluginUi.summaryWindow.IsOpen = Configuration.CardsEnabled && Configuration.SummaryEnabled;
                 return;
             }

             var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
             var subCommand = parts[0];

             switch (subCommand)
             {
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
                  case "dotstats":
                      PrintDotStats(ParseDotDebugTarget(parts, defaultTarget: DotDebugOutputTarget.Chat));
                      break;
                  case "dotdump":
                      ParseDotDumpArgs(parts, out var dumpTarget, out var wantAll, out var max);
                      PrintDotDump(dumpTarget, wantAll, max);
                      break;
              }
          }

         private static DotDebugOutputTarget ParseDotDebugTarget(string[] args, DotDebugOutputTarget defaultTarget)
         {
             var target = defaultTarget;
             for (var i = 1; i < args.Length; i++)
             {
                 if (string.Equals(args[i], "log", StringComparison.OrdinalIgnoreCase))
                 {
                     target = DotDebugOutputTarget.DalamudLog;
                     continue;
                 }

                 if (string.Equals(args[i], "file", StringComparison.OrdinalIgnoreCase))
                 {
                     target = DotDebugOutputTarget.File;
                     continue;
                 }

                 if (string.Equals(args[i], "chat", StringComparison.OrdinalIgnoreCase))
                 {
                     target = DotDebugOutputTarget.Chat;
                 }
             }

             return target;
         }

         private static void ParseDotDumpArgs(string[] args, out DotDebugOutputTarget target, out bool wantAll, out int max)
         {
             target = DotDebugOutputTarget.Chat;
             wantAll = false;
             max = 10;

             for (var i = 1; i < args.Length; i++)
             {
                 if (string.Equals(args[i], "all", StringComparison.OrdinalIgnoreCase))
                 {
                     wantAll = true;
                     continue;
                 }

                 if (string.Equals(args[i], "log", StringComparison.OrdinalIgnoreCase))
                 {
                     target = DotDebugOutputTarget.DalamudLog;
                     continue;
                 }

                 if (string.Equals(args[i], "file", StringComparison.OrdinalIgnoreCase))
                 {
                     target = DotDebugOutputTarget.File;
                     continue;
                 }

                 if (string.Equals(args[i], "chat", StringComparison.OrdinalIgnoreCase))
                 {
                     target = DotDebugOutputTarget.Chat;
                     continue;
                 }

                 if (int.TryParse(args[i], out var parsed))
                     max = parsed;
             }

             max = target == DotDebugOutputTarget.Chat
                 ? Math.Clamp(max, 1, 20)
                 : Math.Clamp(max, 1, 200);
         }

         internal void PrintDotStats(DotDebugOutputTarget target)
         {
             try
             {
                 var stats = GetDotCaptureStats();

                 var battle = Battles.Count > 0 ? Battles[^1] : null;
                 var selfId = DalamudApi.ObjectTable.LocalPlayer?.EntityId ?? 0u;
                 var selfDotTickDamage = 0L;
                 var selfDotSimDamage = 0L;
                 var selfDotTicks = 0;
                 if (battle != null && selfId != 0)
                 {
                     if (battle.DotDamageByActor.TryGetValue(selfId, out var dotDamage))
                         selfDotTickDamage = dotDamage;
                     if (battle.DotTickCountByActor.TryGetValue(selfId, out var dotTicks))
                         selfDotTicks = dotTicks;

                     if (battle.DotDmgList.Count > 0)
                     {
                         foreach (var (active, dotDmg) in battle.DotDmgList)
                         {
                             if ((uint)(active & 0xFFFFFFFF) == selfId)
                                 selfDotSimDamage += (long)dotDmg;
                         }
                     }
                 }

                 var selfDotTotalDamage = selfDotTickDamage + selfDotSimDamage;

                 var lines = new List<string>(4)
                 {
                     $"[DalamudACT] DoTStats 入队 {stats.EnqueuedTotal}（Legacy {stats.EnqueuedLegacy}, Network {stats.EnqueuedNetwork}） 处理 {stats.Processed} 去重丢弃 {stats.DedupDropped} 非法丢弃 {stats.DroppedInvalid}",
                     $"[DalamudACT] DoTStats 未知来源 {stats.UnknownSource} buffId=0 {stats.UnknownBuff} 推断src {stats.InferredSource} 推断buff {stats.InferredBuff}",
                     $"[DalamudACT] DoTStats 归因 Action {stats.AttributedToAction} Status {stats.AttributedToStatus} TotalOnly {stats.AttributedToTotalOnly} 自己Tick {selfDotTicks} Tick伤害 {selfDotTickDamage:N0} 模拟 {selfDotSimDamage:N0} 合计 {selfDotTotalDamage:N0}",
                 };

                 DotDebugOutput.WriteLines(target, lines);

                 if (target == DotDebugOutputTarget.File)
                 {
                     var path = DotDebugOutput.GetFilePath();
                     if (!string.IsNullOrWhiteSpace(path))
                         DalamudApi.Log.Information($"[DalamudACT] DoTStats 已写入: {path}");
                 }
             }
             catch (Exception e)
             {
                 DalamudApi.Log.Error(e, "[DalamudACT] Failed to print dot stats.");
             }
         }

         internal void PrintDotDump(DotDebugOutputTarget target, bool wantAll, int max)
         {
             try
             {
                 var selfId = DalamudApi.ObjectTable.LocalPlayer?.EntityId ?? 0u;

                 var all = dotCapture.GetRecentEventsSnapshot();
                 if (all.Length == 0)
                 {
                     DotDebugOutput.WriteLine(target, "[DalamudACT] DotDump 暂无事件（可先进入战斗或等待 DoT tick）。");
                     return;
                 }

                 var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                 var filtered = new List<DotEventCapture.DotRecentEvent>(all.Length);
                 foreach (var e in all)
                 {
                     if (wantAll)
                     {
                         filtered.Add(e);
                         continue;
                     }

                     if (selfId != 0 && e.ResolvedSourceId == selfId)
                         filtered.Add(e);
                 }

                 if (!wantAll && filtered.Count == 0)
                 {
                     DotDebugOutput.WriteLine(target, $"[DalamudACT] DotDump 当前缓存 {all.Length} 条，未命中自己({selfId:X})的 DoT tick。");
                     return;
                 }

                 var start = Math.Max(0, filtered.Count - max);
                 var show = filtered.GetRange(start, filtered.Count - start);

                 var scopeText = wantAll ? "全部" : "自己";
                 var lines = new List<string>(show.Count + 2)
                 {
                     $"[DalamudACT] DotDump {scopeText} 最近 {show.Count}/{filtered.Count} 条（总缓存 {all.Length}，最大输出 {max}）。",
                 };

                 foreach (var e in show)
                 {
                     var ageMs = nowMs - e.TimeMs;
                     if (ageMs < 0) ageMs = 0;

                     var srcText = e.OriginalSourceId == e.ResolvedSourceId
                         ? $"{e.ResolvedSourceId:X}"
                         : $"{e.OriginalSourceId:X}->{e.ResolvedSourceId:X}";

                     var buffText = e.OriginalBuffId == e.ResolvedBuffId
                         ? $"{e.ResolvedBuffId}"
                         : $"{e.OriginalBuffId}->{e.ResolvedBuffId}";

                     var attrText = e.Attribution switch
                     {
                         DotEventCapture.DotAttributionKind.UnknownSource => $"UnknownSrc({e.AttributionId})",
                         DotEventCapture.DotAttributionKind.Action => $"Action({e.AttributionId})",
                         DotEventCapture.DotAttributionKind.Status => $"Status({e.AttributionId})",
                         DotEventCapture.DotAttributionKind.TotalOnly => "TotalOnly",
                         _ => "-",
                     };

                     var dropText = e.DropReason == DotEventCapture.DotDropReason.None ? "" : $" DROP={e.DropReason}";

                     lines.Add($"[DalamudACT] DotDump ago={ageMs}ms ch={e.Channel} src={srcText} tgt={e.OriginalTargetId:X} buff={buffText} dmg={e.Damage} attr={attrText}{dropText}");
                 }

                 if (target == DotDebugOutputTarget.File)
                     lines.Add(string.Empty);

                 DotDebugOutput.WriteLines(target, lines);

                 if (target == DotDebugOutputTarget.File)
                 {
                     var path = DotDebugOutput.GetFilePath();
                     if (!string.IsNullOrWhiteSpace(path))
                         DalamudApi.Log.Information($"[DalamudACT] DotDump 已写入: {path}");
                 }
             }
             catch (Exception e)
             {
                 DalamudApi.Log.Error(e, "[DalamudACT] Failed to print dot dump.");
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

        internal DotCaptureStats GetDotCaptureStats() => dotCapture.GetStatsSnapshot();

        internal void ResetDotCaptureCaches() => dotCapture.ResetTransientCaches();
    }
}
