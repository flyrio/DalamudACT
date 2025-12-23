using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Lumina.Excel;
using Action = Lumina.Excel.Sheets.Action;
using Dalamud.Interface.Windowing;
using DalamudACT.Struct;
using Lumina.Excel.Sheets;
using Status = Lumina.Excel.Sheets.Status;

namespace DalamudACT;

internal class PluginUI : IDisposable
{
    private static PluginUI? instance;

    private static Configuration config;

    private static ACT _plugin;
    public static int choosed;
    private static ExcelSheet<Action> sheet = DalamudApi.GameData.GetExcelSheet<Action>()!;
    public static ConcurrentDictionary<uint, IDalamudTextureWrap?> Icon = new();
    private static ExcelSheet<Status> buffSheet = DalamudApi.GameData.GetExcelSheet<Status>()!;
    public static ConcurrentDictionary<uint, IDalamudTextureWrap?> BuffIcon = new();

    private static readonly ConcurrentDictionary<uint, Task<IDalamudTextureWrap>> IconTasks = new();
    private static readonly ConcurrentDictionary<uint, Task<IDalamudTextureWrap>> BuffIconTasks = new();
    private static volatile bool disposing;

    private static IDalamudTextureWrap? mainIcon;

    public ConfigWindow configWindow;
    public DebugWindow debugWindow;
    public MainWindow mainWindow;
    public CardsWindow cardsWindow;
    public WindowSystem WindowSystem = new("伤害统计");

    private static void EnsureActionIcon(uint actionId)
    {
        if (disposing || Icon.ContainsKey(actionId)) return;
        if (!sheet.TryGetRow(actionId, out var row) || row.Icon == 0)
        {
            Icon.TryAdd(actionId, null);
            return;
        }

        IconTasks.GetOrAdd(actionId, __ =>
        {
            var task = DalamudApi.TextureProvider.GetFromGameIcon(new GameIconLookup(row.Icon)).RentAsync();
            _ = task.ContinueWith(t =>
            {
                IconTasks.TryRemove(actionId, out _);
                if (t.Status != TaskStatus.RanToCompletion) return;
                if (disposing)
                {
                    t.Result.Dispose();
                    return;
                }

                Icon[actionId] = t.Result;
            }, TaskScheduler.Default);

            return task;
        });
    }

    private static void EnsureBuffIcon(uint buffId)
    {
        if (disposing || BuffIcon.ContainsKey(buffId)) return;
        if (!buffSheet.TryGetRow(buffId, out var row) || row.Icon == 0)
        {
            BuffIcon.TryAdd(buffId, null);
            return;
        }

        BuffIconTasks.GetOrAdd(buffId, __ =>
        {
            var task = DalamudApi.TextureProvider.GetFromGameIcon(new GameIconLookup(row.Icon)).RentAsync();
            _ = task.ContinueWith(t =>
            {
                BuffIconTasks.TryRemove(buffId, out _);
                if (t.Status != TaskStatus.RanToCompletion) return;
                if (disposing)
                {
                    t.Result.Dispose();
                    return;
                }

                BuffIcon[buffId] = t.Result;
            }, TaskScheduler.Default);

            return task;
        });
    }

    private static int PushBackgroundAlpha(float alpha)
    {
        alpha = Math.Clamp(alpha, 0f, 1f);
        var style = ImGui.GetStyle();

        Vector4 ScaleAlpha(Vector4 c) => new(c.X, c.Y, c.Z, c.W * alpha);

        var cols = new[]
        {
            ImGuiCol.ChildBg,
            ImGuiCol.PopupBg,
            ImGuiCol.Border,
            ImGuiCol.BorderShadow,
            ImGuiCol.FrameBg,
            ImGuiCol.FrameBgHovered,
            ImGuiCol.FrameBgActive,
            ImGuiCol.TitleBg,
            ImGuiCol.TitleBgActive,
            ImGuiCol.TitleBgCollapsed,
            ImGuiCol.MenuBarBg,
            ImGuiCol.ScrollbarBg,
            ImGuiCol.ScrollbarGrab,
            ImGuiCol.ScrollbarGrabHovered,
            ImGuiCol.ScrollbarGrabActive,
            ImGuiCol.SliderGrab,
            ImGuiCol.SliderGrabActive,
            ImGuiCol.Button,
            ImGuiCol.ButtonHovered,
            ImGuiCol.ButtonActive,
            ImGuiCol.Header,
            ImGuiCol.HeaderHovered,
            ImGuiCol.HeaderActive,
            ImGuiCol.Separator,
            ImGuiCol.SeparatorHovered,
            ImGuiCol.SeparatorActive,
            ImGuiCol.ResizeGrip,
            ImGuiCol.ResizeGripHovered,
            ImGuiCol.ResizeGripActive,
            ImGuiCol.Tab,
            ImGuiCol.TabHovered,
            ImGuiCol.TabActive,
            ImGuiCol.TabUnfocused,
            ImGuiCol.TabUnfocusedActive,
            ImGuiCol.DockingPreview,
            ImGuiCol.DockingEmptyBg,
            ImGuiCol.TableRowBg,
            ImGuiCol.TableRowBgAlt,
            ImGuiCol.TableHeaderBg,
            ImGuiCol.TableBorderStrong,
            ImGuiCol.TableBorderLight,
        };

        var pushCount = 0;
        foreach (var col in cols)
        {
            ImGui.PushStyleColor(col, ScaleAlpha(style.Colors[(int)col]));
            pushCount++;
        }

        return pushCount;
    }

    private static int PushReadablePopupStyle(float minAlpha = 1.0f)
    {
        minAlpha = Math.Clamp(minAlpha, 0f, 1f);
        var style = ImGui.GetStyle();

        var popupBg = style.Colors[(int)ImGuiCol.PopupBg];
        var border = style.Colors[(int)ImGuiCol.Border];

        var bgAlpha = Math.Max(popupBg.W, minAlpha);
        var borderAlpha = Math.Max(border.W, Math.Min(1f, bgAlpha));

        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(popupBg.X, popupBg.Y, popupBg.Z, bgAlpha));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(border.X, border.Y, border.Z, borderAlpha));
        return 2;
    }

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

    public PluginUI(ACT p)
    {
        instance = this;
        _plugin = p;
        config = p.Configuration;

        mainIcon = File.Exists(DalamudApi.PluginInterface.AssemblyLocation.Directory?.FullName + "\\DDD.png")
            ? DalamudApi.TextureProvider.GetFromFile(
                DalamudApi.PluginInterface.AssemblyLocation.Directory?.FullName + "\\DDD.png").RentAsync().Result
            : DalamudApi.TextureProvider.GetFromGameIcon(new GameIconLookup(62142)).RentAsync().Result;

        configWindow = new ConfigWindow(_plugin);
        debugWindow = new DebugWindow(_plugin);
        mainWindow = new MainWindow(_plugin);
        cardsWindow = new CardsWindow(_plugin);

        WindowSystem.AddWindow(configWindow);
        WindowSystem.AddWindow(debugWindow);
        WindowSystem.AddWindow(mainWindow);
        WindowSystem.AddWindow(cardsWindow);

        mainWindow.IsOpen = true;
        cardsWindow.IsOpen = config.CardsEnabled;

    }

    public void Dispose()
    {
        disposing = true;
        instance = null;
        foreach (var (_, texture) in Icon) texture?.Dispose();

        foreach (var (_, texture) in BuffIcon) texture?.Dispose();
        mainIcon?.Dispose();

        WindowSystem.RemoveAllWindows();
        configWindow.Dispose();
        debugWindow.Dispose();
        mainWindow?.Dispose();
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
                    changed |= ImGui.Checkbox("禁止调整大小", ref config.NoResize);
                    changed |= ImGui.Checkbox("鼠标穿透（按住 Alt 临时操作）", ref config.ClickThrough);
                    changed |= ImGui.SliderInt("背景透明度（不影响文字）", ref config.BGColor, 0, 100);

                    if (ImGui.Button("重置窗口大小"))
                    {
                        config.WindowSize = new Vector2(480, 320);
                        changed = true;
                    }
                }

                if (ImGui.CollapsingHeader("显示", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    changed |= ImGui.Checkbox("隐藏玩家姓名（用职业名代替）", ref config.HideName);
                    changed |= ImGui.SliderInt("仅显示前 N 名（0=全部）", ref config.TopN, 0, 24);
                    changed |= ImGui.Checkbox("显示直击/暴击/直爆列", ref config.ShowRates);
                    changed |= ImGui.Checkbox("高亮自己", ref config.HighlightSelf);
                    changed |= ImGui.Checkbox("自动紧凑布局（窄窗口隐藏部分列）", ref config.AutoCompact);
                    if (!config.AutoCompact)
                        changed |= ImGui.Checkbox("手动紧凑模式", ref config.CompactMode);

                    var showList = !config.Mini;
                    if (ImGui.Checkbox("显示纵向列表（关闭=最小化图标）", ref showList))
                    {
                        config.Mini = !showList;
                        config.ShowVerticalList = showList;
                        changed = true;
                    }

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
                            changed |= ImGui.SliderFloat("名片宽度(列)", ref config.CardColumnWidth, 120f, 800f);
                            changed |= ImGui.SliderFloat("名片高度(列)", ref config.CardColumnHeight, 24f, 200f);
                            changed |= ImGui.SliderFloat("名片间距(列)", ref config.CardColumnSpacing, 0f, 40f);
                        }
                        else
                        {
                            changed |= ImGui.SliderFloat("名片宽度(行)", ref config.CardRowWidth, 120f, 800f);
                            changed |= ImGui.SliderFloat("名片高度(行)", ref config.CardRowHeight, 24f, 200f);
                            changed |= ImGui.SliderFloat("名片间距(行)", ref config.CardRowSpacing, 0f, 40f);
                        }
                    }

                    var sortModes = new[] { "按秒伤", "按总伤害", "按姓名" };
                    var sortMode = config.SortMode;
                    if (ImGui.Combo("排序方式", ref sortMode, sortModes, sortModes.Length))
                    {
                        config.SortMode = sortMode;
                        changed = true;
                    }

                    if (ImGui.Button("重置列宽"))
                    {
                        config.TableLayoutSeed++;
                        changed = true;
                    }
                }

                if (ImGui.CollapsingHeader("战斗", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    // 纵向列表/最小化 已合并到“视图”里控制
                    changed |= ImGui.Checkbox("显示 DoT 模拟偏差", ref config.delta);
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

    public class DebugWindow : Window, IDisposable
    {

        public DebugWindow(ACT plugin) : base("伤害统计 - 调试")
        {

        }

        public override void Draw()
        {
            BgAlpha = Math.Clamp(config.BGColor / 100f, 0f, 1f);
            var popStyle = PushBackgroundAlpha(config.BGColor / 100f);
            try
            {
            lock (_plugin.SyncRoot)
            {
                if (_plugin.Battles.Count < 1) return;
                choosed = Math.Clamp(choosed, 0, _plugin.Battles.Count - 1);
                var battle = _plugin.Battles[choosed];

                ImGui.Text($"持续伤害秒伤：{(float)battle.TotalDotDamage / battle.Duration():F1}");

            if (ImGui.BeginTable("Pot", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                var headers = new[] { "姓名", "对象ID", "参考技能", "技能威力", "速度倍率", "伤害/威力" };
                foreach (var t in headers) ImGui.TableSetupColumn(t);
                ImGui.TableHeadersRow();

                foreach (var (actor, damage) in battle.DataDic)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text($"{battle.Name.GetValueOrDefault(actor, actor.ToString("X"))}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{actor:X}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{(sheet.TryGetRow(damage.PotSkill, out var potRow) ? potRow.Name.ExtractText() : damage.PotSkill.ToString())}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{damage.SkillPotency:F1}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{damage.Speed:F3}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{battle.DPP(actor):F3}");
                }

                ImGui.EndTable();
            }

            ImGui.Separator();

            if (ImGui.BeginTable("Dots", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                var headers = new string[] { "状态", "来源", "权重", "模拟秒伤", "分摊秒伤" };

                foreach (var t in headers) ImGui.TableSetupColumn(t);

                ImGui.TableHeadersRow();
                var total = 0f;
                foreach (var (active, potency) in battle.PlayerDotPotency)
                {
                    var source = (uint)(active & 0xFFFFFFFF);
                    total += battle.DPP(source) * potency;
                }

                foreach (var (active, potency) in battle.PlayerDotPotency)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    var buff = (uint)(active >> 32);
                    var source = (uint)(active & 0xFFFFFFFF);
                    ImGui.Text($"{(buffSheet.TryGetRow(buff, out var buffRow) ? buffRow.Name.ExtractText() : buff.ToString())}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{battle.Name.GetValueOrDefault(source, source.ToString("X"))}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{potency}");
                    ImGui.TableNextColumn();
                    ImGui.Text(
                        $"{battle.DPP(source) * potency / battle.Duration():F1}");
                    ImGui.TableNextColumn();
                    ImGui.Text(
                        $"{(total <= 0 ? 0 : battle.TotalDotDamage * battle.DPP(source) * potency / total / battle.Duration()):F1}");
                }

                ImGui.EndTable();
                if (battle.TotalDotDamage > 0 && total > 0)
                    ImGui.Text($"模拟与实际秒伤偏差：{total * 100 / battle.TotalDotDamage - 100:F2}%");
            }
            }
            }
            finally
            {
                ImGui.PopStyleColor(popStyle);
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
        public CardsWindow(ACT plugin) : base("伤害统计 - 名片列", ImGuiWindowFlags.AlwaysAutoResize, false)
        {
        }

        public void ResetPositioning()
        {
            positionedFromConfig = false;
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

            ACTBattle battle;
            var seconds = 1f;
            var canSimDots = false;
            lock (_plugin.SyncRoot)
            {
                if (_plugin.Battles.Count < 1) return;

                var inCombat = DalamudApi.Conditions.Any(ConditionFlag.InCombat);
                var idx = choosed;
                if (inCombat && _plugin.Battles[^1].StartTime != 0) idx = _plugin.Battles.Count - 1;
                idx = Math.Clamp(idx, 0, _plugin.Battles.Count - 1);
                battle = _plugin.Battles[idx];
                if (battle.StartTime == 0) return;

                seconds = battle.Duration();
                canSimDots = battle.Level is >= 64 && !float.IsInfinity(battle.TotalDotSim) && battle.TotalDotSim != 0;
            }

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

            var localPlayerId = DalamudApi.ClientState.LocalPlayer?.EntityId ?? 0;
            var rows = new List<(uint Actor, uint JobId, long Damage, uint Death)>(battle.DataDic.Count);
            foreach (var (actor, damage) in battle.DataDic)
            {
                var totalDamage = damage.Damages.TryGetValue(0, out var dmg) ? dmg.Damage : 0;
                if (dotByActor != null && dotByActor.TryGetValue(actor, out var dotDamage))
                    totalDamage += (long)dotDamage;

                rows.Add((actor, damage.JobId, totalDamage, damage.Death));
            }

            rows.Sort((a, b) =>
            {
                return config.SortMode switch
                {
                    2 => StringComparer.CurrentCulture.Compare(
                        battle.Name.GetValueOrDefault(a.Actor, JobNameCn(a.JobId)),
                        battle.Name.GetValueOrDefault(b.Actor, JobNameCn(b.JobId))),
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

            var cardWidth = Math.Clamp(config.DisplayLayout == 0 ? config.CardColumnWidth : config.CardRowWidth, 120f, 800f);
            var cardHeight = Math.Clamp(config.DisplayLayout == 0 ? config.CardColumnHeight : config.CardRowHeight, lineHeight * 2.2f, 300f);
            var spacing = Math.Clamp(config.DisplayLayout == 0 ? config.CardColumnSpacing : config.CardRowSpacing, 0f, 40f);

            if (!clickThroughActive && ImGui.IsWindowHovered())
            {
                var wheel = ImGui.GetIO().MouseWheel;
                if (Math.Abs(wheel) > 0.0001f)
                {
                    var changed = false;
                    if (ImGui.GetIO().KeyCtrl && ImGui.GetIO().KeyShift)
                    {
                        if (config.DisplayLayout == 0) config.CardColumnSpacing = Math.Clamp(config.CardColumnSpacing + wheel, 0f, 40f);
                        else config.CardRowSpacing = Math.Clamp(config.CardRowSpacing + wheel, 0f, 40f);
                        changed = true;
                    }
                    else if (ImGui.GetIO().KeyCtrl)
                    {
                        var delta = wheel * 10f;
                        if (config.DisplayLayout == 0) config.CardColumnWidth = Math.Clamp(config.CardColumnWidth + delta, 120f, 800f);
                        else config.CardRowWidth = Math.Clamp(config.CardRowWidth + delta, 120f, 800f);
                        changed = true;
                    }
                    else if (ImGui.GetIO().KeyShift)
                    {
                        var delta = wheel * 2f;
                        if (config.DisplayLayout == 0) config.CardColumnHeight = Math.Clamp(config.CardColumnHeight + delta, 24f, 300f);
                        else config.CardRowHeight = Math.Clamp(config.CardRowHeight + delta, 24f, 300f);
                        changed = true;
                    }

                    if (changed) config.Save();
                }
            }

            var dragStartRequested = false;

            void DrawCard((uint Actor, uint JobId, long Damage, uint Death) r)
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
                        : battle.Name.GetValueOrDefault(r.Actor, JobNameCn(r.JobId));
                    ImGui.Text($"{rank}. {displayName}");

                    var dps = seconds <= 0 ? 0 : (float)r.Damage / seconds;
                    var frac = maxDps <= 0 ? 0 : dps / maxDps;
                    if (frac < 0) frac = 0;
                    if (frac > 1) frac = 1;

                    var barY = Math.Max(iconSize, lineHeight) + 2f;
                    ImGui.SetCursorPos(new Vector2(0, barY));
                    ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
                    var barColor = (config.HighlightSelf && r.Actor == localPlayerId)
                        ? new Vector4(0.3f, 0.9f, 0.3f, 0.9f)
                        : new Vector4(0.25f, 0.65f, 1f, 0.9f);
                    ImGui.PushStyleColor(ImGuiCol.PlotHistogram, barColor);
                    ImGui.ProgressBar(frac, new Vector2(cardWidth, lineHeight), $"{dps:F1}");
                    ImGui.PopStyleColor();
                    ImGui.PopStyleVar();
                }
                finally
                {
                    ImGui.EndChild();
                    ImGui.PopStyleVar();
                }

                if (!clickThroughActive && ImGui.IsItemHovered())
                    instance?.mainWindow?.DrawDetails(r.Actor);

                if (!clickThroughActive && ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                    dragStartRequested = true;

                ImGui.PopID();
            }

            for (var i = 0; i < rows.Count; i++)
            {
                DrawCard(rows[i]);

                if (i == rows.Count - 1) continue;

                if (config.DisplayLayout == 0)
                {
                    if (spacing > 0) ImGui.Dummy(new Vector2(1, spacing));
                }
                else
                {
                    ImGui.SameLine(0, spacing);
                }
            }

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

    public class MainWindow : Window, IDisposable
    {
        private List<Dictionary<uint, long>> savedBattle = new();
        private long startTime = 0;
        private long lastTime = 0;
        private string filter = string.Empty;
        private static readonly string[] SortModes = { "按秒伤", "按总伤害", "按姓名" };
        private static readonly WindowSizeConstraints NormalConstraints = new()
        {
            MinimumSize = new Vector2(200, 120),
            MaximumSize = new Vector2(3000, 3000),
        };

        private static readonly WindowSizeConstraints MiniConstraints = new()
        {
            MinimumSize = new Vector2(1, 1),
            MaximumSize = new Vector2(200, 200),
        };

        private bool pendingRestore;
        private Vector2 pendingRestoreSize;
        private bool pendingRestorePosition;
        private Vector2 pendingRestorePos;
        private bool wasForcedMini;
        private bool wasInMiniMode;
        private bool draggingMiniIcon;

        public MainWindow(ACT plugin) : base("伤害统计")
        {
            Flags = ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.MenuBar | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar;
            SizeConstraints = NormalConstraints;
        }

        public override void Draw()
        {
            
            if (DalamudApi.Conditions[ConditionFlag.PvPDisplayActive]) return;
            var inCombat = DalamudApi.Conditions.Any(ConditionFlag.InCombat);
            var forceMini = false;
            var inMiniModeNow = config.Mini || forceMini;

            if (!wasInMiniMode && inMiniModeNow)
            {
                var beforeMiniSize = ImGui.GetWindowSize();
                if (beforeMiniSize.X > 200 && beforeMiniSize.Y > 120)
                    config.WindowSize = beforeMiniSize;

                config.MainWindowPos = ImGui.GetWindowPos();
                config.HasMainWindowPos = true;

                if (config.HasMiniWindowPos)
                    ImGui.SetWindowPos(config.MiniWindowPos, ImGuiCond.Always);
            }

            if (!inMiniModeNow && wasInMiniMode)
            {
                var restoreSize = config.WindowSize;
                if (restoreSize.X < 200 || restoreSize.Y < 120)
                    restoreSize = new Vector2(480, 320);
                pendingRestore = true;
                pendingRestoreSize = restoreSize;

                if (config.HasMainWindowPos)
                {
                    pendingRestorePosition = true;
                    pendingRestorePos = config.MainWindowPos;
                }
            }

            wasInMiniMode = inMiniModeNow;
            wasForcedMini = forceMini;

            if (inMiniModeNow)
            {
                SizeConstraints = MiniConstraints;
                DrawMini(forceMini);
                return;
            }
            SizeConstraints = NormalConstraints;
            lock (_plugin.SyncRoot)
            {
                if (_plugin.Battles.Count < 1) return;
                if (inCombat && _plugin.Battles[^1].StartTime != 0) choosed = _plugin.Battles.Count - 1;
                choosed = Math.Clamp(choosed, 0, _plugin.Battles.Count - 1);
            var clickThroughActive = config.ClickThrough && !ImGui.GetIO().KeyAlt;
            Flags = ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.MenuBar | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar |
                    (config.NoResize ? ImGuiWindowFlags.NoResize : ImGuiWindowFlags.None) |
                    (clickThroughActive ? ImGuiWindowFlags.NoInputs : ImGuiWindowFlags.None);
            BgAlpha = Math.Clamp(config.BGColor / 100f, 0f, 1f);

            var popStyle = PushBackgroundAlpha(config.BGColor / 100f);
            try
            {
                var restoringThisFrame = false;
                if (pendingRestore)
                {
                    restoringThisFrame = true;
                    pendingRestore = false;
                    ImGui.SetWindowSize(pendingRestoreSize, ImGuiCond.Always);
                    config.WindowSize = pendingRestoreSize;
                }
                if (pendingRestorePosition)
                {
                    pendingRestorePosition = false;
                    ImGui.SetWindowPos(pendingRestorePos, ImGuiCond.Always);
                }

                var currentSize = ImGui.GetWindowSize();
                if (!restoringThisFrame && currentSize.X > 200 && currentSize.Y > 120)
                    config.WindowSize = currentSize;
                {
                    var battle = _plugin.Battles[choosed];
                    var seconds = battle.Duration();
                    if (ImGui.BeginMenuBar())
                    {
                        var menuCompact = ImGui.GetWindowSize().X < 560;

                        if (ImGui.ArrowButton("最小化", ImGuiDir.Left))
                        {
                            config.Mini = true;
                            config.ShowVerticalList = false;
                            config.WindowSize = ImGui.GetWindowSize();
                            config.Save();
                            ImGui.EndMenuBar();
                            return;
                        }

                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("最小化（显示图标）");
                        ImGui.SameLine();
                        if (menuCompact)
                        {
                            if (ImGui.Button("菜单"))
                                ImGui.OpenPopup("##mainMenu");

                            var popPopup = PushReadablePopupStyle();
                            if (ImGui.BeginPopup("##mainMenu"))
                            {
                                var changed = false;

                                ImGui.TextDisabled("战斗");
                                var items = new string[_plugin.Battles.Count];
                                for (var i = 0; i < _plugin.Battles.Count; i++)
                                {
                                    var b = _plugin.Battles[i];
                                    if (b.StartTime == 0)
                                    {
                                        items[i] = "(空)";
                                        continue;
                                    }

                                    var start = DateTimeOffset.FromUnixTimeSeconds(b.StartTime).ToLocalTime();
                                    var end = DateTimeOffset.FromUnixTimeSeconds(b.EndTime).ToLocalTime();
                                    items[i] = $"{start:t}-{end:t} {b.Zone}";
                                }

                                if (_plugin.Battles[^1].StartTime != 0)
                                    items[^1] = $"当前: {_plugin.Battles[^1].Zone}";

                                ImGui.SetNextItemWidth(260);
                                ImGui.Combo("##battles", ref choosed, items, _plugin.Battles.Count);
                                ImGui.SameLine();
                                ImGui.Text(seconds is > 3600 or <= 1 ? $"00:00" : $"{seconds / 60:00}:{seconds % 60:00}");

                                ImGui.Separator();
                                ImGui.TextDisabled("筛选/排序");
                                ImGui.SetNextItemWidth(260);
                                ImGui.InputTextWithHint("##filter", "筛选：姓名/职业", ref filter, 64);
                                ImGui.SetNextItemWidth(260);
                                var sortMode = config.SortMode;
                                if (ImGui.Combo("##sortMode", ref sortMode, SortModes, SortModes.Length))
                                {
                                    config.SortMode = sortMode;
                                    changed = true;
                                }

                                ImGui.Separator();
                                ImGui.TextDisabled("视图");
                                changed |= ImGui.SliderInt("仅显示前 N 名（0=全部）", ref config.TopN, 0, 24);
                                changed |= ImGui.Checkbox("显示直击/暴击/直爆列", ref config.ShowRates);
                                changed |= ImGui.Checkbox("高亮自己", ref config.HighlightSelf);
                                changed |= ImGui.Checkbox("自动紧凑布局（窄窗口隐藏部分列）", ref config.AutoCompact);
                                if (!config.AutoCompact)
                                    changed |= ImGui.Checkbox("手动紧凑模式", ref config.CompactMode);
                                else
                                    ImGui.TextDisabled("自动紧凑布局开启时，忽略“手动紧凑模式”。");

                                var showList = !config.Mini;
                                if (ImGui.Checkbox("显示纵向列表（关闭=最小化图标）", ref showList))
                                {
                                    config.Mini = !showList;
                                    config.ShowVerticalList = showList;
                                    changed = true;
                                }

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
                                        changed |= ImGui.SliderFloat("名片宽度(列)", ref config.CardColumnWidth, 120f, 800f);
                                        changed |= ImGui.SliderFloat("名片高度(列)", ref config.CardColumnHeight, 24f, 200f);
                                        changed |= ImGui.SliderFloat("名片间距(列)", ref config.CardColumnSpacing, 0f, 40f);
                                    }
                                    else
                                    {
                                        changed |= ImGui.SliderFloat("名片宽度(行)", ref config.CardRowWidth, 120f, 800f);
                                        changed |= ImGui.SliderFloat("名片高度(行)", ref config.CardRowHeight, 24f, 200f);
                                        changed |= ImGui.SliderFloat("名片间距(行)", ref config.CardRowSpacing, 0f, 40f);
                                    }
                                }

                                ImGui.Separator();
                                ImGui.TextDisabled("窗口");
                                changed |= ImGui.Checkbox("鼠标穿透（按住 Alt 临时操作）", ref config.ClickThrough);
                                changed |= ImGui.SliderInt("背景透明度（不影响文字）", ref config.BGColor, 0, 100);

                                ImGui.Separator();
                                if (ImGui.Button("清空筛选"))
                                    filter = string.Empty;
                                ImGui.SameLine();
                                if (ImGui.Button("重置列宽"))
                                {
                                    config.TableLayoutSeed++;
                                    changed = true;
                                }

                                ImGui.Separator();
                                if (ImGui.MenuItem(config.HideName ? "姓名：隐藏" : "姓名：显示"))
                                {
                                    config.HideName = !config.HideName;
                                    changed = true;
                                }
                                if (ImGui.MenuItem("打开设置"))
                                    instance?.configWindow.IsOpen = true;
                                if (ImGui.MenuItem("打开调试"))
                                    instance?.debugWindow.IsOpen = true;

                                if (changed) config.Save();
                                ImGui.EndPopup();
                            }
                            ImGui.PopStyleColor(popPopup);
                        }
                        else
                        {
                            var items = new string[_plugin.Battles.Count];
                            for (var i = 0; i < _plugin.Battles.Count; i++)
                            {
                                var b = _plugin.Battles[i];
                                if (b.StartTime == 0)
                                {
                                    items[i] = "(空)";
                                    continue;
                                }

                                var start = DateTimeOffset.FromUnixTimeSeconds(b.StartTime).ToLocalTime();
                                var end = DateTimeOffset.FromUnixTimeSeconds(b.EndTime).ToLocalTime();
                                items[i] = $"{start:t}-{end:t} {b.Zone}";
                            }

                            if (_plugin.Battles[^1].StartTime != 0)
                                items[^1] = $"当前: {_plugin.Battles[^1].Zone}";

                            ImGui.SetNextItemWidth(250);
                            ImGui.Combo("##battles", ref choosed, items, _plugin.Battles.Count);

                            ImGui.Text(seconds is > 3600 or <= 1 ? $"00:00" : $"{seconds / 60:00}:{seconds % 60:00}");

                            ImGui.SameLine();
                            ImGui.SetNextItemWidth(160);
                            ImGui.InputTextWithHint("##filter", "筛选：姓名/职业", ref filter, 64);

                            ImGui.SameLine();
                            ImGui.SetNextItemWidth(90);
                            var sortMode = config.SortMode;
                            if (ImGui.Combo("##sortMode", ref sortMode, SortModes, SortModes.Length))
                            {
                                config.SortMode = sortMode;
                                config.Save();
                            }

                            ImGui.SameLine();
                            if (ImGui.Button("视图"))
                                ImGui.OpenPopup("##view");
                            var popPopup = PushReadablePopupStyle();
                            if (ImGui.BeginPopup("##view"))
                            {
                                var changed = false;
                                changed |= ImGui.SliderInt("仅显示前 N 名（0=全部）", ref config.TopN, 0, 24);
                                changed |= ImGui.Checkbox("显示直击/暴击/直爆列", ref config.ShowRates);
                                changed |= ImGui.Checkbox("高亮自己", ref config.HighlightSelf);
                                changed |= ImGui.Checkbox("自动紧凑布局（窄窗口隐藏部分列）", ref config.AutoCompact);
                                if (!config.AutoCompact)
                                    changed |= ImGui.Checkbox("手动紧凑模式", ref config.CompactMode);
                                else
                                    ImGui.TextDisabled("自动紧凑布局开启时，忽略“手动紧凑模式”。");

                                var showList = !config.Mini;
                                if (ImGui.Checkbox("显示纵向列表（关闭=最小化图标）", ref showList))
                                {
                                    config.Mini = !showList;
                                    config.ShowVerticalList = showList;
                                    changed = true;
                                }

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
                                        changed |= ImGui.SliderFloat("名片宽度(列)", ref config.CardColumnWidth, 120f, 800f);
                                        changed |= ImGui.SliderFloat("名片高度(列)", ref config.CardColumnHeight, 24f, 200f);
                                        changed |= ImGui.SliderFloat("名片间距(列)", ref config.CardColumnSpacing, 0f, 40f);
                                    }
                                    else
                                    {
                                        changed |= ImGui.SliderFloat("名片宽度(行)", ref config.CardRowWidth, 120f, 800f);
                                        changed |= ImGui.SliderFloat("名片高度(行)", ref config.CardRowHeight, 24f, 200f);
                                        changed |= ImGui.SliderFloat("名片间距(行)", ref config.CardRowSpacing, 0f, 40f);
                                    }
                                }

                                ImGui.Separator();
                                if (ImGui.Button("清空筛选"))
                                    filter = string.Empty;
                                ImGui.SameLine();
                                if (ImGui.Button("重置列宽"))
                                {
                                    config.TableLayoutSeed++;
                                    changed = true;
                                }

                                if (changed) config.Save();
                                ImGui.EndPopup();
                            }
                            ImGui.PopStyleColor(popPopup);

                            var style = ImGui.GetStyle();
                            var pad = style.FramePadding.X * 2f;
                            var spacing = style.ItemSpacing.X;
                            var nameLabel = config.HideName ? "姓名(隐)" : "姓名(显)";
                            var groupWidth =
                                (ImGui.CalcTextSize("窗口").X + pad) + spacing +
                                (ImGui.CalcTextSize(nameLabel).X + pad) + spacing +
                                (ImGui.CalcTextSize("设置").X + pad) + spacing +
                                (ImGui.CalcTextSize("调试").X + pad);
                            var targetX = ImGui.GetWindowSize().X - groupWidth - style.WindowPadding.X;
                            if (targetX > ImGui.GetCursorPosX())
                                ImGui.SameLine(targetX);

                            if (ImGui.Button("窗口"))
                                ImGui.OpenPopup("##windowOptions");
                            popPopup = PushReadablePopupStyle();
                            if (ImGui.BeginPopup("##windowOptions"))
                            {
                                var changed = false;
                                changed |= ImGui.Checkbox("鼠标穿透（按住 Alt 临时操作）", ref config.ClickThrough);
                                changed |= ImGui.SliderInt("背景透明度（不影响文字）", ref config.BGColor, 0, 100);
                                if (changed) config.Save();
                                ImGui.Separator();
                                ImGui.TextDisabled("提示：开启鼠标穿透后窗口将不响应点击，按住 Alt 可临时操作。");
                                ImGui.EndPopup();
                            }
                            ImGui.PopStyleColor(popPopup);

                            ImGui.SameLine();
                            if (ImGui.Button(nameLabel))
                            {
                                config.HideName = !config.HideName;
                                config.Save();
                            }
                            if (ImGui.IsItemHovered()) ImGui.SetTooltip(config.HideName ? "当前隐藏姓名" : "当前显示姓名");
                            ImGui.SameLine();
                            if (ImGui.Button("设置")) instance?.configWindow.IsOpen = true;
                            ImGui.SameLine();
                            if (ImGui.Button("调试")) instance?.debugWindow.IsOpen = true;
                        }
                        ImGui.EndMenuBar();
                    }

                    //if (!config.SaveData) 
                    if (config.ShowVerticalList)
                        DrawData(battle);
                    //else DrawDataWithCalc(battle);
                }
            }
            finally
            {
                ImGui.PopStyleColor(popStyle);
            }
            }
        }



        private void CheckSave(Dictionary<uint, long> dmgList)
        {
            if (_plugin.Battles.Count < 1) return;
            var battle = _plugin.Battles[^1];
            var now = DateTimeOffset.Now.ToUnixTimeSeconds();

            if (startTime != battle.StartTime && battle.StartTime != 0)
                //新的战斗
            {
                savedBattle.Clear();
                savedBattle.Add(dmgList);
                startTime = battle.StartTime;
                lastTime = startTime;
            }
            else
            {
                if (savedBattle.Count == 0 || now - lastTime > 0) //过了1秒
                {
                    savedBattle.Add(dmgList);
                    lastTime = now;
                }
            }

            while (savedBattle.Count > config.SaveTime + 2) //删除不必要的数据
            {
                savedBattle.RemoveAt(0);
            }
        }

        private void DrawData(ACTBattle battle)
        {
            var seconds = battle.Duration();
            var canSimDots = battle.Level is >= 64 && !float.IsInfinity(battle.TotalDotSim) && battle.TotalDotSim != 0;
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

            var localPlayerId = DalamudApi.ClientState.LocalPlayer?.EntityId ?? 0;
            var filterText = filter.Trim();
            var rows = new List<(uint Actor, uint JobId, long Damage, uint Death, float D, float C, float DC)>(battle.DataDic.Count);
            long actorDamageTotal = 0;
            foreach (var (actor, damage) in battle.DataDic)
            {
                var totalDamage = damage.Damages.TryGetValue(0, out var dmg) ? dmg.Damage : 0;
                if (dotByActor != null && dotByActor.TryGetValue(actor, out var dotDamage))
                    totalDamage += (long)dotDamage;

                actorDamageTotal += totalDamage;

                var swings = damage.Damages.TryGetValue(0, out var baseDamage) ? baseDamage.swings : 0;
                var dRate = swings == 0 ? -1f : (float)baseDamage.D / swings;
                var cRate = swings == 0 ? -1f : (float)baseDamage.C / swings;
                var dcRate = swings == 0 ? -1f : (float)baseDamage.DC / swings;
                rows.Add((actor, damage.JobId, totalDamage, damage.Death, dRate, cRate, dcRate));
            }

            if (!string.IsNullOrWhiteSpace(filterText))
            {
                rows = rows.Where(r =>
                {
                    var name = battle.Name.GetValueOrDefault(r.Actor, JobNameCn(r.JobId));
                    if (name.Contains(filterText, StringComparison.OrdinalIgnoreCase)) return true;
                    if (JobNameCn(r.JobId).Contains(filterText, StringComparison.OrdinalIgnoreCase)) return true;
                    if (((Job)r.JobId).ToString().Contains(filterText, StringComparison.OrdinalIgnoreCase)) return true;
                    return r.Actor.ToString("X").Contains(filterText, StringComparison.OrdinalIgnoreCase);
                }).ToList();
            }

            rows.Sort((a, b) =>
            {
                return config.SortMode switch
                {
                    2 => StringComparer.CurrentCulture.Compare(
                        battle.Name.GetValueOrDefault(a.Actor, JobNameCn(a.JobId)),
                        battle.Name.GetValueOrDefault(b.Actor, JobNameCn(b.JobId))),
                    1 => b.Damage.CompareTo(a.Damage),
                    _ => ((float)b.Damage / seconds).CompareTo((float)a.Damage / seconds),
                };
            });

            var rankByActor = new Dictionary<uint, int>(rows.Count);
            for (var i = 0; i < rows.Count; i++)
                rankByActor[rows[i].Actor] = i + 1;

            var localPlayerRank = localPlayerId != 0 && rankByActor.TryGetValue(localPlayerId, out var lpr) ? lpr : 0;

            var showingPinnedSelf = false;
            if (config.TopN > 0 && rows.Count > config.TopN)
            {
                if (localPlayerRank > config.TopN && string.IsNullOrWhiteSpace(filterText))
                {
                    var othersCount = Math.Max(0, config.TopN - 1);
                    var selfRow = rows[localPlayerRank - 1];
                    rows = rows.Take(othersCount).Append(selfRow).ToList();
                    showingPinnedSelf = true;
                }
                else
                {
                    rows = rows.Take(config.TopN).ToList();
                }
            }

            var compact = config.AutoCompact
                ? ImGui.GetContentRegionAvail().X < 520
                : config.CompactMode;
            var showRates = config.ShowRates && !compact;

            var maxDps = rows.Count == 0 ? 0 : rows.Max(r => (float)r.Damage / seconds);

            var limitDamage = battle.LimitBreak.Count > 0 ? battle.LimitBreak.Values.Sum() : 0L;
            var totalDamageAll = actorDamageTotal;
            if (battle.TotalDotDamage != 0 && !canSimDots)
                totalDamageAll += battle.TotalDotDamage;
            if (limitDamage > 0)
                totalDamageAll += limitDamage;

            var totalDps = (float)totalDamageAll / seconds;
            ImGui.Text($"总秒伤：{totalDps:F1}    总伤害：{totalDamageAll:N0}    参与：{battle.DataDic.Count}");
            if (!string.IsNullOrWhiteSpace(filterText) || config.TopN > 0)
            {
                var showFilter = string.IsNullOrWhiteSpace(filterText) ? "（无）" : filterText;
                var showTopN = config.TopN > 0
                    ? (showingPinnedSelf ? (config.TopN <= 1 ? "自己" : $"前{config.TopN - 1}+自己") : $"前{config.TopN}")
                    : "全部";
                ImGui.TextDisabled($"当前视图：{showTopN}，筛选：{showFilter}");
            }

            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(6f, 4f));
            var columnCount = showRates ? 7 : 4;
            ImGui.BeginTable($"ACTMainWindow##{config.TableLayoutSeed}", columnCount,
                ImGuiTableFlags.Hideable | ImGuiTableFlags.Resizable |
                ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX |
                ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders |
                ImGuiTableFlags.SizingStretchProp);
            {
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableSetupColumn("###Icon",
                    ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHide |
                    ImGuiTableColumnFlags.NoDirectResize,
                    ImGui.GetTextLineHeight());
                ImGui.TableSetupColumn("姓名", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoHide, 1f);
                ImGui.TableSetupColumn("死亡", ImGuiTableColumnFlags.WidthFixed, 45f);
                if (showRates)
                {
                    ImGui.TableSetupColumn("直击", ImGuiTableColumnFlags.WidthFixed, 55f);
                    ImGui.TableSetupColumn("暴击", ImGuiTableColumnFlags.WidthFixed, 55f);
                    ImGui.TableSetupColumn("直爆", ImGuiTableColumnFlags.WidthFixed, 55f);
                }

                ImGui.TableSetupColumn("秒伤", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHide, 110f);
                ImGui.TableHeadersRow();

                foreach (var r in rows)
                {
                    var rank = rankByActor.TryGetValue(r.Actor, out var realRank) ? realRank : 0;
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    if (_plugin.Icon.TryGetValue(r.JobId, out var icon) && icon != null)
                        ImGui.Image(icon.Handle,
                            new Vector2(ImGui.GetTextLineHeight(), ImGui.GetTextLineHeight()));

                    ImGui.TableNextColumn();
                    var displayName = config.HideName
                        ? JobNameCn(r.JobId)
                        : battle.Name.GetValueOrDefault(r.Actor, JobNameCn(r.JobId));

                    if (config.HighlightSelf && r.Actor == localPlayerId)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.85f, 0.2f, 1f));
                        ImGui.Text($"{rank}. {displayName}");
                        ImGui.PopStyleColor();
                    }
                    else
                    {
                        ImGui.Text($"{rank}. {displayName}");
                    }

                    ImGui.TableNextColumn();
                    ImGui.Text(r.Death.ToString("D"));

                    if (showRates)
                    {
                        ImGui.TableNextColumn();
                        ImGui.Text(r.D < 0 ? "-" : r.D.ToString("P1"));
                        ImGui.TableNextColumn();
                        ImGui.Text(r.C < 0 ? "-" : r.C.ToString("P1"));
                        ImGui.TableNextColumn();
                        ImGui.Text(r.DC < 0 ? "-" : r.DC.ToString("P1"));
                    }

                    ImGui.TableNextColumn();
                    var dps = (float)r.Damage / seconds;
                    var frac = maxDps <= 0 ? 0 : dps / maxDps;
                    if (frac < 0) frac = 0;
                    if (frac > 1) frac = 1;

                    ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
                    var barColor = (config.HighlightSelf && r.Actor == localPlayerId)
                        ? new Vector4(0.3f, 0.9f, 0.3f, 0.9f)
                        : new Vector4(0.25f, 0.65f, 1f, 0.9f);
                    ImGui.PushStyleColor(ImGuiCol.PlotHistogram, barColor);
                    ImGui.ProgressBar(frac, new Vector2(-1, ImGui.GetTextLineHeight()), $"{dps:F1}");
                    ImGui.PopStyleColor();
                    ImGui.PopStyleVar();
                    if (ImGui.IsItemHovered()) DrawDetails(r.Actor);
                }
                if (battle.TotalDotDamage != 0 && !canSimDots) //Dot damage
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.Text("持续伤害");
                    ImGui.TableNextColumn();
                    if (showRates)
                    {
                        ImGui.TableNextColumn();
                        ImGui.TableNextColumn();
                        ImGui.TableNextColumn();
                    }
                    ImGui.TableNextColumn();
                    ImGui.Text($"{(float)battle.TotalDotDamage / battle.Duration():F1}");
                }

                if (limitDamage > 0)
                {
                    ImGui.TableNextRow(); //LimitBreak
                    ImGui.TableNextColumn();
                    if (_plugin.Icon.TryGetValue(99, out var icon) && icon != null)
                        ImGui.Image(icon.Handle,
                            new Vector2(ImGui.GetTextLineHeight(), ImGui.GetTextLineHeight()));
                    ImGui.TableNextColumn();
                    ImGui.Text("极限技");
                    ImGui.TableNextColumn();
                    if (showRates)
                    {
                        ImGui.TableNextColumn();
                        ImGui.TableNextColumn();
                        ImGui.TableNextColumn();
                    }
                    ImGui.TableNextColumn();
                    ImGui.Text($"{(float)limitDamage / seconds:F1}");
                    if (ImGui.IsItemHovered()) DrawLimitBreak();
                }

                if (canSimDots && battle.TotalDotDamage != 0 && config.delta) //Dot Simulation
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.Text("DoT 模拟偏差");
                    ImGui.TableNextColumn();
                    if (showRates)
                    {
                        ImGui.TableNextColumn();
                        ImGui.TableNextColumn();
                        ImGui.TableNextColumn();
                    }
                    ImGui.TableNextColumn();
                    ImGui.Text($"{battle.TotalDotSim * 100 / battle.TotalDotDamage - 100:F2}%");
                }
            }
            ImGui.EndTable();
            ImGui.PopStyleVar();
        }

        private void DrawDataCardsRow(
            ACTBattle battle,
            List<(uint Actor, uint JobId, long Damage, uint Death, float D, float C, float DC)> rows,
            Dictionary<uint, int> rankByActor,
            uint localPlayerId,
            float seconds,
            float maxDps,
            bool compact,
            bool canSimDots,
            long limitDamage)
        {
            var style = ImGui.GetStyle();
            var lineHeight = ImGui.GetTextLineHeight();
            var cardWidth = compact ? 210f : 260f;
            var cardHeight = compact ? (lineHeight * 3.4f) : (lineHeight * 3.9f);

            ImGui.BeginChild($"ACTCards##{config.TableLayoutSeed}", new Vector2(0, cardHeight + style.WindowPadding.Y * 2), false,
                ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            try
            {
                for (var i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    var rank = rankByActor.TryGetValue(r.Actor, out var realRank) ? realRank : 0;

                    ImGui.PushID((int)r.Actor);
                    var isSelf = config.HighlightSelf && r.Actor == localPlayerId;
                    ImGui.BeginChild("##card", new Vector2(cardWidth, cardHeight), true,
                        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
                    try
                    {
                        var cardStartX = ImGui.GetCursorPosX();

                        var iconSize = lineHeight * 1.3f;
                        if (_plugin.Icon.TryGetValue(r.JobId, out var icon) && icon != null)
                            ImGui.Image(icon.Handle, new Vector2(iconSize, iconSize));
                        else
                            ImGui.Dummy(new Vector2(iconSize, iconSize));

                        ImGui.SameLine();
                        ImGui.BeginGroup();
                        var displayName = config.HideName
                            ? JobNameCn(r.JobId)
                            : battle.Name.GetValueOrDefault(r.Actor, JobNameCn(r.JobId));

                        ImGui.Text($"{rank}. {displayName}");

                        ImGui.TextDisabled($"死亡 {r.Death:D}");
                        ImGui.EndGroup();

                        var afterHeaderY = ImGui.GetCursorPosY();
                        ImGui.SetCursorPosX(cardStartX);
                        ImGui.SetCursorPosY(afterHeaderY);

                        var dps = seconds <= 0 ? 0 : (float)r.Damage / seconds;
                        var frac = maxDps <= 0 ? 0 : dps / maxDps;
                        if (frac < 0) frac = 0;
                        if (frac > 1) frac = 1;

                        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
                        var barColor = isSelf
                            ? new Vector4(0.3f, 0.9f, 0.3f, 0.9f)
                            : new Vector4(0.25f, 0.65f, 1f, 0.9f);
                        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, barColor);
                        ImGui.ProgressBar(frac, new Vector2(-1, lineHeight), $"{dps:F1}");
                        ImGui.PopStyleColor();
                        ImGui.PopStyleVar();
                    }
                    finally
                    {
                        ImGui.EndChild();
                    }

                    if (ImGui.IsItemHovered()) DrawDetails(r.Actor);
                    ImGui.PopID();

                    if (i != rows.Count - 1)
                        ImGui.SameLine();
                }
            }
            finally
            {
                ImGui.EndChild();
            }

            if (battle.TotalDotDamage != 0 && !canSimDots)
                ImGui.TextDisabled($"DoT 伤害：{(float)battle.TotalDotDamage / battle.Duration():F1}");

            if (limitDamage > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"极限技：{(float)limitDamage / seconds:F1}");
                if (ImGui.IsItemHovered()) DrawLimitBreak();
            }

            if (canSimDots && battle.TotalDotDamage != 0 && config.delta)
                ImGui.TextDisabled($"DoT 模拟偏差：{battle.TotalDotSim * 100 / battle.TotalDotDamage - 100:F2}%");
        }

        private void DrawDataWithCalc(ACTBattle battle)
        {
            if (!config.SaveData)
            {
                DrawData(battle);
                return;
            }

            long total = 0;
            Dictionary<uint, long> dmgList = new();
            var seconds = battle.Duration();
            var index = Math.Max(1, Math.Min(savedBattle.Count, config.CalcTime + 1) - 1);

            foreach (var (actor, damage) in battle.DataDic)
            {
                dmgList.Add(actor, damage.Damages[0].Damage);
                if (float.IsInfinity(battle.TotalDotSim) || battle.TotalDotSim == 0 || battle.Level < 64) continue;
                var dotDamage = (from entry in battle.DotDmgList where (entry.Key & 0xFFFFFFFF) == actor select entry.Value).Sum();
                dmgList[actor] += (long)dotDamage;
            }

            dmgList = (from entry in dmgList orderby entry.Value descending select entry).ToDictionary(x => x.Key, x => x.Value);

            CheckSave(dmgList);
            if (savedBattle.Count < 2) return;

            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(6f, 4f));
            ImGui.BeginTable($"ACTMainWindow##{config.TableLayoutSeed}", 8,
                ImGuiTableFlags.Hideable | ImGuiTableFlags.Resizable |
                ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX |
                ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders |
                ImGuiTableFlags.SizingStretchProp);
            {
                ImGui.TableSetupColumn("###Icon",
                    ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHide |
                    ImGuiTableColumnFlags.NoDirectResize,
                    ImGui.GetTextLineHeight());
                var headers = new string[]
                    {"姓名", "死亡", "直击", "暴击", "直爆"};
                foreach (var t in headers) ImGui.TableSetupColumn(t);

                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (45f - ImGui.CalcTextSize("伤害").X) / 2);
                ImGui.TableSetupColumn("秒伤", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHide, 90f);
                ImGui.TableSetupColumn("计算伤害", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHide, 80f);
                ImGui.TableHeadersRow();
                
                foreach (var (actor, value) in dmgList)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    if (_plugin.Icon.TryGetValue(battle.DataDic[actor].JobId, out var icon) && icon != null)
                        ImGui.Image(icon.Handle,
                            new Vector2(ImGui.GetTextLineHeight(), ImGui.GetTextLineHeight()));

                    ImGui.TableNextColumn();
                    ImGui.Text(config.HideName
                        ? JobNameCn(battle.DataDic[actor].JobId)
                        : battle.Name.GetValueOrDefault(actor, JobNameCn(battle.DataDic[actor].JobId)));
                    ImGui.TableNextColumn();
                    ImGui.Text(battle.DataDic[actor].Death.ToString("D"));
                    ImGui.TableNextColumn();
                    ImGui.Text(battle.DataDic[actor].Damages[0].swings == 0
                        ? "-"
                        : ((float)battle.DataDic[actor].Damages[0].D / battle.DataDic[actor].Damages[0].swings).ToString("P1"));
                    ImGui.TableNextColumn();
                    ImGui.Text(battle.DataDic[actor].Damages[0].swings == 0
                        ? "-"
                        : ((float)battle.DataDic[actor].Damages[0].C / battle.DataDic[actor].Damages[0].swings).ToString("P1"));
                    ImGui.TableNextColumn();
                    ImGui.Text(battle.DataDic[actor].Damages[0].swings == 0
                        ? "-"
                        : ((float)battle.DataDic[actor].Damages[0].DC / battle.DataDic[actor].Damages[0].swings).ToString("P1"));
                    ImGui.TableNextColumn();
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetColumnWidth() -
                                        ImGui.CalcTextSize($"{(float)value / seconds,8:F1}").X);
                    ImGui.Text($"{(float)value / seconds,8:F1}");
                    if (ImGui.IsItemHovered()) DrawDetails(actor);

                    ImGui.TableNextColumn();
                    savedBattle[index].TryGetValue(actor, out var later);
                    savedBattle[0].TryGetValue(actor, out var first);
                    var data = ((float)later - first) / (index);

                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetColumnWidth() -
                                        ImGui.CalcTextSize($"{data,8:F1}").X);
                    ImGui.Text($"{data,8:F1}");

                    total += value;
                }
                if (battle.TotalDotDamage != 0 && (float.IsInfinity(battle.TotalDotSim) || battle.TotalDotSim == 0 || battle.Level < 64)) //Dot damage
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.Text("持续伤害");
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetColumnWidth() -
                    ImGui.CalcTextSize(
                    $"{(float)battle.TotalDotDamage / battle.Duration(),8:F1}")
                                            .X);
                    ImGui.Text(
                    $"{(float)battle.TotalDotDamage / battle.Duration(),8:F1}");
                    total += battle.TotalDotDamage;
                    ImGui.TableNextColumn();
                }

                if (battle.LimitBreak.Count > 0)
                {
                    long limitDamage = 0;
                    foreach (var (skill, damage) in battle.LimitBreak)
                    {
                        limitDamage += damage;
                    }
                    ImGui.TableNextRow(); //LimitBreak
                    ImGui.TableNextColumn();
                    if (_plugin.Icon.TryGetValue(99, out var icon))
                        ImGui.Image(icon!.Handle,
                            new Vector2(ImGui.GetTextLineHeight(), ImGui.GetTextLineHeight()));
                    ImGui.TableNextColumn();
                    ImGui.Text("极限技");
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetColumnWidth() -
                                        ImGui.CalcTextSize($"{(float)limitDamage / seconds,8:F1}").X);
                    ImGui.Text($"{(float)limitDamage / seconds,8:F1}");
                    if (ImGui.IsItemHovered()) DrawLimitBreak();
                    ImGui.TableNextColumn();
                    total += limitDamage;
                }

                ImGui.TableNextRow(); //Total Damage
                ImGui.TableNextColumn();
                ImGui.TableNextColumn();
                ImGui.Text("总计");
                ImGui.TableNextColumn();
                ImGui.TableNextColumn();
                ImGui.TableNextColumn();
                ImGui.TableNextColumn();
                ImGui.TableNextColumn();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetColumnWidth() -
                                    ImGui.CalcTextSize($"{(float)total / seconds,8:F1}").X);
                ImGui.Text($"{(float)total / seconds,8:F1}");
                ImGui.TableNextColumn();

                if (!float.IsInfinity(battle.TotalDotSim) && battle.TotalDotSim != 0 && config.delta && battle.TotalDotDamage != 0) //Dot Simulation
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.Text("Δ");
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.Text($"{battle.TotalDotSim * 100 / battle.TotalDotDamage - 100:F2}%");
                    ImGui.TableNextColumn();
                }
            }
            ImGui.EndTable();
            ImGui.PopStyleVar();
        }


        internal void DrawDetails(uint actor)
        {
            lock (_plugin.SyncRoot)
            {
                if (_plugin.Battles.Count < 1) return;
                choosed = Math.Clamp(choosed, 0, _plugin.Battles.Count - 1);

                ImGui.BeginTooltip();
            ImGui.BeginTable("详情", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg);

            ImGui.TableSetupColumn("###Icon");
            ImGui.TableSetupColumn("###SkillName");
            ImGui.TableSetupColumn("直击");
            ImGui.TableSetupColumn("暴击");
            ImGui.TableSetupColumn("直爆");
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (45f - ImGui.CalcTextSize("伤害").X) / 2);
            ImGui.TableSetupColumn("秒伤", ImGuiTableColumnFlags.WidthFixed, 60f);
            ImGui.TableHeadersRow();

            var battle = _plugin.Battles[choosed];

            var damage = battle.DataDic[actor].Damages.ToList();
            damage.Sort((pair1, pair2) => pair2.Value.Damage.CompareTo(pair1.Value.Damage));
            foreach (var (action, dmg) in damage)
            {
                if (action == 0 || !sheet.TryGetRow(action, out var actionRow)) continue;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                EnsureActionIcon(action);
                if (Icon.TryGetValue(action, out var icon) && icon != null)
                    ImGui.Image(icon.Handle,
                        new Vector2(ImGui.GetTextLineHeight(), ImGui.GetTextLineHeight()));
                ImGui.TableNextColumn();
                ImGui.Text(actionRow.Name.ExtractText());
                ImGui.TableNextColumn();
                ImGui.Text(dmg.swings == 0 ? "-" : ((float)dmg.D / dmg.swings).ToString("P1"));
                ImGui.TableNextColumn();
                ImGui.Text(dmg.swings == 0 ? "-" : ((float)dmg.C / dmg.swings).ToString("P1"));
                ImGui.TableNextColumn();
                ImGui.Text(dmg.swings == 0 ? "-" : ((float)dmg.DC / dmg.swings).ToString("P1"));
                ImGui.TableNextColumn();
                var temp = (float)dmg.Damage / battle.Duration();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetColumnWidth() -
                                    ImGui.CalcTextSize($"{temp,8:F1}").X);
                ImGui.Text($"{temp,8:F1}");
            }


            if (!float.IsInfinity(battle.TotalDotSim) && battle.TotalDotSim != 0)
            {
                ImGui.TableNextRow();
                var dots = (from dot in battle.DotDmgList where (dot.Key & 0xFFFFFFFF) == actor select dot).ToList();
                foreach (var (active, dotDmg) in dots)
                {
                    var buff = (uint)(active >> 32);
                    var source = (uint)(active & 0xFFFFFFFF);
                    EnsureBuffIcon(buff);

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    if (BuffIcon.TryGetValue(buff, out var buffIcon) && buffIcon != null)
                        ImGui.Image(buffIcon.Handle,
                            new Vector2(ImGui.GetTextLineHeight(), ImGui.GetTextLineHeight() * 1.2f));
                    ImGui.TableNextColumn();
                    ImGui.Text($"{(buffSheet.TryGetRow(buff, out var buffRow) ? buffRow.Name.ExtractText() : buff.ToString())}");
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    var temp = dotDmg / battle.Duration();
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetColumnWidth() -
                                        ImGui.CalcTextSize($"{temp,8:F1}").X);
                    ImGui.Text($"{temp,8:F1}");
                }
            }

            ImGui.EndTable();
            if (battle.DataDic[actor].MaxDamageSkill != 0)
            {
                ImGui.Text($"最大伤害：{sheet.GetRow(battle.DataDic[actor].MaxDamageSkill).Name.ExtractText()} - {battle.DataDic[actor].MaxDamage:N0}");
            }
                ImGui.EndTooltip();
            }
        }

        private void DrawLimitBreak()
        {
            ImGui.BeginTooltip();
            ImGui.BeginTable("极限技", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg);

            ImGui.TableSetupColumn("###Icon");
            ImGui.TableSetupColumn("###SkillName");
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (45f - ImGui.CalcTextSize("伤害").X) / 2);
            ImGui.TableSetupColumn("秒伤", ImGuiTableColumnFlags.WidthFixed, 60f);
            ImGui.TableHeadersRow();

            var damage = _plugin.Battles[choosed].LimitBreak.ToList();
            damage.Sort((pair1, pair2) => pair2.Value.CompareTo(pair1.Value));
            foreach (var (action, dmg) in damage)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (_plugin.Icon.TryGetValue(99, out var icon))
                    ImGui.Image(icon!.Handle,
                        new Vector2(ImGui.GetTextLineHeight(), ImGui.GetTextLineHeight()));
                ImGui.TableNextColumn();
                ImGui.Text(sheet.GetRow(action).Name.ExtractText());
                ImGui.TableNextColumn();
                var temp = (float)dmg / _plugin.Battles[choosed].Duration();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetColumnWidth() -
                                    ImGui.CalcTextSize($"{temp,8:F1}").X);
                ImGui.Text($"{temp,8:F1}");
            }

            ImGui.EndTable();
            ImGui.EndTooltip();
        }

        private void DrawMini(bool forced)
        {
            if (!config.Mini && !forced) return;

            var clickThroughActive = config.ClickThrough && !ImGui.GetIO().KeyAlt;
            var flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar |
                        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
                        (config.NoResize ? ImGuiWindowFlags.NoResize : ImGuiWindowFlags.None) |
                        (clickThroughActive ? ImGuiWindowFlags.NoInputs : ImGuiWindowFlags.None);
            if (forced)
                flags |= ImGuiWindowFlags.NoBackground;

            Flags = flags;
            BgAlpha = Math.Clamp(config.BGColor / 100f, 0f, 1f);

            var popStyle = PushBackgroundAlpha(config.BGColor / 100f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4f, 4f));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2f, 2f));
            var clicked = ImGui.ImageButton(mainIcon.Handle, new Vector2(40f));
            if (!clickThroughActive)
            {
                if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                    draggingMiniIcon = true;

                if (draggingMiniIcon)
                {
                    if (ImGui.IsMouseDown(ImGuiMouseButton.Right))
                    {
                        ImGui.SetWindowPos(ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta);
                    }
                    else
                    {
                        draggingMiniIcon = false;
                        config.MiniWindowPos = ImGui.GetWindowPos();
                        config.HasMiniWindowPos = true;
                        config.Save();
                    }
                }
            }
            if (clicked && !forced)
            {
                config.MiniWindowPos = ImGui.GetWindowPos();
                config.HasMiniWindowPos = true;
                config.Mini = false;
                config.ShowVerticalList = true;
                config.Save();
                var restoreSize = config.WindowSize;
                if (restoreSize.X < 200 || restoreSize.Y < 120)
                    restoreSize = new Vector2(480, 320);
                pendingRestore = true;
                pendingRestoreSize = restoreSize;
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(popStyle);

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(forced ? "最小化图标（右键拖动）" : "左键显示纵向列表，右键拖动图标");
        }

        public void Dispose()
        {

        }
    }

}
