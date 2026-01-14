using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures;
using Lumina.Excel;
using Action = Lumina.Excel.Sheets.Action;
using Status = Lumina.Excel.Sheets.Status;

namespace DalamudACT.Struct;

public class ACTBattle
{
    private const int Total = 0;


    public static ExcelSheet<Action>? ActionSheet;
    public static ExcelSheet<Status>? StatusSheet;
    private static readonly Dictionary<uint, uint> OwnerCache = new();
    private static readonly object OwnerCacheLock = new();
    //public static readonly Dictionary<uint, uint> Pet = new();
    public readonly Dictionary<uint, long> LimitBreak = new();

    public long StartTime;
    public long EndTime;
    public long LastEventTime;
    public string? Zone;
    public int? Level;
    public readonly Dictionary<uint, string> Name = new();
    public readonly Dictionary<uint, Data> DataDic = new();
    public readonly Dictionary<long, long> PlayerDotPotency = new();
    public readonly Dictionary<uint, long> DotDamageByActor = new();
    public readonly Dictionary<ulong, uint> DotSourceCache = new();
    public readonly Dictionary<ulong, uint> DotBuffCache = new();

    public readonly List<Dot> ActiveDots = new();
    public long TotalDotDamage;
    public float TotalDotSim;
    public Dictionary<long, float> DotDmgList = new();


    public ACTBattle(long time1, long time2)
    {
        StartTime = time1;
        EndTime = time2;
        Level = DalamudApi.PlayerState.EffectiveLevel;
    }

    public class Dot
    {
        public uint Source;
        public uint BuffId;
    }

    public class Data
    {
        public Dictionary<uint, SkillDamage> Damages = new();
        public uint PotSkill;
        public float SkillPotency;
        public uint JobId;
        public float Speed = 1f;
        public uint Death = 0;
        public uint MaxDamageSkill = 0;
        public uint MaxDamage = 0;

        public long FirstDamageTime;
        public long LastDamageTime;
    }

    public class SkillDamage
    {
        public long Damage = 0;
        public uint D = 0;
        public uint C = 0;
        public uint DC = 0;
        public uint swings = 0;

        public SkillDamage(long damage = 0)
        {
            Damage = damage;
        }

        public void AddDC(byte dc)
        {
            switch (dc)
            {
				case 64:
					D++;
					break;
				case 32:
					C++;
					break;
				case 96:
					DC++;
					D++;
					C++;
					break;
			}

            swings++;
        }

        public void AddDamage(long damage)
        {
            Damage += damage;
        }
    }
    


    private static float DurationSeconds(long startMs, long endMs)
    {
        if (startMs <= 0 || endMs <= 0) return 1f;
        var deltaMs = endMs - startMs;
        if (deltaMs <= 0) return 1f;
        var seconds = (float)Math.Floor(deltaMs / 1000.0);
        return seconds <= 0 ? 1f : seconds;
    }

    private static float GetJobPotencyMulti(uint jobId)
    {
        var index = (int)jobId;
        return index >= 0 && index < Potency.Muti.Length ? Potency.Muti[index] : 1f;
    }

    private static uint GetBaseSkill(uint jobId)
    {
        var index = (int)jobId;
        return index >= 0 && index < Potency.BaseSkill.Length ? Potency.BaseSkill[index] : 0u;
    }

    public float Duration() => DurationSeconds(StartTime, EndTime);

    public float ActorDuration(uint actor)
        => DataDic.TryGetValue(actor, out var data)
            ? DurationSeconds(data.FirstDamageTime, data.LastDamageTime)
            : Duration();

    private void MarkEncounterActivity(long timeMs)
    {
        if (timeMs <= 0) return;
        if (StartTime == 0) StartTime = timeMs;
        if (LastEventTime < timeMs) LastEventTime = timeMs;
        if (EndTime < timeMs) EndTime = timeMs;
    }

    private void MarkActorActivity(uint actor, long timeMs)
    {
        if (timeMs <= 0) return;
        if (!DataDic.TryGetValue(actor, out var data)) return;
        if (data.FirstDamageTime == 0) data.FirstDamageTime = timeMs;
        if (data.LastDamageTime < timeMs) data.LastDamageTime = timeMs;
    }

    private static bool IsLocalOrPartyMember(uint actorId)
    {
        if (actorId == 0 || actorId > 0x40000000) return false;

        var localPlayer = DalamudApi.ObjectTable.LocalPlayer;
        if (localPlayer != null && localPlayer.EntityId == actorId) return true;

        foreach (var member in DalamudApi.PartyList)
        {
            if (member.EntityId == actorId) return true;
        }

        return false;
    }

    private bool ShouldTrackEvent(uint sourceId)
    {
        if (StartTime != 0) return true;
        if (DalamudApi.Conditions.Any(ConditionFlag.InCombat)) return true;
        return sourceId != 0xE0000000 && IsLocalOrPartyMember(sourceId);
    }

    private void EnsurePlayer(uint objectId)
    {
        if (objectId == 0 || objectId > 0x40000000) return;

        if (!DataDic.ContainsKey(objectId))
        {
            DataDic.Add(objectId, new Data
            {
                Damages = new Dictionary<uint, SkillDamage> { { Total, new SkillDamage() } },
                Speed = 1f,
            });
        }
        else if (DataDic[objectId].Damages.Count == 0)
        {
            DataDic[objectId].Damages = new Dictionary<uint, SkillDamage> { { Total, new SkillDamage() } };
        }
        else if (!DataDic[objectId].Damages.ContainsKey(Total))
        {
            DataDic[objectId].Damages[Total] = new SkillDamage();
        }

        if (!Name.TryGetValue(objectId, out var currentName) || string.IsNullOrWhiteSpace(currentName))
        {
            var actor = DalamudApi.ObjectTable.FirstOrDefault(x =>
                x.EntityId == objectId && x.ObjectKind == ObjectKind.Player);
            if (actor != default && !string.IsNullOrWhiteSpace(actor.Name.TextValue))
            {
                Name[objectId] = actor.Name.TextValue;
            }
            else
            {
                foreach (var member in DalamudApi.PartyList)
                {
                    if (member.EntityId != objectId) continue;
                    var memberName = member.Name.TextValue;
                    if (!string.IsNullOrWhiteSpace(memberName))
                        Name[objectId] = memberName;
                    break;
                }
            }
        }

        var data = DataDic[objectId];
        if (data.JobId != 0) return;

        uint jobId = 0;
        var character = DalamudApi.ObjectTable.FirstOrDefault(x =>
            x.EntityId == objectId && x.ObjectKind == ObjectKind.Player);
        if (character != default)
        {
            jobId = ((ICharacter)character).ClassJob.RowId;
        }
        else
        {
            foreach (var member in DalamudApi.PartyList)
            {
                if (member.EntityId != objectId) continue;
                jobId = member.ClassJob.RowId;
                break;
            }
        }

        if (jobId == 0) return;

        data.JobId = jobId;
        data.PotSkill = GetBaseSkill(jobId);
    }

    public void AddSS(uint objectId, float casttime, uint actionId)
    {
        var muti = actionId switch
        {
            7 => 1,
            8 => 1,
            3577 => 2.8f / casttime,
            24315 => 1,
            _ => 1.5f / casttime
        };
        if (DataDic.ContainsKey(objectId) &&
            (DataDic[objectId].Speed > muti || DataDic[objectId].Speed == 1))
            DataDic[objectId].Speed = muti;
    }

    public enum EventKind
    {
        Damage = 3,
        Death = 6,
    }

    public void AddEvent(EventKind eventKind, uint from, uint target, uint id, long damage, byte dc = 0, bool countHit = true, long eventTimeMs = 0)
    {
        if (eventTimeMs <= 0) eventTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (!ShouldTrackEvent(from)) return;

        if (from > 0x40000000 && from != 0xE0000000 || from == 0x0)
        {
            DalamudApi.Log.Error($"Unknown Id {from:X}");
            return;
        }

        DalamudApi.Log.Verbose($"AddEvent:{eventKind}:{from:X}:{target:X}:{id}:{damage}");

        if (from != 0xE0000000) EnsurePlayer(from);

        //死亡
        if (eventKind == EventKind.Death)
        {
            if (!DataDic.TryGetValue(from, out var data)) return;
            data.Death++;
            return;
        }

        //DOT 伤害
        if (from == 0xE0000000 && eventKind == EventKind.Damage)
        {
            MarkEncounterActivity(eventTimeMs);
            TotalDotDamage += damage;
            if (!CheckTargetDot(target))
            {
                CalcDot();
                return;
            }

            if (id != 0 && DotPot().ContainsKey(id))
                ActiveDots.RemoveAll(dot => dot.BuffId != id);

            foreach (var dot in ActiveDots)
            {
                if (dot.Source > 0x40000000) dot.Source = ResolveOwner(dot.Source);
                if (dot.Source > 0x40000000) continue;
                EnsurePlayer(dot.Source);
                if (!DataDic.ContainsKey(dot.Source)) continue;
                MarkActorActivity(dot.Source, eventTimeMs);
                
                var active = DotToActive(dot);
                if (PlayerDotPotency.ContainsKey(active))
                    PlayerDotPotency[active] += DotPot()[dot.BuffId];
                else
                    PlayerDotPotency.Add(active,DotPot()[dot.BuffId]);
            }

            CalcDot();
        }

        //伤害
        if (from != 0xE0000000 && eventKind == EventKind.Damage)
        {
            MarkEncounterActivity(eventTimeMs);

            if (ActionSheet != null && ActionSheet.TryGetRow(id, out var actionRow) && actionRow.PrimaryCostType == 11) //LimitBreak
            {
                if (LimitBreak.ContainsKey(id)) LimitBreak[id] += damage;
                else LimitBreak.Add(id, damage);
            }

            MarkActorActivity(from, eventTimeMs);
            if (SkillPot().TryGetValue(id, out var pot)) //基线技能
            {
                if (DataDic[from].PotSkill == id)
                {
                    DataDic[from].SkillPotency += pot * GetJobPotencyMulti(DataDic[from].JobId);
                }
                else if (id > 10)
                {
                    DataDic[from].PotSkill = id;
                    DataDic[from].SkillPotency = pot * GetJobPotencyMulti(DataDic[from].JobId);
                }
            }

            if (DataDic[from].Damages.ContainsKey(id))
            {
                DataDic[from].Damages[id].AddDamage(damage);
            }
            else
            {
                DataDic[from].Damages.Add(id, new SkillDamage(damage));
            }

            if (DataDic[from].MaxDamage < damage)
            {
                DataDic[from].MaxDamage = (uint)damage;
                DataDic[from].MaxDamageSkill = id;
            }

            if (countHit)
            {
                DataDic[from].Damages[id].AddDC(dc);
            }
            DataDic[from].Damages[Total].AddDamage(damage);
            if (countHit)
            {
                DataDic[from].Damages[Total].AddDC(dc);
            }
        }
    }

    public void AddDotDamage(uint from, long damage, byte dc = 0, long eventTimeMs = 0)
    {
        if (eventTimeMs <= 0) eventTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!ShouldTrackEvent(from)) return;
        if (from > 0x40000000 || from == 0x0) return;

        MarkEncounterActivity(eventTimeMs);

        EnsurePlayer(from);
        if (!DataDic.ContainsKey(from)) return;

        MarkActorActivity(from, eventTimeMs);
        DataDic[from].Damages[Total].AddDamage(damage);
    }

    public void AddDotTick(uint from, long damage)
    {
        if (from > 0x40000000 || from == 0x0) return;
        if (DotDamageByActor.ContainsKey(from))
            DotDamageByActor[from] += damage;
        else
            DotDamageByActor.Add(from, damage);
    }

    private void CalcDot()
    {
        TotalDotSim = 0;

        // ?? DPP ?????????:?? DoT ???????
        float fallbackDpp = 1f;
        if (PlayerDotPotency.Count > 0)
        {
            var known = new List<float>();
            foreach (var (active, _) in PlayerDotPotency)
            {
                var source = (uint)(active & 0xFFFFFFFF);
                var dpp = DPP(source);
                if (dpp > 0) known.Add(dpp);
            }

            if (known.Count > 0)
                fallbackDpp = known.Average();
        }

        foreach (var (active, potency) in PlayerDotPotency)
        {
            var source = (uint)(active & 0xFFFFFFFF);
            var dpp = DPP(source);
            if (dpp <= 0) dpp = fallbackDpp;

            var dmg = dpp * potency;
            TotalDotSim += dmg;
            if (!DotDmgList.TryAdd(active, dmg)) DotDmgList[active] = dmg;
        }

        if (TotalDotSim <= 0)
        {
            DotDmgList.Clear();
            return;
        }

        foreach (var (active, damage) in DotDmgList)
        {
            DotDmgList[active] = damage / TotalDotSim * TotalDotDamage;
        }

        var dic = from entry in DotDmgList orderby entry.Value descending select entry;
        DotDmgList = dic.ToDictionary(x=> x.Key,x => x.Value);
    }

    private static long DotToActive(Dot dot)
    {
        return ((long) dot.BuffId << 32) + dot.Source;
    }

    private static ulong DotCacheKey(uint targetId, uint buffId)
        => ((ulong)buffId << 32) | targetId;

    private static ulong DotBuffCacheKey(uint targetId, uint sourceId)
        => ((ulong)sourceId << 32) | targetId;

    public bool TryGetCachedDotSource(uint targetId, uint buffId, out uint source)
    {
        source = 0;
        if (targetId == 0 || buffId == 0) return false;
        return DotSourceCache.TryGetValue(DotCacheKey(targetId, buffId), out source) && source is > 0 and <= 0x40000000;
    }

    public void RememberDotSource(uint targetId, uint buffId, uint source)
    {
        if (targetId == 0 || buffId == 0) return;
        if (source == 0 || source > 0x40000000) return;
        DotSourceCache[DotCacheKey(targetId, buffId)] = source;
    }

    public bool TryGetCachedDotBuff(uint targetId, uint sourceId, out uint buffId)
    {
        buffId = 0;
        if (targetId == 0 || sourceId == 0) return false;
        if (sourceId > 0x40000000) return false;
        return DotBuffCache.TryGetValue(DotBuffCacheKey(targetId, sourceId), out buffId) && buffId != 0;
    }

    public void RememberDotBuff(uint targetId, uint sourceId, uint buffId)
    {
        if (targetId == 0 || sourceId == 0 || buffId == 0) return;
        if (sourceId > 0x40000000) return;
        DotBuffCache[DotBuffCacheKey(targetId, sourceId)] = buffId;
    }

    public float DPP(uint actor)
    {
        if (actor == 0 || actor > 0x40000000) return 0;
        if (!DataDic.TryGetValue(actor, out var data)) return 0;

        long result = 1;
        if (data.Damages.TryGetValue(data.PotSkill, out var dmg)) result = dmg.Damage;
        if (result <= 0 || data.SkillPotency <= 0) return 0;
        return result * data.Speed / data.SkillPotency;
    }

    private bool CheckTargetDot(uint id)
    {
        ActiveDots.Clear();
        var target = DalamudApi.ObjectTable.SearchById(id);
        if (target == null || target.ObjectKind != ObjectKind.BattleNpc)
        {
            DalamudApi.Log.Error($"Dot target {id:X} is not BattleNpc");
            return false;
        }

        var npc = (IBattleNpc) target;
        foreach (var status in npc.StatusList)
        {
            DalamudApi.Log.Verbose($"Check Dot on {id:X}:{status.StatusId}:{status.SourceId}-{ResolveOwner(status.SourceId)}");
            if (DotPot().ContainsKey(status.StatusId))
            {
                var source = status.SourceId;
                if (status.SourceId > 0x40000000) source = ResolveOwner(source);
                if (source > 0x40000000) continue;
                ActiveDots.Add(new Dot()
                    {BuffId = status.StatusId, Source = source});
            }
        }

        return true;
    }

    //public static void SearchForPet()
    //{
    //    Pet.Clear();
    //    foreach (var obj in DalamudApi.ObjectTable)
    //    {
    //        if (obj == null) continue;
    //        if (obj.ObjectKind != ObjectKind.BattleNpc) continue;
    //        var owner = ((IBattleNpc) obj).OwnerId;
    //        if (owner == 0xE0000000) continue;
    //        if (Pet.ContainsKey(owner))
    //            Pet[owner] = obj.EntityId;
    //        else
    //            Pet.Add(obj.EntityId, owner);
    //        DalamudApi.Log.Verbose($"SearchForPet:{obj.EntityId:X}:{owner:X}");
    //    }
    //}

    public static uint GetOwner(uint id) => DalamudApi.ObjectTable.SearchByEntityId(id)?.OwnerId ?? 0xE000_0000;

    public static void ClearOwnerCache()
    {
        lock (OwnerCacheLock)
        {
            OwnerCache.Clear();
        }
    }

    public static uint ResolveOwner(uint id)
    {
        if (id == 0 || id == 0xE0000000) return id;
        if (id <= 0x40000000) return id;
        lock (OwnerCacheLock)
        {
            if (OwnerCache.TryGetValue(id, out var cached) && cached is > 0 and <= 0x40000000) return cached;
        }

        var owner = GetOwner(id);
        if (owner is > 0 and <= 0x40000000)
        {
            lock (OwnerCacheLock)
            {
                OwnerCache[id] = owner;
            }
        }
        return owner;
    }

    public bool TryResolveDotSource(uint targetId, uint buffId, out uint source)
    {
        source = 0;
        if (buffId == 0) return false;

        var target = DalamudApi.ObjectTable.SearchById(targetId);
        if (target == null || target.ObjectKind != ObjectKind.BattleNpc) return false;

        uint matchedSource = 0;
        foreach (var status in ((IBattleNpc)target).StatusList)
        {
            if (status.StatusId != buffId) continue;
            var resolved = ResolveOwner(status.SourceId);
            if (resolved is 0 or > 0x40000000) continue;
            if (matchedSource != 0 && matchedSource != resolved) return false;
            matchedSource = resolved;
        }

        if (matchedSource == 0) return false;
        source = matchedSource;
        return true;
    }

    public bool TryResolveDotBuff(uint targetId, uint sourceId, out uint buffId)
    {
        buffId = 0;
        if (sourceId == 0 || sourceId > 0x40000000) return false;

        var target = DalamudApi.ObjectTable.SearchById(targetId);
        if (target == null || target.ObjectKind != ObjectKind.BattleNpc) return false;

        uint matchedBuff = 0;
        var dotPot = DotPot();
        foreach (var status in ((IBattleNpc)target).StatusList)
        {
            if (!dotPot.ContainsKey(status.StatusId)) continue;
            var resolved = ResolveOwner(status.SourceId);
            if (resolved != sourceId) continue;
            if (matchedBuff != 0 && matchedBuff != status.StatusId) return false;
            matchedBuff = status.StatusId;
        }

        if (matchedBuff == 0) return false;
        buffId = matchedBuff;
        return true;
    }

    public bool TryResolveDotBuffFromTarget(uint targetId, out uint buffId)
    {
        buffId = 0;

        var target = DalamudApi.ObjectTable.SearchById(targetId);
        if (target == null || target.ObjectKind != ObjectKind.BattleNpc) return false;

        uint matchedBuff = 0;
        var dotPot = DotPot();
        foreach (var status in ((IBattleNpc)target).StatusList)
        {
            if (!dotPot.ContainsKey(status.StatusId)) continue;
            var resolved = ResolveOwner(status.SourceId);
            if (!IsLocalOrPartyMember(resolved)) continue;
            if (matchedBuff != 0 && matchedBuff != status.StatusId) return false;
            matchedBuff = status.StatusId;
        }

        if (matchedBuff == 0) return false;
        buffId = matchedBuff;
        return true;
    }

    public bool TryResolveDotBuffByDamageWithoutSource(uint targetId, long damage, out uint buffId)
    {
        buffId = 0;
        if (damage <= 0) return false;

        var target = DalamudApi.ObjectTable.SearchById(targetId);
        if (target == null || target.ObjectKind != ObjectKind.BattleNpc) return false;

        var dotPot = DotPot();
        var pairs = new HashSet<ulong>();
        var candidates = new List<(uint Source, uint Buff)>();
        foreach (var status in ((IBattleNpc)target).StatusList)
        {
            if (!dotPot.ContainsKey(status.StatusId)) continue;
            var resolved = ResolveOwner(status.SourceId);
            if (!IsLocalOrPartyMember(resolved)) continue;

            var key = ((ulong)resolved << 32) | status.StatusId;
            if (!pairs.Add(key)) continue;
            candidates.Add((resolved, status.StatusId));
        }

        if (candidates.Count == 0) return false;

        uint uniqueBuff = 0;
        foreach (var (_, buff) in candidates)
        {
            if (uniqueBuff == 0)
            {
                uniqueBuff = buff;
            }
            else if (uniqueBuff != buff)
            {
                uniqueBuff = 0;
                break;
            }
        }

        if (uniqueBuff != 0)
        {
            buffId = uniqueBuff;
            return true;
        }

        var knownDpp = new List<float>();
        foreach (var (src, _) in candidates)
        {
            EnsurePlayer(src);
            var dpp = DPP(src);
            if (dpp > 0) knownDpp.Add(dpp);
        }

        var fallbackDpp = knownDpp.Count > 0 ? knownDpp.Average() : 1f;

        var bestError = float.PositiveInfinity;
        var secondError = float.PositiveInfinity;
        uint bestBuff = 0;
        foreach (var (src, buff) in candidates)
        {
            var dpp = DPP(src);
            if (dpp <= 0) dpp = fallbackDpp;
            if (dpp <= 0) continue;

            var basePred = dpp * dotPot[buff];
            var err = BestDotTickRelativeError(basePred, damage);
            if (err < bestError)
            {
                secondError = bestError;
                bestError = err;
                bestBuff = buff;
            }
            else if (err < secondError)
            {
                secondError = err;
            }
        }

        const float maxError = 0.18f;
        const float minSeparation = 0.05f;
        if (bestBuff == 0 || bestError > maxError || secondError - bestError < minSeparation) return false;

        buffId = bestBuff;
        return true;
    }

    public bool TryResolveDotBuffByDamage(uint targetId, uint sourceId, long damage, out uint buffId)
    {
        buffId = 0;
        if (sourceId == 0 || sourceId > 0x40000000) return false;
        if (damage <= 0) return false;

        EnsurePlayer(sourceId);
        var dpp = DPP(sourceId);
        if (dpp <= 0) return false;

        var target = DalamudApi.ObjectTable.SearchById(targetId);
        if (target == null || target.ObjectKind != ObjectKind.BattleNpc) return false;

        var dotPot = DotPot();
        var candidates = new HashSet<uint>();
        foreach (var status in ((IBattleNpc)target).StatusList)
        {
            if (!dotPot.ContainsKey(status.StatusId)) continue;
            var resolved = ResolveOwner(status.SourceId);
            if (resolved != sourceId) continue;
            candidates.Add(status.StatusId);
        }

        if (candidates.Count == 0) return false;
        if (candidates.Count == 1)
        {
            buffId = candidates.First();
            return true;
        }

        var bestError = float.PositiveInfinity;
        var secondError = float.PositiveInfinity;
        uint bestId = 0;
        foreach (var statusId in candidates)
        {
            var basePred = dpp * dotPot[statusId];
            var err = BestDotTickRelativeError(basePred, damage);
            if (err < bestError)
            {
                secondError = bestError;
                bestError = err;
                bestId = statusId;
            }
            else if (err < secondError)
            {
                secondError = err;
            }
        }

        const float maxError = 0.18f;
        const float minSeparation = 0.05f;
        if (bestId == 0 || bestError > maxError || secondError - bestError < minSeparation) return false;

        buffId = bestId;
        return true;
    }

    public bool TryResolveDotPairByDamage(uint targetId, long damage, out uint sourceId, out uint buffId)
    {
        sourceId = 0;
        buffId = 0;
        if (damage <= 0) return false;

        var target = DalamudApi.ObjectTable.SearchById(targetId);
        if (target == null || target.ObjectKind != ObjectKind.BattleNpc) return false;

        var dotPot = DotPot();
        var pairs = new HashSet<ulong>();
        var candidates = new List<(uint Source, uint Buff)>();
        foreach (var status in ((IBattleNpc)target).StatusList)
        {
            if (!dotPot.ContainsKey(status.StatusId)) continue;
            var resolved = ResolveOwner(status.SourceId);
            if (!IsLocalOrPartyMember(resolved)) continue;

            var key = ((ulong)resolved << 32) | status.StatusId;
            if (!pairs.Add(key)) continue;
            candidates.Add((resolved, status.StatusId));
        }

        if (candidates.Count == 0) return false;

        var knownDpp = new List<float>();
        foreach (var (src, _) in candidates)
        {
            EnsurePlayer(src);
            var dpp = DPP(src);
            if (dpp > 0) knownDpp.Add(dpp);
        }

        var fallbackDpp = knownDpp.Count > 0 ? knownDpp.Average() : 1f;

        var bestError = float.PositiveInfinity;
        var secondError = float.PositiveInfinity;
        uint bestSource = 0;
        uint bestBuff = 0;

        foreach (var (src, buff) in candidates)
        {
            var dpp = DPP(src);
            if (dpp <= 0) dpp = fallbackDpp;
            if (dpp <= 0) continue;

            var basePred = dpp * dotPot[buff];
            var err = BestDotTickRelativeError(basePred, damage);
            if (err < bestError)
            {
                secondError = bestError;
                bestError = err;
                bestSource = src;
                bestBuff = buff;
            }
            else if (err < secondError)
            {
                secondError = err;
            }
        }

        const float maxError = 0.18f;
        const float minSeparation = 0.05f;
        if (bestSource == 0 || bestBuff == 0 || bestError > maxError || secondError - bestError < minSeparation)
            return false;

        sourceId = bestSource;
        buffId = bestBuff;
        return true;
    }

    private static float BestDotTickRelativeError(float basePrediction, long observedDamage)
    {
        if (basePrediction <= 0 || observedDamage <= 0) return float.PositiveInfinity;

        // ?? crit/dh ???????????,????????????????
        ReadOnlySpan<float> multipliers = stackalloc float[] { 1f, 1.25f, 1.4f, 1.6f, 1.75f, 2f };
        var observed = (float)observedDamage;

        var best = float.PositiveInfinity;
        foreach (var m in multipliers)
        {
            var expected = basePrediction * m;
            if (expected <= 0) continue;
            var err = Math.Abs(observed - expected) / expected;
            if (err < best) best = err;
        }

        return best;
    }

    private Dictionary<uint, uint> DotPot()
    {
        if (Level >= 94) return Potency.DotPot;
            else return Potency.DotPot94;
    }

    private Dictionary<uint, float> SkillPot()
    {
        if (Level >= 94) return Potency.SkillPot;
            else return Potency.SkillPot94;
    }


}
