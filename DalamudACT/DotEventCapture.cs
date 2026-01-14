using System;
using System.Collections.Generic;
using DalamudACT.Struct;

namespace DalamudACT;

internal enum DotTickChannel
{
    LegacyActorControlSelf = 1,
    NetworkActorControl = 2,
    NetworkActorControlSelf = 3,
    NetworkActorControlTarget = 4,
}

internal readonly struct DotTickEvent
{
    public readonly long TimeMs;
    public readonly uint SourceId;
    public readonly uint TargetId;
    public readonly uint BuffId;
    public readonly uint Damage;
    public readonly DotTickChannel Channel;

    public DotTickEvent(long timeMs, uint sourceId, uint targetId, uint buffId, uint damage, DotTickChannel channel)
    {
        TimeMs = timeMs;
        SourceId = sourceId;
        TargetId = targetId;
        BuffId = buffId;
        Damage = damage;
        Channel = channel;
    }
}

internal readonly struct DotCaptureStats
{
    public readonly long EnqueuedTotal;
    public readonly long EnqueuedLegacy;
    public readonly long EnqueuedNetwork;
    public readonly long DroppedInvalid;
    public readonly long DedupDropped;
    public readonly long Processed;
    public readonly long UnknownSource;
    public readonly long UnknownBuff;
    public readonly long InferredSource;
    public readonly long InferredBuff;
    public readonly long AttributedToAction;
    public readonly long AttributedToStatus;
    public readonly long AttributedToTotalOnly;

    public DotCaptureStats(
        long enqueuedTotal,
        long enqueuedLegacy,
        long enqueuedNetwork,
        long droppedInvalid,
        long dedupDropped,
        long processed,
        long unknownSource,
        long unknownBuff,
        long inferredSource,
        long inferredBuff,
        long attributedToAction,
        long attributedToStatus,
        long attributedToTotalOnly)
    {
        EnqueuedTotal = enqueuedTotal;
        EnqueuedLegacy = enqueuedLegacy;
        EnqueuedNetwork = enqueuedNetwork;
        DroppedInvalid = droppedInvalid;
        DedupDropped = dedupDropped;
        Processed = processed;
        UnknownSource = unknownSource;
        UnknownBuff = unknownBuff;
        InferredSource = inferredSource;
        InferredBuff = inferredBuff;
        AttributedToAction = attributedToAction;
        AttributedToStatus = attributedToStatus;
        AttributedToTotalOnly = attributedToTotalOnly;
    }
}

internal sealed class DotEventCapture
{
    private readonly object gate = new();
    private readonly Queue<DotTickEvent> queue = new();
    private readonly Dictionary<ulong, DedupEntry> dedupLastByKey = new();
    private readonly Queue<(ulong Key, long TimeMs, DotTickChannel Channel)> dedupWindow = new();

    private readonly Configuration config;

    private readonly struct DedupEntry
    {
        public readonly long TimeMs;
        public readonly DotTickChannel Channel;

        public DedupEntry(long timeMs, DotTickChannel channel)
        {
            TimeMs = timeMs;
            Channel = channel;
        }
    }

    private long enqueuedTotal;
    private long enqueuedLegacy;
    private long enqueuedNetwork;
    private long droppedInvalid;
    private long dedupDropped;
    private long processed;
    private long unknownSource;
    private long unknownBuff;
    private long inferredSource;
    private long inferredBuff;
    private long attributedToAction;
    private long attributedToStatus;
    private long attributedToTotalOnly;

    // 仅用于“跨通道去重”（避免双通道导致伤害翻倍）；不应过大以免误杀同一 tick 内的合法事件。
    private const int DedupWindowMs = 800;

    public DotEventCapture(Configuration config)
    {
        this.config = config;
    }

    public void ResetTransientCaches()
    {
        lock (gate)
        {
            queue.Clear();
            dedupLastByKey.Clear();
            dedupWindow.Clear();
        }
    }

    public DotCaptureStats GetStatsSnapshot()
    {
        lock (gate)
        {
            return new DotCaptureStats(
                enqueuedTotal,
                enqueuedLegacy,
                enqueuedNetwork,
                droppedInvalid,
                dedupDropped,
                processed,
                unknownSource,
                unknownBuff,
                inferredSource,
                inferredBuff,
                attributedToAction,
                attributedToStatus,
                attributedToTotalOnly);
        }
    }

    public void Enqueue(in DotTickEvent evt)
    {
        lock (gate)
        {
            enqueuedTotal++;
            if (evt.Channel == DotTickChannel.LegacyActorControlSelf)
                enqueuedLegacy++;
            else
                enqueuedNetwork++;

            queue.Enqueue(evt);
        }
    }

    public void FlushInto(ACTBattle battle)
    {
        List<DotTickEvent> batch;
        lock (gate)
        {
            if (queue.Count == 0) return;
            batch = new List<DotTickEvent>(queue.Count);
            while (queue.Count > 0)
                batch.Add(queue.Dequeue());
        }

        foreach (var evt in batch)
            ProcessOne(battle, evt);
    }

    private void ProcessOne(ACTBattle battle, DotTickEvent evt)
    {
        var targetId = evt.TargetId;
        if (targetId is 0 or < 0x4000_0000)
        {
            lock (gate)
                droppedInvalid++;
            return;
        }

        var timeMs = evt.TimeMs <= 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : evt.TimeMs;

        // 字段修复：owner 解析、source/buff 推断。
        var sourceId = evt.SourceId;
        if (sourceId > 0x4000_0000 && sourceId != 0xE000_0000)
            sourceId = ACTBattle.ResolveOwner(sourceId);

        var buffId = evt.BuffId;
        var damage = (long)evt.Damage;

        if ((sourceId == 0 || sourceId > 0x4000_0000) && buffId != 0)
        {
            if (!battle.TryGetCachedDotSource(targetId, buffId, out sourceId) &&
                battle.TryResolveDotSource(targetId, buffId, out var resolvedSource))
            {
                sourceId = resolvedSource;
                lock (gate)
                    inferredSource++;
            }
        }

        if (config.EnableEnhancedDotCapture && buffId == 0 && sourceId is > 0 and <= 0x4000_0000)
        {
            if (!battle.TryGetCachedDotBuff(targetId, sourceId, out buffId) &&
                (battle.TryResolveDotBuff(targetId, sourceId, out var resolvedBuff) ||
                 battle.TryResolveDotBuffByDamage(targetId, sourceId, damage, out resolvedBuff)))
            {
                buffId = resolvedBuff;
                battle.RememberDotBuff(targetId, sourceId, buffId);
                lock (gate)
                    inferredBuff++;
            }
        }

        if (config.EnableEnhancedDotCapture && buffId == 0 && (sourceId == 0 || sourceId > 0x4000_0000))
        {
            if (battle.TryResolveDotBuffFromTarget(targetId, out var resolvedBuff) ||
                battle.TryResolveDotBuffByDamageWithoutSource(targetId, damage, out resolvedBuff))
            {
                buffId = resolvedBuff;
                lock (gate)
                    inferredBuff++;
            }
        }

        // 跨通道去重：仅在不同通道重复时丢弃，避免未知来源/同通道误删。
        if (IsDuplicate(timeMs, sourceId, targetId, evt.Damage, evt.Channel))
        {
            lock (gate)
                dedupDropped++;
            return;
        }

        lock (gate)
        {
            processed++;
            if (buffId == 0) unknownBuff++;
        }

        if (config.EnableDotDiagnostics)
            DalamudApi.Log.Verbose($"[DalamudACT] DotTick({evt.Channel}) t={timeMs} src={sourceId:X} tgt={targetId:X} buff={buffId} dmg={damage}");

        // 归因失败：走未知来源 DoT 口径（TotalDotDamage + 目标状态扫描模拟）。
        if (sourceId == 0 || sourceId > 0x4000_0000)
        {
            lock (gate)
                unknownSource++;

            battle.AddEvent(ACTBattle.EventKind.Damage, 0xE000_0000, targetId, buffId, damage, countHit: false, eventTimeMs: timeMs);
            return;
        }

        // 归因成功：tick 计入来源，同时尽量产出“技能明细”（actionId 或 statusId）。
        battle.AddDotTick(sourceId, damage);
        battle.RememberDotSource(targetId, buffId, sourceId);
        battle.RememberDotBuff(targetId, sourceId, buffId);

        if (buffId != 0 && Potency.BuffToAction.TryGetValue(buffId, out var actionId))
        {
            lock (gate)
                attributedToAction++;
            battle.AddEvent(ACTBattle.EventKind.Damage, sourceId, targetId, actionId, damage, countHit: false, eventTimeMs: timeMs);
            return;
        }

        if (config.EnableEnhancedDotCapture && buffId != 0)
        {
            lock (gate)
                attributedToStatus++;
            battle.AddEvent(ACTBattle.EventKind.Damage, sourceId, targetId, buffId, damage, countHit: false, eventTimeMs: timeMs);
            return;
        }

        lock (gate)
            attributedToTotalOnly++;
        battle.AddDotDamage(sourceId, damage, eventTimeMs: timeMs);
    }

    private bool IsDuplicate(long timeMs, uint sourceId, uint targetId, uint damage, DotTickChannel channel)
    {
        var key = DedupKey(sourceId, targetId, damage);

        lock (gate)
        {
            while (dedupWindow.Count > 0)
            {
                var (oldKey, oldTime, oldChannel) = dedupWindow.Peek();
                if (timeMs - oldTime <= DedupWindowMs) break;
                dedupWindow.Dequeue();
                if (dedupLastByKey.TryGetValue(oldKey, out var stored) &&
                    stored.TimeMs == oldTime &&
                    stored.Channel == oldChannel)
                {
                    dedupLastByKey.Remove(oldKey);
                }
            }

            var isDuplicate = false;
            if (dedupLastByKey.TryGetValue(key, out var last) && timeMs - last.TimeMs <= DedupWindowMs)
            {
                if (last.Channel != channel)
                    isDuplicate = true;
            }

            dedupLastByKey[key] = new DedupEntry(timeMs, channel);
            dedupWindow.Enqueue((key, timeMs, channel));
            return isDuplicate;
        }
    }

    private static ulong DedupKey(uint sourceId, uint targetId, uint damage)
    {
        // 使用 (source,target,damage) 作为“去重”Key：忽略 buffId，避免增强链路补齐 buffId 后无法匹配 legacy。
        // 冲突概率较低（同一 tick 内同源同目标同伤害的合法事件非常少），且窗口足够小。
        var key = ((ulong)sourceId << 32) | targetId;
        key ^= (ulong)damage * 0x9E3779B97F4A7C15UL;
        key ^= key >> 33;
        key *= 0xC2B2AE3D27D4EB4FUL;
        key ^= key >> 29;
        return key;
    }
}
