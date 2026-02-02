// ACT 插件入口与事件 Hook，负责战斗事件采集与统计更新。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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

         // ACT 的 ActiveEncounter 以“全局战斗事件 + 失活超时”决定遭遇结束。
         // 为对齐 ACT，插件也使用相同的“最近事件时间”超时策略，而非仅依赖本地 InCombat 标志。
         // 对齐 ACT：遭遇结束延迟（可配置）。过短会在 boss 转阶段/脱战瞬间误分段。
         private int EncounterTimeoutMs => Math.Clamp(Configuration.EncounterTimeoutMs, 1000, 120000);
        // 兜底：当本地仍处于 InCombat，但一段时间没有可计入的战斗事件（如 boss 转阶段上天/无敌），
        // 用 InCombat 做 keepalive 推进遭遇计时，避免误分段为两场战斗。
        private const int EncounterInCombatKeepAliveIntervalMs = 1000;
         private const int ActMcpPollIntervalMs = 1000;
         private const int ActMcpConnectTimeoutMs = 800;
         private const int ActMcpStaleGraceMs = 5000;

         private long actMcpLastPollMs;
         private Task? actMcpPollTask;

         private readonly object actDllProbeGate = new();
         private string actDllProbeSummary = string.Empty;
         private string actDllProbeUtc = string.Empty;
         private Task? actDllProbeTask;

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

        internal enum BattleStatsMode : byte
        {
            Config = 0,
            Local = 1,
            Act = 2,
            Both = 3,
        }


        #region OPcode & Hook functions

         private unsafe void Ability(ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targets, uint sourceId)
         {
              var length = header->NumTargets;
              if (length == 0) return;

              DalamudApi.Log.Verbose($"-----------------------Ability{length}:{sourceId:X}------------------------------");

               var eventTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
 
               var actionId = header->SpellId != 0 ? (uint)header->SpellId : header->ActionId;
 
               // 对齐 ACT：遭遇计时由“战斗相关的行动/效果”驱动，否则在 boss 转阶段（无伤害但仍有动作）时会过早分段。
               // 这里仅推进遭遇生命周期（Start/Last/End），不写入任何玩家伤害数据。
               var hasBattleNpc = sourceId is > 0x4000_0000 and not 0xE000_0000;
               if (!hasBattleNpc)
               {
                   for (var i = 0; i < length; i++)
                   {
                       var targetId = (uint)(targets[i] & uint.MaxValue);
                       if (targetId is > 0x4000_0000 and not 0xE000_0000)
                       {
                           hasBattleNpc = true;
                           break;
                       }
                   }
               }

               var hasDamageLikeEffect = false;
               for (var i = 0; i < length && !hasDamageLikeEffect; i++)
               {
                   for (var j = 0; j < 8; j++)
                   {
                       ref var e = ref effects[i].Effects[j];
                       if (e.Type is 3 or 5 or 6) // Damage / Blocked / Parried
                       {
                           hasDamageLikeEffect = true;
                           break;
                       }
                   }
               }

               var encounterStarted = false;
               lock (SyncRoot)
                   encounterStarted = Battles[^1].StartTime != 0;

               var inCombatNow = DalamudApi.Conditions.Any(ConditionFlag.InCombat);
               if (hasDamageLikeEffect || hasBattleNpc || (encounterStarted && inCombatNow))
               {
                   lock (SyncRoot)
                       Battles[^1].MarkEncounterActivityOnly(eventTimeMs);
               }
 
               // Network DoT tick：在某些环境/设置下，ActorControlSelf 可能无法覆盖全部 DoT（尤其是他人/召唤物/地面 DoT）。
               // 当 ActionEffect 的 sourceId 为 0xE0000000 时，将其作为“未知来源 DoT tick”入队，交由 DotEventCapture 统一归因与去重。
               if (sourceId == 0xE000_0000)
               {
                 var isDotLike = actionId != 0 &&
                                 (Potency.DotPot.ContainsKey(actionId) ||
                                  Potency.DotPot94.ContainsKey(actionId) ||
                                  Potency.BuffToAction.ContainsKey(actionId));
                 // 兼容：DotPot 表可能缺失新/特殊 DoT（尤其是地面 DoT / 区域技能）。
                 // 若启用了增强归因，则允许通过“目标状态表存在该 StatusId”来放行。
                 if (!isDotLike && Configuration.EnableEnhancedDotCapture && actionId != 0)
                 {
                     for (var i = 0; i < length && !isDotLike; i++)
                     {
                         var targetId = (uint)(targets[i] & uint.MaxValue);
                         if (targetId is 0 or 0xE000_0000 or <= 0x4000_0000) continue;

                         var obj = DalamudApi.ObjectTable.SearchByEntityId(targetId);
                         if (obj == null || obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc) continue;

                         foreach (var status in ((Dalamud.Game.ClientState.Objects.Types.IBattleNpc)obj).StatusList)
                         {
                             if (status.StatusId == actionId)
                             {
                                 isDotLike = true;
                                 break;
                             }
                         }
                     }
                 }

                 if (!isDotLike)
                     return;

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
                                 dotCapture.Enqueue(new DotTickEvent(
                                     eventTimeMs,
                                     sourceId,
                                     targetId,
                                     buffId: actionId,
                                     damage: damage,
                                     DotTickChannel.NetworkActorControlSelf));
                             }
                         }
                     }
                 }

                 return;
             }

             // Network DoT tick（非 0xE0000000 来源）：部分周期性伤害会以施放者为 sourceId 上报。
             // 为对齐 ACT 的 DoT 口径：当 actionId 命中 DoT 表时，将其统一交由 DotEventCapture 归因/去重，
             // 避免被当作普通技能伤害计入 baseDamage，导致 DoT 统计偏低。
             if (Configuration.EnableEnhancedDotCapture && actionId != 0)
             {
                 var isDotLike = Potency.DotPot.ContainsKey(actionId) ||
                                 Potency.DotPot94.ContainsKey(actionId) ||
                                 Potency.BuffToAction.ContainsKey(actionId);
                 if (isDotLike)
                 {
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
                                     dotCapture.Enqueue(new DotTickEvent(
                                         eventTimeMs,
                                         sourceId,
                                         targetId,
                                         buffId: actionId,
                                         damage: damage,
                                         DotTickChannel.NetworkActorControl));
                                 }
                             }
                         }
                     }

                     return;
                 }
             }

              var originalSourceId = sourceId;
              if (sourceId > 0x40000000 && sourceId != 0xE0000000)
              {
                  sourceId = ACTBattle.ResolveOwner(sourceId);
                  if (sourceId == 0xE0000000 && originalSourceId != 0xE0000000)
                  {
                      // 对齐 ACT：优先尝试用缓存/对象表补齐 owner 关系，尽量避免“召唤物伤害丢失”。
                      ACTBattle.WarmOwnerCacheFromObjectTable(eventTimeMs);
                      sourceId = ACTBattle.ResolveOwner(originalSourceId);
                  }
             }

            if (sourceId == 0xE0000000 && originalSourceId != 0xE0000000) return;
            if (sourceId is > 0x40000000 or 0x0) return;

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
            if (source is > 0x4000_0000 and not 0xE000_0000)
            {
                // 对齐 ACT：敌方施法也应驱动遭遇计时（常见于转阶段/无敌期：有动作但无伤害）。
                var eventTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                lock (SyncRoot)
                    Battles[^1].MarkEncounterActivityOnly(eventTimeMs);
                return;
            }
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

        private bool CheckTime()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lock (SyncRoot)
            {
                var battle = Battles[^1];

                 if (DalamudApi.ObjectTable.LocalPlayer != null)
                     battle.Zone = GetPlaceName();

                 if (Configuration.EnableActMcpSync && Configuration.PreferActMcpTotals && battle.ActEncounter != null)
                 {
                     var inCombatNow = DalamudApi.Conditions.Any(ConditionFlag.InCombat);
                     var stale = IsActEncounterStale(battle.ActEncounter, now);

                     // Boss 转阶段/长时间不可打等情况下，ACT 的 endTime 可能长时间不更新。
                     // 若此时仍处于战斗状态，不应清空 ACT 快照，避免 UI/历史回退到本地口径导致“DOT 比例对不上/误分段”。
                     if (stale && !inCombatNow)
                         battle.SetActEncounter(null);
                     else
                         battle.SyncEncounterTimingFromAct(battle.ActEncounter, EncounterTimeoutMs);
                 }

                 // 对齐 ACT：遭遇结束由“最后一条战斗事件时间 + 超时”决定。
                 if (battle.StartTime != 0 && battle.LastEventTime > 0)
                 {
                      var inCombatNow = DalamudApi.Conditions.Any(ConditionFlag.InCombat);

                      if (inCombatNow && now - battle.LastEventTime >= EncounterInCombatKeepAliveIntervalMs)
                          battle.MarkEncounterActivityOnly(now);

                       if (!inCombatNow && now - battle.LastEventTime > EncounterTimeoutMs)
                       {
                           // 避免“空白遭遇”挤占历史：仅在该场遭遇确实产生过统计数据时才写入历史。
                           // 否则直接重置当前战斗槽位（通常是遭遇计时被动作/环境事件误触发导致）。
                           if (!battle.HasMeaningfulData())
                           {
                               Battles[^1] = new ACTBattle(0, 0);
                               dotCapture.ResetTransientCaches();
                               return false;
                           }

                           battle.EndTime = battle.LastEventTime;
                           battle.ActiveDots.Clear();
                           ACTBattle.ClearOwnerCache();

                          if (Battles.Count == 5)
                             Battles.RemoveAt(0);
                     
                         Battles.Add(new ACTBattle(0, 0));
                        return true;
                      }

                       // 对齐 ACT：进行中遭遇的 EndTime 采用“最后战斗事件时间 + 超时缓冲”。
                       // ACT 的 ActiveEncounter 会把 EndTime 推进到“预计结束时刻”，因此 ENCDPS 会在最后一条事件后继续平滑变化直至超时结束。
                      battle.EndTime = battle.LastEventTime + EncounterTimeoutMs;
                  }
              }

             return false;
          }

        private bool IsActEncounterStale(ActMcpEncounterSnapshot encounter, long nowMs)
        {
            if (encounter.EndTimeMs <= 0) return false;
            var ageMs = nowMs - encounter.EndTimeMs;
            if (ageMs <= 0) return false;
            return ageMs > EncounterTimeoutMs + ActMcpStaleGraceMs;
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
             ACTBattle.ActLikeOwnerAttribution = Configuration.EnableActLikeAttribution;
             ACTBattle.WarmOwnerCacheFromObjectTable();
               lock (SyncRoot)
                   dotCapture.FlushInto(Battles[^1]);
               var ended = CheckTime();
               if (ended)
               {
                   if (Configuration.AutoExportDotDumpOnEnd)
                   {
                       PrintDotStats(DotDebugOutputTarget.File);
                       PrintDotDump(DotDebugOutputTarget.File, wantAll: true, max: Configuration.AutoExportDotDumpMax);
                   }

                   if (Configuration.AutoExportBattleStatsOnEnd)
                   {
                       var shouldExport = false;
                       lock (SyncRoot)
                       {
                           if (Battles.Count >= 2)
                           {
                               var endedBattle = Battles[^2];
                               shouldExport = endedBattle.HasMeaningfulData();
                           }
                       }

                       if (shouldExport)
                       {
                           var mode = Configuration.EnableActMcpSync ? BattleStatsMode.Both : BattleStatsMode.Local;
                           PrintBattleStats(BattleStatsOutputTarget.File, topN: 0, mode: mode);
                       }
                   }

                   dotCapture.ResetTransientCaches();
               }

               PollActMcpMaybe();
           }

          private void PollActMcpMaybe()
          {
              if (!Configuration.EnableActMcpSync) return;
 
              var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
              var inCombatNow = DalamudApi.Conditions.Any(ConditionFlag.InCombat);
              var inCombatSnapshot = inCombatNow;
              var pollIntervalMs = inCombatNow ? ActMcpPollIntervalMs : 10000;
              if (nowMs - actMcpLastPollMs < pollIntervalMs) return;
              if (actMcpPollTask != null && !actMcpPollTask.IsCompleted) return;
              actMcpLastPollMs = nowMs;

             var pipeName = Configuration.ActMcpPipeName ?? string.Empty;
             if (string.IsNullOrWhiteSpace(pipeName)) return;

             var selfName = DalamudApi.ObjectTable.LocalPlayer?.Name.TextValue;

             actMcpPollTask = Task.Run(async () =>
             {
                 var encounter = await ActMcpClient.TryGetEncounterAsync(
                     pipeName,
                     selfName,
                     connectTimeoutMs: ActMcpConnectTimeoutMs,
                     cancellationToken: CancellationToken.None).ConfigureAwait(false);

                 if (encounter == null) return;
                 if (encounter.CombatantsByName.Count == 0) return;

                 var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                 if (IsActEncounterStale(encounter, now) && !inCombatSnapshot) return;
                 lock (SyncRoot)
                 {
                     // 避免“空白战斗槽位”被 ACT 的上一场遭遇反复覆盖，导致历史记录不断被空白挤掉：
                     // 当当前槽位尚未开始（Start/Last/End=0 且无本地数据）时，把 ACT 快照写入上一场战斗对象。
                     var battle = Battles[^1];
                     if (Battles.Count >= 2 &&
                         battle.StartTime == 0 &&
                         battle.EndTime == 0 &&
                         battle.LastEventTime == 0 &&
                         battle.DataDic.Count == 0)
                     {
                         battle = Battles[^2];
                     }
                     battle.SetActEncounter(encounter);
                     if (Configuration.PreferActMcpTotals)
                         battle.SyncEncounterTimingFromAct(encounter, EncounterTimeoutMs);
                 }
             });
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
                  HelpMessage = "/act 开关独立名片\n/act config 打开设置窗口\n/act prev 查看上一场\n/act next 查看下一场\n/act clear 清空战斗记录\n/act stats [local|act|both] [log|file] [N] 导出战斗统计快照（调试/对齐 ACT）\n/act dotstats [log|file] 输出 DoT 采集统计（调试）\n/act dotdump [log|file] [all] [N] 输出最近 DoT tick 事件（调试）\n/act actdll [init] 探测加载 ACT/FFXIV_ACT_Plugin DLL（实验，高风险）"
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
                   case "stats":
                       ParseBattleStatsArgs(parts, out var statsTarget, out var top, out var mode);
                       PrintBattleStats(statsTarget, top, mode);
                       break;
                    case "dotdump":
                        ParseDotDumpArgs(parts, out var dumpTarget, out var wantAll, out var max);
                        PrintDotDump(dumpTarget, wantAll, max);
                        break;
                    case "actdll":
                        var tryInit = parts.Length > 1 && string.Equals(parts[1], "init", StringComparison.OrdinalIgnoreCase);
                        ProbeActDllBridge(tryInit);
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

          private static void ParseBattleStatsArgs(string[] args, out BattleStatsOutputTarget target, out int top, out BattleStatsMode mode)
          {
              target = BattleStatsOutputTarget.File;
              top = 0; // 0=全部
              mode = BattleStatsMode.Config;

              for (var i = 1; i < args.Length; i++)
              {
                  if (string.Equals(args[i], "local", StringComparison.OrdinalIgnoreCase))
                  {
                      mode = BattleStatsMode.Local;
                      continue;
                  }

                  if (string.Equals(args[i], "act", StringComparison.OrdinalIgnoreCase))
                  {
                      mode = BattleStatsMode.Act;
                      continue;
                  }

                  if (string.Equals(args[i], "both", StringComparison.OrdinalIgnoreCase))
                  {
                      mode = BattleStatsMode.Both;
                      continue;
                  }

                  if (string.Equals(args[i], "log", StringComparison.OrdinalIgnoreCase))
                  {
                      target = BattleStatsOutputTarget.DalamudLog;
                      continue;
                  }

                  if (string.Equals(args[i], "file", StringComparison.OrdinalIgnoreCase))
                  {
                      target = BattleStatsOutputTarget.File;
                      continue;
                  }

                  if (string.Equals(args[i], "chat", StringComparison.OrdinalIgnoreCase))
                  {
                      target = BattleStatsOutputTarget.Chat;
                      continue;
                  }

                  if (int.TryParse(args[i], out var parsed))
                      top = parsed;
              }

              top = Math.Clamp(top, 0, 200);
          }

           internal void PrintBattleStats(BattleStatsOutputTarget target, int topN, BattleStatsMode mode = BattleStatsMode.Config)
           {
               try
               {
                    ACTBattle? battle = null;
                    ActMcpEncounterSnapshot? actEncounter = null;
                    lock (SyncRoot)
                    {
                        ACTBattle? fallback = null;
                        ActMcpEncounterSnapshot? fallbackAct = null;
                        for (var i = Battles.Count - 1; i >= 0; i--)
                        {
                            var b = Battles[i];
                           if (b.HasMeaningfulData())
                            {
                                battle = b;
                                actEncounter = b.ActEncounter;
                                break;
                            }

                            if (fallback == null && (b.StartTime != 0 || b.EndTime != 0))
                            {
                                fallback = b;
                                fallbackAct = b.ActEncounter;
                            }
                        }

                        if (battle == null && fallback != null)
                        {
                            battle = fallback;
                            actEncounter = fallbackAct;
                        }
                    }

                   if (battle == null)
                   {
                       if (target == BattleStatsOutputTarget.File)
                       {
                           var payload = new Dictionary<string, object?>
                           {
                               ["utc"] = DateTimeOffset.UtcNow.ToString("O"),
                               ["source"] = "diagnostic",
                               ["message"] = "(no battle)",
                           };
                           BattleStatsOutput.WriteLine(target, JsonSerializer.Serialize(payload));
                       }
                       else
                       {
                           BattleStatsOutput.WriteLine(target, "[DalamudACT] BattleStats: (no battle)");
                       }
                       return;
                   }

                    var actEncounterFresh = false;
                    if (mode is BattleStatsMode.Act or BattleStatsMode.Both)
                    {
                        try
                        {
                            var pipeName = Configuration.ActMcpPipeName ?? string.Empty;
                           if (!string.IsNullOrWhiteSpace(pipeName))
                           {
                               var selfName = DalamudApi.ObjectTable.LocalPlayer?.Name.TextValue;
                               var latest = ActMcpClient.TryGetEncounterAsync(
                                       pipeName,
                                       selfName,
                                       connectTimeoutMs: ActMcpConnectTimeoutMs,
                                       cancellationToken: CancellationToken.None)
                                   .GetAwaiter()
                                    .GetResult();
                                if (latest != null && latest.CombatantsByName.Count > 0)
                                {
                                    actEncounter = latest;
                                    actEncounterFresh = true;
                                    lock (SyncRoot)
                                        battle.SetActEncounter(latest);
                                }
                            }
                        }
                        catch
                        {
                            // ignore on-demand ACT snapshot failures; fall back to cached data
                        }
                    }

                    var hasAct = actEncounter != null && actEncounter.CombatantsByName.Count > 0;
                    BattleStatsSnapshot snapshot;
                    lock (SyncRoot)
                        snapshot = BattleStatsSnapshot.FromBattle(battle);

                   if (mode == BattleStatsMode.Both)
                   {
                       var utc = DateTimeOffset.UtcNow.ToString("O");
                       var pairId = Guid.NewGuid().ToString("N");

                       WriteBattleStatsOne(target, topN, snapshot, useAct: false, actEncounter, utc, pairId);
                       if (hasAct)
                       {
                           WriteBattleStatsOne(target, topN, snapshot, useAct: true, actEncounter, utc, pairId, actSnapshotOnDemand: actEncounterFresh);
                       }
                       else
                       {
                           if (target == BattleStatsOutputTarget.File)
                           {
                               var diag = new Dictionary<string, object?>
                               {
                                   ["utc"] = utc,
                                   ["source"] = "act_mcp",
                                   ["pairId"] = pairId,
                                   ["error"] = "no_encounter_snapshot",
                               };
                               BattleStatsOutput.WriteLine(target, JsonSerializer.Serialize(diag));
                           }
                           else
                           {
                               BattleStatsOutput.WriteLine(target, "[DalamudACT] BattleStats: (no ACT encounter snapshot)");
                           }
                       }

                       return;
                   }

                  var wantAct = mode switch
                  {
                      BattleStatsMode.Act => true,
                      BattleStatsMode.Local => false,
                      _ => Configuration.PreferActMcpTotals,
                  };

                   if (mode == BattleStatsMode.Act && !hasAct)
                   {
                       if (target == BattleStatsOutputTarget.File)
                       {
                           var payload = new Dictionary<string, object?>
                           {
                               ["utc"] = DateTimeOffset.UtcNow.ToString("O"),
                               ["source"] = "act_mcp",
                               ["error"] = "no_encounter_snapshot",
                           };
                           BattleStatsOutput.WriteLine(target, JsonSerializer.Serialize(payload));
                       }
                       else
                       {
                           BattleStatsOutput.WriteLine(target, "[DalamudACT] BattleStats: (no ACT encounter snapshot)");
                       }
                       return;
                   }

                   WriteBattleStatsOne(target, topN, snapshot, useAct: wantAct && hasAct, actEncounter, DateTimeOffset.UtcNow.ToString("O"), pairId: null, actSnapshotOnDemand: actEncounterFresh);
               }
               catch (Exception e)
               {
                   DalamudApi.Log.Error(e, "[DalamudACT] Failed to print battle stats.");
              }
          }

           private void WriteBattleStatsOne(
               BattleStatsOutputTarget target,
               int topN,
               BattleStatsSnapshot battle,
               bool useAct,
               ActMcpEncounterSnapshot? actEncounter,
               string utc,
               string? pairId,
               bool? actSnapshotOnDemand = null)
           {
               var canSimDots = !useAct && battle.Level is >= 64 && !float.IsInfinity(battle.TotalDotSim) && battle.TotalDotSim != 0;

               var dotByActor = new Dictionary<uint, float>();
               if (canSimDots && battle.TotalDotDamage > 0 && battle.DotDmgList.Count > 0)
               {
                   foreach (var (active, dotDmg) in battle.DotDmgList)
                   {
                       var source = (uint)(active & 0xFFFFFFFF);
                       dotByActor[source] = dotByActor.TryGetValue(source, out var cur) ? cur + dotDmg : dotDmg;
                   }
               }

               var encSeconds = useAct ? actEncounter!.DurationSeconds() : battle.DurationSeconds;

                List<Dictionary<string, object?>> actors;
                if (useAct && actEncounter != null && actEncounter.CombatantsByName.Count > 0)
                {
                    var localByName = new Dictionary<string, BattleStatsActorSnapshot>(StringComparer.Ordinal);
                    var usedIds = new HashSet<uint>();
                    foreach (var a in battle.Actors)
                    {
                        usedIds.Add(a.ActorId);
                        if (!string.IsNullOrWhiteSpace(a.Name) && !localByName.ContainsKey(a.Name))
                            localByName[a.Name] = a;
                    }

                    var syntheticId = 0xF000_0000u;
                    uint AllocateSyntheticId()
                    {
                        while (usedIds.Contains(syntheticId) || syntheticId == 0xE000_0000u)
                            syntheticId++;
                        usedIds.Add(syntheticId);
                        return syntheticId++;
                    }

                    actors = new List<Dictionary<string, object?>>(actEncounter.CombatantsByName.Count);
                    foreach (var (name, actCombatant) in actEncounter.CombatantsByName)
                    {
                        var hasLocal = localByName.TryGetValue(name, out var local);
                        var actorId = hasLocal ? local.ActorId : AllocateSyntheticId();
                        var jobId = hasLocal ? local.JobId : 0u;

                        var totalDamage = actCombatant.Damage;
                        var actDotDamage = actCombatant.DotDamage;
                        var encDps = actCombatant.EncDps;
                        var dps = actCombatant.Dps;
                        var activeSeconds = 0f;
                        if (dps > 0)
                            activeSeconds = (float)(totalDamage / dps);
                        else if (hasLocal)
                            activeSeconds = local.ActiveSeconds;

                        actors.Add(new Dictionary<string, object?>
                        {
                            ["actorId"] = $"0x{actorId:X8}",
                            ["name"] = name,
                            ["jobId"] = jobId,
                            ["baseDamage"] = totalDamage,
                            ["dotTickDamage"] = 0L,
                            ["dotTickCount"] = 0,
                            ["dotSkillDamage"] = 0L,
                            ["dotTotalOnlyTickDamage"] = 0L,
                            ["dotTotalOnlyTickCount"] = 0,
                            ["dotSimDamage"] = 0L,
                            ["actDotDamage"] = actDotDamage,
                            ["totalDamage"] = totalDamage,
                            ["encdps"] = encDps,
                            ["dps"] = dps,
                            ["activeSeconds"] = activeSeconds,
                        });
                    }
                }
                else
                {
                    actors = new List<Dictionary<string, object?>>(battle.Actors.Count);
                    foreach (var a in battle.Actors)
                    {
                        var actorId = a.ActorId;
                        var name = a.Name;
                        var baseDamage = a.BaseDamage;
                        var dotSimDamage = dotByActor.TryGetValue(actorId, out var sim) ? (long)sim : 0L;
                        var totalDamage = baseDamage + dotSimDamage;
                        var dotTickDamage = a.DotTickDamage;
                        var dotTickCount = a.DotTickCount;
                        var dotSkillDamage = a.DotSkillDamage;
                        var dotTotalOnlyTickDamage = a.DotTotalOnlyTickDamage;
                        var dotTotalOnlyTickCount = a.DotTotalOnlyTickCount;
                        var activeSeconds = a.ActiveSeconds;
                        var encDps = encSeconds <= 0 ? 0d : totalDamage / (double)encSeconds;
                        var dps = activeSeconds <= 0 ? 0d : totalDamage / (double)activeSeconds;

                        actors.Add(new Dictionary<string, object?>
                        {
                            ["actorId"] = $"0x{actorId:X8}",
                            ["name"] = name,
                            ["jobId"] = a.JobId,
                            ["baseDamage"] = baseDamage,
                            ["dotTickDamage"] = dotTickDamage,
                            ["dotTickCount"] = dotTickCount,
                            ["dotSkillDamage"] = dotSkillDamage,
                            ["dotTotalOnlyTickDamage"] = dotTotalOnlyTickDamage,
                            ["dotTotalOnlyTickCount"] = dotTotalOnlyTickCount,
                            ["dotSimDamage"] = dotSimDamage,
                            ["actDotDamage"] = null,
                            ["totalDamage"] = totalDamage,
                            ["encdps"] = encDps,
                            ["dps"] = dps,
                            ["activeSeconds"] = activeSeconds,
                        });
                    }
                }

               if (actors.Count > 1)
                   actors.Sort((a, b) => Convert.ToInt64(b["totalDamage"]).CompareTo(Convert.ToInt64(a["totalDamage"])));

              if (topN > 0 && actors.Count > topN)
                  actors = actors.GetRange(0, topN);

                var actorDamageTotal = 0L;
                foreach (var a in actors)
                    actorDamageTotal += Convert.ToInt64(a["totalDamage"]);

                var dotTickDamageTotal = 0L;
                var dotSimDamageTotal = 0L;
                foreach (var a in actors)
                {
                    dotTickDamageTotal += Convert.ToInt64(a["dotTickDamage"]);
                    dotSimDamageTotal += Convert.ToInt64(a["dotSimDamage"]);
                }

                var totalDamageAll = actorDamageTotal;
                if (!useAct && battle.TotalDotDamage != 0)
                {
                    if (!canSimDots)
                    {
                        totalDamageAll += battle.TotalDotDamage;
                    }
                    else
                    {
                        // 若启用了模拟分配，则 dotSimDamage 已计入各 Actor，总量需补上未被分配的那部分未知来源 DoT。
                        var unassigned = battle.TotalDotDamage - dotSimDamageTotal;
                        if (unassigned > 0)
                            totalDamageAll += unassigned;
                    }
                }

               var zone = useAct && actEncounter != null && !string.IsNullOrWhiteSpace(actEncounter.Zone) ? actEncounter.Zone : (battle.Zone ?? "Unknown");
                var startTimeMs = useAct && actEncounter != null && actEncounter.StartTimeMs > 0
                    ? actEncounter.StartTimeMs
                    : battle.EffectiveStartTimeMs;
               var endTimeMs = useAct && actEncounter != null && actEncounter.EndTimeMs > 0 ? actEncounter.EndTimeMs : battle.EndTimeMs;
                // 对齐 ACT：totalDotDamage 应包含“已归因 tick + 未知来源 DoT 总量”。
                var totalDotDamage = useAct ? 0L : dotTickDamageTotal + battle.TotalDotDamage;
                var totalDotSim = useAct ? 0f : battle.TotalDotSim;

               if (useAct)
               {
                   var actDotTotal = 0L;
                   foreach (var a in actors)
                   {
                       if (!a.TryGetValue("actDotDamage", out var v) || v == null) continue;
                       actDotTotal += v switch
                       {
                           long l => l,
                           int i => i,
                           double d => (long)d,
                           float f => (long)f,
                           _ => long.TryParse(v.ToString(), out var parsed) ? parsed : 0L,
                       };
                   }

                   totalDotDamage = actDotTotal;
               }

               var payload = new Dictionary<string, object?>
               {
                  ["utc"] = utc,
                  ["source"] = useAct ? "act_mcp" : "local",
                  ["zone"] = zone,
                  ["level"] = battle.Level,
                  ["startTimeMs"] = startTimeMs,
                  ["endTimeMs"] = endTimeMs,
                  ["durationSeconds"] = encSeconds,
                  ["dpsTimeMode"] = Configuration.DpsTimeMode,
                  ["canSimDots"] = canSimDots,
                   ["totalDotDamage"] = totalDotDamage,
                   ["totalDotSim"] = totalDotSim,
                   ["limitBreakDamage"] = useAct ? 0L : battle.LimitBreakDamage,
                   ["totalDamageAll"] = totalDamageAll,
                   ["actors"] = actors,
               };

                if (useAct && actSnapshotOnDemand.HasValue)
                    payload["actSnapshotOnDemand"] = actSnapshotOnDemand.Value;

                if (useAct && actEncounter != null && actEncounter.CombatantsByName.Count > 0)
                {
                    const int actTop = 30;
                    var actTopCombatants = actEncounter.CombatantsByName
                        .OrderByDescending(static kv => kv.Value.Damage)
                        .Take(actTop)
                        .Select(static kv => (object?)new Dictionary<string, object?>
                        {
                            ["name"] = kv.Key,
                            ["damage"] = kv.Value.Damage,
                            ["dotDamage"] = kv.Value.DotDamage,
                            ["encdps"] = kv.Value.EncDps,
                            ["dps"] = kv.Value.Dps,
                        })
                        .ToList();

                    payload["actTopCombatants"] = actTopCombatants;
                }

               if (!string.IsNullOrWhiteSpace(pairId))
                   payload["pairId"] = pairId;

              var json = JsonSerializer.Serialize(payload);

               if (target == BattleStatsOutputTarget.Chat)
               {
                   BattleStatsOutput.WriteLine(target, $"[DalamudACT] BattleStats: {zone} {encSeconds:F1}s participants={battle.Actors.Count} source={(useAct ? "act_mcp" : "local")}");
                   BattleStatsOutput.WriteLine(target, "[DalamudACT] BattleStats: 请使用 /act stats file 导出 JSON（聊天栏不输出完整 JSON）。");
                   return;
               }

               BattleStatsOutput.WriteLine(target, json);

               if (target == BattleStatsOutputTarget.File)
               {
                   var path = BattleStatsOutput.GetFilePath();
                   if (!string.IsNullOrWhiteSpace(path))
                       DalamudApi.Log.Information($"[DalamudACT] BattleStats 已写入: {path}");
               }
           }

           private sealed class BattleStatsSnapshot
           {
               public required long EffectiveStartTimeMs { get; init; }
               public required long EndTimeMs { get; init; }
               public required float DurationSeconds { get; init; }
               public required string? Zone { get; init; }
               public required int? Level { get; init; }
               public required long TotalDotDamage { get; init; }
               public required float TotalDotSim { get; init; }
               public required Dictionary<long, float> DotDmgList { get; init; }
               public required long LimitBreakDamage { get; init; }
               public required List<BattleStatsActorSnapshot> Actors { get; init; }

                public static BattleStatsSnapshot FromBattle(ACTBattle battle)
                {
                    var actors = new List<BattleStatsActorSnapshot>(battle.DataDic.Count);
                    foreach (var (actorId, data) in battle.DataDic)
                    {
                        var name = battle.Name.TryGetValue(actorId, out var n) ? n : string.Empty;
                        var baseDamage = data.Damages.TryGetValue(0, out var total) ? total.Damage : 0L;
                        var activeSeconds = battle.ActorDuration(actorId);
                        var dotTickDamage = battle.DotDamageByActor.TryGetValue(actorId, out var dotDmg) ? dotDmg : 0L;
                        var dotTickCount = battle.DotTickCountByActor.TryGetValue(actorId, out var ticks) ? ticks : 0;
                        var dotTotalOnlyTickDamage = battle.DotTotalOnlyDamageByActor.TryGetValue(actorId, out var totalOnlyDmg) ? totalOnlyDmg : 0L;
                        var dotTotalOnlyTickCount = battle.DotTotalOnlyTickCountByActor.TryGetValue(actorId, out var totalOnlyTicks) ? totalOnlyTicks : 0;

                        var dotSkillDamage = 0L;
                        foreach (var (skillId, skillDamage) in data.Damages)
                        {
                            if (skillId == 0 || skillDamage.Damage <= 0)
                                continue;

                            if (Potency.DotPot.ContainsKey(skillId) ||
                                Potency.DotPot94.ContainsKey(skillId) ||
                                Potency.BuffToAction.ContainsValue(skillId))
                            {
                                dotSkillDamage += skillDamage.Damage;
                            }
                        }

                        actors.Add(new BattleStatsActorSnapshot(
                            actorId,
                            name,
                            data.JobId,
                            baseDamage,
                            activeSeconds,
                            dotTickDamage,
                            dotTickCount,
                            dotSkillDamage,
                            dotTotalOnlyTickDamage,
                            dotTotalOnlyTickCount));
                    }

                   var limitBreakDamage = battle.LimitBreak.Count > 0 ? battle.LimitBreak.Values.Sum() : 0L;

                   return new BattleStatsSnapshot
                   {
                       EffectiveStartTimeMs = battle.EffectiveStartTime(),
                       EndTimeMs = battle.EndTime,
                       DurationSeconds = battle.Duration(),
                       Zone = battle.Zone,
                       Level = battle.Level,
                       TotalDotDamage = battle.TotalDotDamage,
                       TotalDotSim = battle.TotalDotSim,
                       DotDmgList = new Dictionary<long, float>(battle.DotDmgList),
                       LimitBreakDamage = limitBreakDamage,
                       Actors = actors,
                   };
               }
           }

            private readonly record struct BattleStatsActorSnapshot(
                uint ActorId,
                string Name,
                uint JobId,
                long BaseDamage,
                float ActiveSeconds,
                long DotTickDamage,
                int DotTickCount,
                long DotSkillDamage,
                long DotTotalOnlyTickDamage,
                int DotTotalOnlyTickCount);



          internal void PrintDotStats(DotDebugOutputTarget target)
          {
              try
             {
                 var stats = GetDotCaptureStats();

                 var battle = Battles.Count > 0 ? Battles[^1] : null;
                 var selfId = DalamudApi.ObjectTable.LocalPlayer?.EntityId ?? 0u;
                 var selfDotTickDamage = 0L;
                 var selfDotSimDamage = 0L;
                 var selfDotTotalOnlyTickDamage = 0L;
                 var selfDotTicks = 0;
                 var selfDotTotalOnlyTicks = 0;
                 if (battle != null && selfId != 0)
                 {
                     if (battle.DotDamageByActor.TryGetValue(selfId, out var dotDamage))
                         selfDotTickDamage = dotDamage;
                     if (battle.DotTickCountByActor.TryGetValue(selfId, out var dotTicks))
                         selfDotTicks = dotTicks;
                     if (battle.DotTotalOnlyDamageByActor.TryGetValue(selfId, out var totalOnlyDamage))
                         selfDotTotalOnlyTickDamage = totalOnlyDamage;
                     if (battle.DotTotalOnlyTickCountByActor.TryGetValue(selfId, out var totalOnlyTicks))
                         selfDotTotalOnlyTicks = totalOnlyTicks;

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
                     $"[DalamudACT] DoTStats 未知来源 {stats.UnknownSource} buffId=0 {stats.UnknownBuff} 推断src {stats.InferredSource} 推断buff {stats.InferredBuff} 拒绝src(目标无DoT) {stats.RejectedSourceNotOnTarget}",
                     $"[DalamudACT] DoTStats 归因 Action {stats.AttributedToAction} Status {stats.AttributedToStatus} TotalOnly {stats.AttributedToTotalOnly} 自己Tick {selfDotTicks} Tick伤害 {selfDotTickDamage:N0} 未识别Tick {selfDotTotalOnlyTickDamage:N0}({selfDotTotalOnlyTicks:D}) 模拟 {selfDotSimDamage:N0} 合计 {selfDotTotalDamage:N0}",
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

          internal string GetActDllProbeStatusLine()
          {
              lock (actDllProbeGate)
              {
                  if (string.IsNullOrWhiteSpace(actDllProbeUtc) && string.IsNullOrWhiteSpace(actDllProbeSummary))
                      return "尚未探测";

                  if (string.IsNullOrWhiteSpace(actDllProbeUtc))
                      return actDllProbeSummary;

                  if (string.IsNullOrWhiteSpace(actDllProbeSummary))
                      return actDllProbeUtc;

                  return $"{actDllProbeUtc} {actDllProbeSummary}";
              }
          }

          internal void ProbeActDllBridge(bool tryInitPlugin)
          {
              if (!Configuration.EnableActDllBridgeExperimental)
              {
                  DalamudApi.ChatGui.Print("[DalamudACT] 未启用“ACT DLL 复用(实验)”开关，已拒绝执行。");
                  return;
              }

              var root = Configuration.ActDieMoeRoot ?? string.Empty;
              if (string.IsNullOrWhiteSpace(root))
              {
                  DalamudApi.ChatGui.Print("[DalamudACT] ActDieMoeRoot 为空，无法探测。");
                  return;
              }

              lock (actDllProbeGate)
              {
                  if (actDllProbeTask != null && !actDllProbeTask.IsCompleted)
                  {
                      DalamudApi.ChatGui.Print("[DalamudACT] ACT DLL 探测正在进行中…");
                      return;
                  }

                  actDllProbeUtc = DateTimeOffset.UtcNow.ToString("O");
                  actDllProbeSummary = "探测中…";
              }

              actDllProbeTask = Task.Run(() =>
              {
                  try
                  {
                      var result = ActDllBridge.Probe(root, tryInitPlugin);
                      foreach (var line in result.LogLines)
                          DalamudApi.Log.Information(line);
                      DalamudApi.Log.Information($"[ActDllBridge] SUMMARY: {result.Summary}");

                      lock (actDllProbeGate)
                      {
                          actDllProbeUtc = DateTimeOffset.UtcNow.ToString("O");
                          actDllProbeSummary = result.Summary;
                      }
                  }
                  catch (Exception e)
                  {
                      DalamudApi.Log.Error(e, "[ActDllBridge] Probe failed.");
                      lock (actDllProbeGate)
                      {
                          actDllProbeUtc = DateTimeOffset.UtcNow.ToString("O");
                          actDllProbeSummary = $"{e.GetType().Name}: {e.Message}";
                      }
                  }
              });
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
