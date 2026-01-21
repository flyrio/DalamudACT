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
    private readonly Queue<DotRecentEvent> recent = new();
    private readonly Dictionary<ulong, DedupEntry> dedupLastByKey = new();
    private readonly Queue<(ulong Key, long TimeMs, DotTickChannel Channel, uint RawBuffId, uint ResolvedBuffId)> dedupWindow = new();
    private readonly Dictionary<ulong, long> sameChannelDedupLastByKey = new();
    private readonly Queue<(ulong Key, long TimeMs)> sameChannelDedupWindow = new();

    private readonly Configuration config;

    private readonly struct DedupEntry
    {
        public readonly long TimeMs;
        public readonly DotTickChannel Channel;
        public readonly uint RawBuffId;
        public readonly uint ResolvedBuffId;

        public DedupEntry(long timeMs, DotTickChannel channel, uint rawBuffId, uint resolvedBuffId)
        {
            TimeMs = timeMs;
            Channel = channel;
            RawBuffId = rawBuffId;
            ResolvedBuffId = resolvedBuffId;
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

    private const int RecentBufferCapacity = 64;

    // 跨通道去重：避免双通道导致伤害翻倍；不应过大以免误杀同一 tick 内的合法事件。
    private const int DedupWindowMs = 800;

    // 同通道去重：仅用于过滤“完全重复”的 DoT tick（少量环境/版本会出现）；窗口要足够小以避免误删。
    private const int SameChannelDedupWindowMs = 200;

    public DotEventCapture(Configuration config)
    {
        this.config = config;
    }

    public void ResetTransientCaches()
    {
        lock (gate)
        {
            queue.Clear();
            recent.Clear();
            dedupLastByKey.Clear();
            dedupWindow.Clear();
            sameChannelDedupLastByKey.Clear();
            sameChannelDedupWindow.Clear();
        }
    }

    internal enum DotDropReason : byte
    {
        None = 0,
        InvalidTarget = 1,
        SameChannelDuplicate = 2,
        CrossChannelDuplicate = 3,
    }

    internal enum DotAttributionKind : byte
    {
        None = 0,
        UnknownSource = 1,
        Action = 2,
        Status = 3,
        TotalOnly = 4,
    }

    internal readonly struct DotRecentEvent
    {
        public readonly long TimeMs;
        public readonly DotTickChannel Channel;
        public readonly uint OriginalSourceId;
        public readonly uint OriginalTargetId;
        public readonly uint OriginalBuffId;
        public readonly uint Damage;
        public readonly uint ResolvedSourceId;
        public readonly uint ResolvedBuffId;
        public readonly DotDropReason DropReason;
        public readonly DotAttributionKind Attribution;
        public readonly uint AttributionId;

        public DotRecentEvent(
            long timeMs,
            DotTickChannel channel,
            uint originalSourceId,
            uint originalTargetId,
            uint originalBuffId,
            uint damage,
            uint resolvedSourceId,
            uint resolvedBuffId,
            DotDropReason dropReason,
            DotAttributionKind attribution,
            uint attributionId)
        {
            TimeMs = timeMs;
            Channel = channel;
            OriginalSourceId = originalSourceId;
            OriginalTargetId = originalTargetId;
            OriginalBuffId = originalBuffId;
            Damage = damage;
            ResolvedSourceId = resolvedSourceId;
            ResolvedBuffId = resolvedBuffId;
            DropReason = dropReason;
            Attribution = attribution;
            AttributionId = attributionId;
        }
    }

    internal DotRecentEvent[] GetRecentEventsSnapshot()
    {
        lock (gate)
        {
            return recent.ToArray();
        }
    }

    private void RecordRecent(in DotRecentEvent evt)
    {
        lock (gate)
        {
            if (recent.Count >= RecentBufferCapacity)
                recent.Dequeue();
            recent.Enqueue(evt);
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

        if (batch.Count > 1)
        {
            // 优先处理带 buffId 的事件，便于后续去重保留更“信息完整”的那条。
            batch.Sort((a, b) =>
            {
                var t = a.TimeMs.CompareTo(b.TimeMs);
                if (t != 0) return t;
                var aScore = a.BuffId == 0 ? 1 : 0;
                var bScore = b.BuffId == 0 ? 1 : 0;
                t = aScore.CompareTo(bScore);
                if (t != 0) return t;
                return ((int)a.Channel).CompareTo((int)b.Channel);
            });
        }

        foreach (var evt in batch)
            ProcessOne(battle, evt);
    }

    private void ProcessOne(ACTBattle battle, DotTickEvent evt)
    {
        var originalSourceId = evt.SourceId;
        var originalTargetId = evt.TargetId;
        var originalBuffId = evt.BuffId;

        var targetId = evt.TargetId;
        if (targetId is 0 or 0xE000_0000 or < 0x4000_0000)
        {
            lock (gate)
                droppedInvalid++;
            RecordRecent(new DotRecentEvent(
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                evt.Channel,
                originalSourceId,
                originalTargetId,
                originalBuffId,
                evt.Damage,
                resolvedSourceId: 0,
                resolvedBuffId: 0,
                DotDropReason.InvalidTarget,
                DotAttributionKind.None,
                attributionId: 0));
            return;
        }

        var timeMs = evt.TimeMs <= 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : evt.TimeMs;

        // 字段修复：owner 解析、source/buff 推断。
        var sourceId = evt.SourceId;
        if (sourceId > 0x4000_0000 && sourceId != 0xE000_0000)
        {
            var resolvedOwner = ACTBattle.ResolveOwner(sourceId);
            if (resolvedOwner == 0xE000_0000 && config.EnableActLikeAttribution)
            {
                ACTBattle.WarmOwnerCacheFromObjectTable(timeMs);
                resolvedOwner = ACTBattle.ResolveOwner(sourceId);
            }

            sourceId = resolvedOwner;
        }

        // 防御性处理：部分环境下 sourceId 可能为极小常量（非有效 ActorId），避免将 DoT 错归因到“幽灵实体”。
        // 仅在明显异常时归零，后续可通过状态表/缓存推断来源。
        if (sourceId is > 0 and < 0x0001_0000)
            sourceId = 0;

        var buffId = evt.BuffId;
        var damage = (long)evt.Damage;

        if ((sourceId == 0 || sourceId > 0x4000_0000) && buffId != 0)
        {
            var resolved = false;
            uint resolvedSource = 0;

            if (config.EnableActLikeAttribution)
            {
                resolved = battle.TryResolveDotSource(targetId, buffId, out resolvedSource) ||
                           battle.TryResolveDotSourceByDamage(targetId, buffId, damage, out resolvedSource);
            }
            else
            {
                // 默认优先尝试“目标状态表”解析（更接近 ACT），失败再回退缓存，避免缓存过期导致错归因。
                resolved = battle.TryResolveDotSource(targetId, buffId, out resolvedSource) ||
                           battle.TryResolveDotSourceByDamage(targetId, buffId, damage, out resolvedSource);

                if (!resolved && battle.TryGetCachedDotSource(targetId, buffId, out var cachedSource))
                    sourceId = cachedSource;
            }

            if (resolved && resolvedSource != 0)
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

        // 二次来源推断：当报文缺失 buffId 但后续推断补齐后，若 sourceId 仍未知，则再尝试解析来源。
        // 该分支不按伤害“强行推断来源”，仅在目标状态表能唯一确定来源时才补齐（避免错归因扩大）。
        if (originalBuffId == 0 && (sourceId == 0 || sourceId > 0x4000_0000) && buffId != 0)
        {
            if (battle.TryResolveDotSource(targetId, buffId, out var resolvedSource) && resolvedSource != 0)
            {
                sourceId = resolvedSource;
                lock (gate)
                    inferredSource++;
            }
        }

        // 防御性校验：当 buffId 可确定且状态表能唯一给出来源时，以状态表为准，修正可能错误的 sourceId。
        if (buffId != 0 && sourceId is > 0 and <= 0x4000_0000)
        {
            if (battle.TryResolveDotSource(targetId, buffId, out var statusSource) &&
                statusSource is > 0 and <= 0x4000_0000 &&
                statusSource != sourceId)
            {
                sourceId = statusSource;
                lock (gate)
                    inferredSource++;
            }
        }

        // 同通道去重：过滤完全重复事件（如同一帧重复回调/重复包）。
        if (IsSameChannelDuplicate(timeMs, sourceId, targetId, buffId, evt.Damage, evt.Channel))
        {
            lock (gate)
                dedupDropped++;
            RecordRecent(new DotRecentEvent(
                timeMs,
                evt.Channel,
                originalSourceId,
                originalTargetId,
                originalBuffId,
                evt.Damage,
                sourceId,
                buffId,
                DotDropReason.SameChannelDuplicate,
                DotAttributionKind.None,
                attributionId: 0));
            return;
        }

        // 跨通道去重：仅在不同通道重复时丢弃，避免未知来源/同通道误删。
        // 注意：使用“原始 buffId”而非推断后的 buffId，避免推断补齐后无法识别同 tick 的双事件（buffId=0 与 buffId!=0）。
        if (IsDuplicate(timeMs, sourceId, targetId, evt.Damage, evt.BuffId, buffId, evt.Channel))
        {
            lock (gate)
                dedupDropped++;
            RecordRecent(new DotRecentEvent(
                timeMs,
                evt.Channel,
                originalSourceId,
                originalTargetId,
                originalBuffId,
                evt.Damage,
                sourceId,
                buffId,
                DotDropReason.CrossChannelDuplicate,
                DotAttributionKind.None,
                attributionId: 0));
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
            RecordRecent(new DotRecentEvent(
                timeMs,
                evt.Channel,
                originalSourceId,
                originalTargetId,
                originalBuffId,
                evt.Damage,
                sourceId,
                buffId,
                DotDropReason.None,
                DotAttributionKind.UnknownSource,
                attributionId: buffId));
            return;
        }

        // 归因成功：tick 计入来源，同时尽量产出“技能明细”（actionId 或 statusId）。
        battle.AddDotTick(sourceId, damage);
        if (!config.EnableActLikeAttribution)
            battle.RememberDotSource(targetId, buffId, sourceId);
        battle.RememberDotBuff(targetId, sourceId, buffId);

        if (buffId != 0 && Potency.BuffToAction.TryGetValue(buffId, out var actionId))
        {
            lock (gate)
                attributedToAction++;
            battle.AddEvent(ACTBattle.EventKind.Damage, sourceId, targetId, actionId, damage, countHit: false, eventTimeMs: timeMs);
            RecordRecent(new DotRecentEvent(
                timeMs,
                evt.Channel,
                originalSourceId,
                originalTargetId,
                originalBuffId,
                evt.Damage,
                sourceId,
                buffId,
                DotDropReason.None,
                DotAttributionKind.Action,
                attributionId: actionId));
            return;
        }

        if (config.EnableEnhancedDotCapture && buffId != 0)
        {
            lock (gate)
                attributedToStatus++;
            battle.AddEvent(ACTBattle.EventKind.Damage, sourceId, targetId, buffId, damage, countHit: false, eventTimeMs: timeMs);
            RecordRecent(new DotRecentEvent(
                timeMs,
                evt.Channel,
                originalSourceId,
                originalTargetId,
                originalBuffId,
                evt.Damage,
                sourceId,
                buffId,
                DotDropReason.None,
                DotAttributionKind.Status,
                attributionId: buffId));
            return;
        }

        lock (gate)
            attributedToTotalOnly++;
        battle.AddDotDamage(sourceId, damage, eventTimeMs: timeMs);
        RecordRecent(new DotRecentEvent(
            timeMs,
            evt.Channel,
            originalSourceId,
            originalTargetId,
            originalBuffId,
            evt.Damage,
            sourceId,
            buffId,
            DotDropReason.None,
            DotAttributionKind.TotalOnly,
            attributionId: 0));
    }

    private bool IsDuplicate(long timeMs, uint sourceId, uint targetId, uint damage, uint rawBuffId, uint resolvedBuffId, DotTickChannel channel)
    {
        var key = DedupKey(sourceId, targetId, damage);

        lock (gate)
        {
            while (dedupWindow.Count > 0)
            {
                var (oldKey, oldTime, oldChannel, oldRawBuffId, oldResolvedBuffId) = dedupWindow.Peek();
                if (timeMs - oldTime <= DedupWindowMs) break;
                dedupWindow.Dequeue();
                if (dedupLastByKey.TryGetValue(oldKey, out var stored) &&
                    stored.TimeMs == oldTime &&
                    stored.Channel == oldChannel &&
                    stored.RawBuffId == oldRawBuffId &&
                    stored.ResolvedBuffId == oldResolvedBuffId)
                {
                    dedupLastByKey.Remove(oldKey);
                }
            }

            var isDuplicate = false;
            if (dedupLastByKey.TryGetValue(key, out var last) && timeMs - last.TimeMs <= DedupWindowMs)
            {
                if (last.Channel != channel)
                {
                    // 跨通道去重：优先避免“同 tick 双通道上报”导致伤害翻倍；但若两条事件明确属于不同 buff，则不应误删。
                    var lastCandidate = last.RawBuffId != 0 ? last.RawBuffId : last.ResolvedBuffId;
                    var curCandidate = rawBuffId != 0 ? rawBuffId : resolvedBuffId;

                    // 当两侧都能确定 buff 且不一致时，判定为不同 DoT（避免误删）。其余情况保守视为重复（避免翻倍）。
                    isDuplicate = !(lastCandidate != 0 && curCandidate != 0 && lastCandidate != curCandidate);
                }
                else if ((last.RawBuffId == 0) != (rawBuffId == 0))
                {
                    isDuplicate = true;
                }
            }

            dedupLastByKey[key] = new DedupEntry(timeMs, channel, rawBuffId, resolvedBuffId);
            dedupWindow.Enqueue((key, timeMs, channel, rawBuffId, resolvedBuffId));
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

    private bool IsSameChannelDuplicate(long timeMs, uint sourceId, uint targetId, uint buffId, uint damage, DotTickChannel channel)
    {
        // 仅对“同通道完全重复”做保守去重；以免不同 DoT 在同一 tick 内误判。
        var key = SameChannelDedupKey(sourceId, targetId, buffId, damage, channel);

        lock (gate)
        {
            while (sameChannelDedupWindow.Count > 0)
            {
                var (oldKey, oldTime) = sameChannelDedupWindow.Peek();
                if (timeMs - oldTime <= SameChannelDedupWindowMs) break;
                sameChannelDedupWindow.Dequeue();
                if (sameChannelDedupLastByKey.TryGetValue(oldKey, out var storedTime) && storedTime == oldTime)
                {
                    sameChannelDedupLastByKey.Remove(oldKey);
                }
            }

            if (sameChannelDedupLastByKey.TryGetValue(key, out var lastTime) && timeMs - lastTime <= SameChannelDedupWindowMs)
                return true;

            sameChannelDedupLastByKey[key] = timeMs;
            sameChannelDedupWindow.Enqueue((key, timeMs));
            return false;
        }
    }

    private static ulong SameChannelDedupKey(uint sourceId, uint targetId, uint buffId, uint damage, DotTickChannel channel)
    {
        var key = ((ulong)sourceId << 32) | targetId;
        key ^= ((ulong)buffId << 1) * 0x9E3779B97F4A7C15UL;
        key ^= (ulong)damage * 0xC2B2AE3D27D4EB4FUL;
        key ^= ((ulong)channel + 1) * 0x165667B19E3779F9UL;
        key ^= key >> 33;
        key *= 0x9E3779B97F4A7C15UL;
        key ^= key >> 29;
        return key;
    }
}
