using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;
using DalamudACT.Struct;
using Lumina.Excel.Sheets;

namespace DalamudACT;

internal class PluginUI : IDisposable
{
    private static PluginUI? instance;

    private static Configuration config = null!;

    private static ACT _plugin = null!;

    public ConfigWindow configWindow;
    public CardsWindow cardsWindow;
    public WindowSystem WindowSystem = new("伤害统计");

    private static string JobNameCn(uint jobId)
        => jobId switch
        {
            1 => "剑术师",
            2 => "格斗家",
            3 => "斧术师",
            4 => "枪术师",
            5 => "弓箭手",
            6 => "幻术师",
            7 => "咒术师",
            8 => "刻木匠",
            9 => "锻铁匠",
            10 => "铸甲匠",
            11 => "雕金匠",
            12 => "制革匠",
            13 => "裁衣匠",
            14 => "炼金术士",
            15 => "烹调师",
            16 => "采矿工",
            17 => "园艺工",
            18 => "捕鱼人",
            19 => "骑士",
            20 => "武僧",
            21 => "战士",
            22 => "龙骑士",
            23 => "吟游诗人",
            24 => "白魔法师",
            25 => "黑魔法师",
            26 => "秘术师",
            27 => "召唤师",
            28 => "学者",
            29 => "双剑师",
            30 => "忍者",
            31 => "机工士",
            32 => "暗黑骑士",
            33 => "占星术士",
            34 => "武士",
            35 => "赤魔法师",
            36 => "青魔法师",
            37 => "绝枪战士",
            38 => "舞者",
            39 => "钐镰客",
            40 => "贤者",
            41 => "蝰蛇剑士",
            42 => "绘灵法师",
            _ => ((Job)jobId).ToString()
        };

    private static string FormatRate(float rate)
        => rate < 0 ? "-" : rate.ToString("P0");

    private sealed class SpringFloat
    {
        public float Value;
        public float Velocity;
        public double LastTime;
    }

    private static float SpringTo(Dictionary<uint, SpringFloat> springs, uint key, float target)
    {
        var now = ImGui.GetTime();
        if (!springs.TryGetValue(key, out var state))
        {
            springs[key] = new SpringFloat { Value = target, Velocity = 0f, LastTime = now };
            return target;
        }

        var dt = (float)(now - state.LastTime);
        state.LastTime = now;
        dt = Math.Clamp(dt, 0f, 0.05f);

        // Slightly under-damped spring for a more "bouncy" DPS bar.
        const float stiffness = 160f;
        const float damping = 12f;

        var acceleration = stiffness * (target - state.Value) - damping * state.Velocity;
        state.Velocity += acceleration * dt;
        state.Value += state.Velocity * dt;

        if (MathF.Abs(target - state.Value) < 0.0005f && MathF.Abs(state.Velocity) < 0.0005f)
        {
            state.Value = target;
            state.Velocity = 0f;
        }

        return state.Value;
    }

    public PluginUI(ACT p)
    {
        instance = this;
        _plugin = p;
        config = p.Configuration;

        configWindow = new ConfigWindow(_plugin);
        cardsWindow = new CardsWindow(_plugin);

        WindowSystem.AddWindow(configWindow);
        WindowSystem.AddWindow(cardsWindow);

        cardsWindow.IsOpen = config.CardsEnabled;
    }

    public void Dispose()
    {
        instance = null;

        WindowSystem.RemoveAllWindows();
        configWindow.Dispose();
        cardsWindow?.Dispose();
    }

    public class ConfigWindow : Window, IDisposable
    {
        public ConfigWindow(ACT plugin) : base("伤害统计 - 设置", ImGuiWindowFlags.AlwaysAutoResize, false)
        {
        }

        public override void Draw()
        {
            BgAlpha = 1f;
            try
            {
                var changed = false;
                if (ImGui.CollapsingHeader("窗口", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    changed |= ImGui.Checkbox("鼠标穿透（按住 Alt 临时操作）", ref config.ClickThrough);
                    ImGui.TextDisabled("提示：开启穿透后名片不响应点击，按住 Alt 可临时操作。");
                }

                if (ImGui.CollapsingHeader("显示", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    changed |= ImGui.Checkbox("隐藏玩家姓名（用职业名代替）", ref config.HideName);
                    changed |= ImGui.SliderInt("仅显示前 N 名（0=全部）", ref config.TopN, 0, 24);
                    changed |= ImGui.Checkbox("显示直击/暴击/直爆列", ref config.ShowRates);
                    changed |= ImGui.Checkbox("高亮自己", ref config.HighlightSelf);

                    var cardsEnabled = config.CardsEnabled;
                    if (ImGui.Checkbox("显示独立名片", ref cardsEnabled))
                    {
                        config.CardsEnabled = cardsEnabled;
                        if (instance?.cardsWindow != null)
                        {
                            instance.cardsWindow.IsOpen = cardsEnabled;
                            if (cardsEnabled) instance.cardsWindow.ResetPositioning();
                        }

                        changed = true;
                    }
                    else
                    {
                        if (instance?.cardsWindow != null) instance.cardsWindow.IsOpen = config.CardsEnabled;
                    }

                    if (config.CardsEnabled)
                    {
                        var layoutModes = new[] { "独立名片列", "独立名片行" };
                        var layoutMode = config.DisplayLayout;
                        if (ImGui.Combo("名片布局", ref layoutMode, layoutModes, layoutModes.Length))
                        {
                            config.DisplayLayout = layoutMode;
                            changed = true;
                        }

                        if (config.DisplayLayout == 0)
                        {
                            changed |= ImGui.SliderFloat("名片缩放", ref config.CardsScale, 0.5f, 2.0f);
                            if (ImGui.IsItemDeactivatedAfterEdit())
                                _plugin.RefreshCardsFont();
                            changed |= ImGui.SliderFloat("名片宽度(列)", ref config.CardColumnWidth, 120f, 800f);
                            changed |= ImGui.SliderFloat("名片高度(列)", ref config.CardColumnHeight, 24f, 200f);
                            changed |= ImGui.SliderFloat("名片间距(列)", ref config.CardColumnSpacing, 0f, 40f);
                            changed |= ImGui.SliderInt("每列名片数", ref config.CardsPerLine, 1, 24);
                        }
                        else
                        {
                            changed |= ImGui.SliderFloat("名片缩放", ref config.CardsScale, 0.5f, 2.0f);
                            if (ImGui.IsItemDeactivatedAfterEdit())
                                _plugin.RefreshCardsFont();
                            changed |= ImGui.SliderFloat("名片宽度(行)", ref config.CardRowWidth, 120f, 800f);
                            changed |= ImGui.SliderFloat("名片高度(行)", ref config.CardRowHeight, 24f, 200f);
                            changed |= ImGui.SliderFloat("名片间距(行)", ref config.CardRowSpacing, 0f, 40f);
                            changed |= ImGui.SliderInt("每行名片数", ref config.CardsPerLine, 1, 24);
                        }
                    }

                    var sortModes = new[] { "按秒伤", "按总伤害", "按姓名" };
                    if (config.CardsEnabled)
                    {
                        var placementMode = config.CardsPlacementMode;
                        if (ImGui.Checkbox("名片摆放模式(脱战预览)", ref placementMode))
                        {
                            config.CardsPlacementMode = placementMode;
                            changed = true;
                        }

                        if (placementMode)
                            ImGui.TextDisabled("提示: 脱战会显示预览名片，可以拖动（穿透开启时按住alt）松开自动保存位置。");
                    }

                    var sortMode = config.SortMode;
                    if (ImGui.Combo("排序方式", ref sortMode, sortModes, sortModes.Length))
                    {
                        config.SortMode = sortMode;
                        changed = true;
                    }
                }

                if (changed) config.Save();

                if (ImGui.CollapsingHeader("调试信息"))
                {
                    unsafe
                    {
                        var ptr = *(nint*)((nint)FFXIVClientStructs.FFXIV.Client.System.Framework.GameWindow.Instance() + 0xA8);
                        ImGui.Text(ptr.ToString());
                        ImGui.Text(Marshal.PtrToStringUTF8(ptr));
                    }

                    ImGui.Text($"区域：{DalamudApi.ClientState.TerritoryType}");
                    ImGui.Text($"ContentMemberType 表行数：{DalamudApi.GameData.GetExcelSheet<ContentMemberType>().Count}");
                }
            }
            finally
            {
            }
        }

        public void Dispose()
        {
        }
    }

    public class CardsWindow : Window, IDisposable
    {
        private bool positionedFromConfig;
        private bool draggingCards;
        private int battleHistoryOffset;
        private readonly Dictionary<uint, SpringFloat> dpsBarSprings = new();

        public CardsWindow(ACT plugin) : base("伤害统计 - 名片列", ImGuiWindowFlags.AlwaysAutoResize, false)
        {
        }

        public void ResetPositioning()
        {
            positionedFromConfig = false;
        }

        public void ResetHistory()
        {
            battleHistoryOffset = 0;
        }

        public void NudgeHistory(int delta)
        {
            battleHistoryOffset = Math.Max(0, battleHistoryOffset + delta);
        }

        public void ClearBattleHistory()
        {
            lock (_plugin.SyncRoot)
            {
                _plugin.Battles.Clear();
                _plugin.Battles.Add(new ACTBattle(0, 0));
            }

            dpsBarSprings.Clear();
            ResetHistory();
        }

        private static string FormatDuration(float seconds)
        {
            var totalSeconds = seconds <= 0 ? 0 : (int)MathF.Round(seconds);
            if (totalSeconds < 60) return $"00:{totalSeconds:00}";
            if (totalSeconds < 3600) return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
            return $"{totalSeconds / 3600:00}:{(totalSeconds % 3600) / 60:00}:{totalSeconds % 60:00}";
        }

        private bool DrawBattleHeader(bool inCombatNow, int battleCount, string? zone, float seconds)
        {
            var clearRequested = false;

            ImGui.AlignTextToFramePadding();

            if (!inCombatNow && battleCount > 1)
            {
                if (ImGui.SmallButton("<<")) battleHistoryOffset = Math.Min(battleHistoryOffset + 1, battleCount - 1);
                ImGui.SameLine();
                if (ImGui.SmallButton(">>")) battleHistoryOffset = Math.Max(battleHistoryOffset - 1, 0);
                ImGui.SameLine();
            }

            if (!inCombatNow && battleHistoryOffset != 0)
            {
                if (ImGui.SmallButton("最新")) battleHistoryOffset = 0;
                ImGui.SameLine();
            }

            if (ImGui.SmallButton("清除"))
                clearRequested = true;

            ImGui.SameLine();
            var slotText = inCombatNow ? "战斗中" : $"历史 {battleHistoryOffset + 1}/{battleCount}";
            ImGui.TextDisabled($"{slotText}  {FormatDuration(seconds)}  {zone}");

            ImGui.Dummy(new Vector2(1f, ImGui.GetTextLineHeight() * 0.25f));
            return clearRequested;
        }

        public override void Draw()
        {
            if (DalamudApi.Conditions[ConditionFlag.PvPDisplayActive]) return;
            if (!config.CardsEnabled)
            {
                IsOpen = false;
                return;
            }

            var clickThroughActive = config.ClickThrough && !ImGui.GetIO().KeyAlt;
            if (!positionedFromConfig && config.HasCardsWindowPos)
            {
                ImGui.SetWindowPos(config.CardsWindowPos, ImGuiCond.Always);
                positionedFromConfig = true;
            }

            Flags = ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.NoFocusOnAppearing |
                    ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
                    ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoBackground |
                    (clickThroughActive ? (ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoMouseInputs) : ImGuiWindowFlags.None);
            BgAlpha = 0f;

            var inCombatNow = DalamudApi.Conditions.Any(ConditionFlag.InCombat);

            var localPlayerId = DalamudApi.ObjectTable.LocalPlayer?.EntityId ?? 0;

            var hasBattle = false;
            var showPreview = false;
            var battleCount = 0;
            var seconds = 1f;
            var canSimDots = false;
            var totalDotDamage = 0L;
            var limitDamage = 0L;
            var participantCount = 0;
            string? zone = null;
            var nameByActor = new Dictionary<uint, string>();
            var rows = new List<(uint Actor, uint JobId, long Damage, uint Death, float D, float C, float DC)>();

            lock (_plugin.SyncRoot)
            {
                var battleIndices = new List<int>(_plugin.Battles.Count);
                for (var i = 0; i < _plugin.Battles.Count; i++)
                {
                    var b = _plugin.Battles[i];
                    if (b.StartTime != 0 || b.EndTime != 0 || b.DataDic.Count != 0)
                        battleIndices.Add(i);
                }

                battleCount = battleIndices.Count;
                if (inCombatNow)
                {
                    battleHistoryOffset = 0;
                    var battle = _plugin.Battles[^1];
                    hasBattle = true;
                    zone = battle.Zone ?? "Unknown";
                    seconds = battle.Duration();
                    canSimDots = battle.Level is >= 64 && !float.IsInfinity(battle.TotalDotSim) && battle.TotalDotSim != 0;
                    totalDotDamage = battle.TotalDotDamage;
                    participantCount = battle.DataDic.Count;
                    limitDamage = battle.LimitBreak.Count > 0 ? battle.LimitBreak.Values.Sum() : 0L;
                    nameByActor = new Dictionary<uint, string>(battle.Name);

                    Dictionary<uint, float>? dotByActor = null;
                    if (canSimDots)
                    {
                        dotByActor = new Dictionary<uint, float>();
                        foreach (var (active, dotDmg) in battle.DotDmgList)
                        {
                            var source = (uint)(active & 0xFFFFFFFF);
                            dotByActor[source] = dotByActor.TryGetValue(source, out var cur) ? cur + dotDmg : dotDmg;
                        }
                    }

                    rows = new List<(uint Actor, uint JobId, long Damage, uint Death, float D, float C, float DC)>(battle.DataDic.Count);
                    foreach (var (actor, damage) in battle.DataDic)
                    {
                        var totalDamage = damage.Damages.TryGetValue(0, out var dmg) ? dmg.Damage : 0;
                        if (dotByActor != null && dotByActor.TryGetValue(actor, out var dotDamage))
                            totalDamage += (long)dotDamage;

                        damage.Damages.TryGetValue(0, out var baseDamage);
                        var swings = baseDamage?.swings ?? 0;
                        var dRate = swings == 0 ? -1f : (float)baseDamage!.D / swings;
                        var cRate = swings == 0 ? -1f : (float)baseDamage!.C / swings;
                        var dcRate = swings == 0 ? -1f : (float)baseDamage!.DC / swings;
                        rows.Add((actor, damage.JobId, totalDamage, damage.Death, dRate, cRate, dcRate));
                    }
                }
                else if (battleCount > 0)
                {
                    battleHistoryOffset = Math.Clamp(battleHistoryOffset, 0, battleCount - 1);

                    var battleIndex = battleIndices[battleCount - 1 - battleHistoryOffset];
                    var battle = _plugin.Battles[battleIndex];

                    hasBattle = true;
                    zone = battle.Zone ?? "Unknown";
                    seconds = battle.Duration();
                    canSimDots = battle.Level is >= 64 && !float.IsInfinity(battle.TotalDotSim) && battle.TotalDotSim != 0;
                    totalDotDamage = battle.TotalDotDamage;
                    participantCount = battle.DataDic.Count;
                    limitDamage = battle.LimitBreak.Count > 0 ? battle.LimitBreak.Values.Sum() : 0L;
                    nameByActor = new Dictionary<uint, string>(battle.Name);

                    Dictionary<uint, float>? dotByActor = null;
                    if (canSimDots)
                    {
                        dotByActor = new Dictionary<uint, float>();
                        foreach (var (active, dotDmg) in battle.DotDmgList)
                        {
                            var source = (uint)(active & 0xFFFFFFFF);
                            dotByActor[source] = dotByActor.TryGetValue(source, out var cur) ? cur + dotDmg : dotDmg;
                        }
                    }

                    rows = new List<(uint Actor, uint JobId, long Damage, uint Death, float D, float C, float DC)>(battle.DataDic.Count);
                    foreach (var (actor, damage) in battle.DataDic)
                    {
                        var totalDamage = damage.Damages.TryGetValue(0, out var dmg) ? dmg.Damage : 0;
                        if (dotByActor != null && dotByActor.TryGetValue(actor, out var dotDamage))
                            totalDamage += (long)dotDamage;

                        damage.Damages.TryGetValue(0, out var baseDamage);
                        var swings = baseDamage?.swings ?? 0;
                        var dRate = swings == 0 ? -1f : (float)baseDamage!.D / swings;
                        var cRate = swings == 0 ? -1f : (float)baseDamage!.C / swings;
                        var dcRate = swings == 0 ? -1f : (float)baseDamage!.DC / swings;
                        rows.Add((actor, damage.JobId, totalDamage, damage.Death, dRate, cRate, dcRate));
                    }
                }
                else
                {
                    showPreview = config.CardsPlacementMode;
                }
            }

            if (!hasBattle && !showPreview) return;

            if (hasBattle)
            {
                if (DrawBattleHeader(inCombatNow, battleCount, zone, seconds))
                {
                    ClearBattleHistory();
                    return;
                }

                DrawRows(rows, nameByActor, seconds, clickThroughActive, localPlayerId, hasBattle: true,
                    participantCount: participantCount, totalDotDamage: totalDotDamage, limitDamage: limitDamage, canSimDots: canSimDots);
                return;
            }

            // Preview (only used when there is no battle data yet).
            var previewNames = new Dictionary<uint, string>();
            var previewJobs = new[] { 19u, 21u, 24u, 27u, 28u, 31u, 35u, 38u };
            var previewRows = new List<(uint Actor, uint JobId, long Damage, uint Death, float D, float C, float DC)>();
            for (var i = 0; i < previewJobs.Length; i++)
            {
                var actorId = (uint)(i + 1);
                previewNames[actorId] = $"预览 {i + 1}";
                previewRows.Add((actorId, previewJobs[i], (previewJobs.Length - i) * 1000, 0, -1f, -1f, -1f));
            }

            DrawRows(previewRows, previewNames, seconds: 1f, clickThroughActive, localPlayerId: 0, hasBattle: false,
                participantCount: 0, totalDotDamage: 0, limitDamage: 0, canSimDots: false);
        }

        private void DrawRows(
            List<(uint Actor, uint JobId, long Damage, uint Death, float D, float C, float DC)> rows,
            Dictionary<uint, string> nameByActor,
            float seconds,
            bool clickThroughActive,
            uint localPlayerId,
            bool hasBattle,
            int participantCount,
            long totalDotDamage,
            long limitDamage,
            bool canSimDots)
        {
            var cardScale = Math.Clamp(config.CardsScale, 0.5f, 2.0f);
            var pushedFont = false;
            if (_plugin.CardsFontHandle != null && _plugin.CardsFontHandle.Available)
            {
                _plugin.CardsFontHandle.Push();
                pushedFont = true;
            }

            var summaryLineHeight = ImGui.GetTextLineHeight();
            if (hasBattle)
            {
                var actorDamageTotal = rows.Sum(r => r.Damage);

                var totalDamageAll = actorDamageTotal;
                if (totalDotDamage != 0 && !canSimDots)
                    totalDamageAll += totalDotDamage;
                if (limitDamage > 0)
                    totalDamageAll += limitDamage;

                var totalDps = seconds <= 0 ? 0 : (float)totalDamageAll / seconds;
                var limitDps = seconds <= 0 ? 0 : (float)limitDamage / seconds;

                ImGui.TextDisabled($"总秒伤 {totalDps:F1}  总伤害 {totalDamageAll:N0}  参与 {participantCount}");
                if (limitDamage > 0)
                    ImGui.TextDisabled($"极限技 {limitDps:F1}（{limitDamage:N0}）");
            }
            else
            {
                ImGui.TextDisabled("预览模式（右键拖动整体）");
            }

            ImGui.Dummy(new Vector2(1f, summaryLineHeight * 0.25f));

            rows.Sort((a, b) =>
            {
                return config.SortMode switch
                {
                    2 => StringComparer.CurrentCulture.Compare(
                        nameByActor.GetValueOrDefault(a.Actor, JobNameCn(a.JobId)),
                        nameByActor.GetValueOrDefault(b.Actor, JobNameCn(b.JobId))),
                    1 => b.Damage.CompareTo(a.Damage),
                    _ => ((float)b.Damage / seconds).CompareTo((float)a.Damage / seconds),
                };
            });

            var rankByActor = new Dictionary<uint, int>(rows.Count);
            for (var i = 0; i < rows.Count; i++)
                rankByActor[rows[i].Actor] = i + 1;

            if (config.TopN > 0 && rows.Count > config.TopN)
            {
                var localPlayerRank = localPlayerId != 0 && rankByActor.TryGetValue(localPlayerId, out var lpr) ? lpr : 0;
                if (localPlayerRank > config.TopN)
                {
                    var othersCount = Math.Max(0, config.TopN - 1);
                    var selfRow = rows[localPlayerRank - 1];
                    rows = rows.Take(othersCount).Append(selfRow).ToList();
                }
                else
                {
                    rows = rows.Take(config.TopN).ToList();
                }
            }

            var maxDps = rows.Count == 0 ? 0 : rows.Max(r => (float)r.Damage / seconds);
            var lineHeight = ImGui.GetTextLineHeight();

            var baseCardWidth = Math.Clamp(config.DisplayLayout == 0 ? config.CardColumnWidth : config.CardRowWidth, 120f, 800f);
            var baseCardHeight = Math.Clamp(config.DisplayLayout == 0 ? config.CardColumnHeight : config.CardRowHeight, 24f, 300f);
            var baseSpacing = Math.Clamp(config.DisplayLayout == 0 ? config.CardColumnSpacing : config.CardRowSpacing, 0f, 40f);

            var cardWidth = baseCardWidth * cardScale;
            var cardHeight = Math.Max(baseCardHeight * cardScale, lineHeight * 2.2f);
            var spacing = baseSpacing * cardScale;

            var dragStartRequested = false;

            void DrawCard((uint Actor, uint JobId, long Damage, uint Death, float D, float C, float DC) r)
            {
                var rank = rankByActor.TryGetValue(r.Actor, out var realRank) ? realRank : 0;

                ImGui.PushID((int)r.Actor);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
                ImGui.BeginChild("##card", new Vector2(cardWidth, cardHeight), false,
                    ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
                    (clickThroughActive ? (ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoMouseInputs) : ImGuiWindowFlags.None));
                try
                {
                    var iconSize = lineHeight * 1.2f;
                    if (_plugin.Icon.TryGetValue(r.JobId, out var icon) && icon != null)
                        ImGui.Image(icon.Handle, new Vector2(iconSize, iconSize));
                    else
                        ImGui.Dummy(new Vector2(iconSize, iconSize));

                    ImGui.SameLine();
                    var displayName = config.HideName
                        ? JobNameCn(r.JobId)
                        : nameByActor.GetValueOrDefault(r.Actor, JobNameCn(r.JobId));
                    ImGui.Text($"{rank}. {displayName}");
                    ImGui.SameLine(0, 6f);
                    if (config.ShowRates)
                        ImGui.TextDisabled($"直 {FormatRate(r.D)} 爆 {FormatRate(r.C)} 直暴 {FormatRate(r.DC)} 死 {r.Death:D}");
                    else
                        ImGui.TextDisabled($"死 {r.Death:D}");

                    var dps = seconds <= 0 ? 0 : (float)r.Damage / seconds;
                    var frac = maxDps <= 0 ? 0 : dps / maxDps;
                    if (frac < 0) frac = 0;
                    if (frac > 1) frac = 1;

                    var barY = Math.Max(iconSize, lineHeight) + 2f;
                    ImGui.SetCursorPos(new Vector2(0, barY));
                    var animatedFrac = Math.Clamp(SpringTo(dpsBarSprings, r.Actor, frac), 0f, 1f);
                    ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, lineHeight * 0.5f);
                    var barColor = (config.HighlightSelf && r.Actor == localPlayerId)
                        ? new Vector4(0.3f, 0.9f, 0.3f, 0.9f)
                        : new Vector4(0.25f, 0.65f, 1f, 0.9f);
                    ImGui.PushStyleColor(ImGuiCol.PlotHistogram, barColor);
                    ImGui.ProgressBar(animatedFrac, new Vector2(cardWidth, lineHeight), $"{dps:F1}");
                    ImGui.PopStyleColor();
                    ImGui.PopStyleVar();
                }
                finally
                {
                    ImGui.EndChild();
                    ImGui.PopStyleVar();
                }

                if (!clickThroughActive && ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                    dragStartRequested = true;

                ImGui.PopID();
            }

            var lineBreakSpacing = spacing > 0 ? spacing : 0.0001f;
            if (config.DisplayLayout == 0)
            {
                var perCol = Math.Clamp(config.CardsPerLine, 1, 100);
                var totalRows = Math.Min(perCol, rows.Count);
                var totalCols = (rows.Count + perCol - 1) / perCol;
                var contentW = totalCols <= 0 ? 0 : totalCols * cardWidth + (totalCols - 1) * spacing;
                var contentH = totalRows <= 0 ? 0 : totalRows * cardHeight + (totalRows - 1) * lineBreakSpacing;

                var startPos = ImGui.GetCursorPos();
                ImGui.Dummy(new Vector2(contentW, contentH));
                var afterDummy = ImGui.GetCursorPos();
                ImGui.SetCursorPos(startPos);

                for (var i = 0; i < rows.Count; i++)
                {
                    var row = i % perCol;
                    var col = i / perCol;
                    ImGui.SetCursorPos(new Vector2(
                        startPos.X + col * (cardWidth + spacing),
                        startPos.Y + row * (cardHeight + lineBreakSpacing)));
                    DrawCard(rows[i]);
                }

                ImGui.SetCursorPos(afterDummy);
            }
            else
            {
                var perRow = Math.Clamp(config.CardsPerLine, 1, 100);
                var totalCols = Math.Min(perRow, rows.Count);
                var totalRows = (rows.Count + perRow - 1) / perRow;
                var contentW = totalCols <= 0 ? 0 : totalCols * cardWidth + (totalCols - 1) * spacing;
                var contentH = totalRows <= 0 ? 0 : totalRows * cardHeight + (totalRows - 1) * lineBreakSpacing;

                var startPos = ImGui.GetCursorPos();
                ImGui.Dummy(new Vector2(contentW, contentH));
                var afterDummy = ImGui.GetCursorPos();
                ImGui.SetCursorPos(startPos);

                for (var i = 0; i < rows.Count; i++)
                {
                    var row = i / perRow;
                    var col = i % perRow;
                    ImGui.SetCursorPos(new Vector2(
                        startPos.X + col * (cardWidth + spacing),
                        startPos.Y + row * (cardHeight + lineBreakSpacing)));
                    DrawCard(rows[i]);
                }

                ImGui.SetCursorPos(afterDummy);
            }

            if (pushedFont)
                _plugin.CardsFontHandle!.Pop();

            if (!clickThroughActive)
            {
                if (dragStartRequested)
                    draggingCards = true;

                if (draggingCards)
                {
                    if (ImGui.IsMouseDown(ImGuiMouseButton.Right))
                    {
                        ImGui.SetWindowPos(ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta);
                    }
                    else
                    {
                        draggingCards = false;
                        config.CardsWindowPos = ImGui.GetWindowPos();
                        config.HasCardsWindowPos = true;
                        config.Save();
                    }
                }
            }
        }

        public void Dispose()
        {
        }
    }
}
