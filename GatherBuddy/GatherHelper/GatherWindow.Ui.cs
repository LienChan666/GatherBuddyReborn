﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using GatherBuddy.AutoGather.Extensions;
using GatherBuddy.Classes;
using GatherBuddy.Config;
using GatherBuddy.Gui;
using GatherBuddy.Interfaces;
using GatherBuddy.Time;
using ImGuiNET;
using OtterGui;
using Functions = GatherBuddy.Plugin.Functions;
using ImRaii = OtterGui.Raii.ImRaii;

namespace GatherBuddy.GatherHelper;

public class GatherWindow : Window
{
    private readonly GatherBuddy _plugin;

    private int       _deleteSet              = -1;
    private int       _deleteItemIdx          = -1;
    private bool      _deleteAutoGather;
    private TimeStamp _earliestKeyboardToggle = TimeStamp.Epoch;
    private Vector2   _lastSize               = Vector2.Zero;
    private Vector2   _newPosition            = Vector2.Zero;

    public GatherWindow(GatherBuddy plugin)
        : base("##GatherHelperReborn",
            ImGuiWindowFlags.AlwaysAutoResize
          | ImGuiWindowFlags.NoTitleBar
          | ImGuiWindowFlags.NoFocusOnAppearing
          | ImGuiWindowFlags.NoNavFocus
          | ImGuiWindowFlags.NoScrollbar)
    {
        _plugin            = plugin;
        IsOpen             = GatherBuddy.Config.ShowGatherWindow;
        RespectCloseHotkey = false;
        Namespace          = "GatherHelperReborn";
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = Vector2.Zero,
            MaximumSize = Vector2.One * 10000,
        };
    }

    private static void DrawTime(ILocation? loc, TimeInterval time)
    {
        if (!GatherBuddy.Config.ShowGatherWindowTimers || !ImGui.TableNextColumn())
            return;
        if (time.Equals(TimeInterval.Always))
            return;

        if (time.Start > GatherBuddy.Time.ServerTime)
        {
            using var color = ImRaii.PushColor(ImGuiCol.Text, ColorId.UpcomingItem.Value());
            ImGui.Text(TimeInterval.DurationString(time.Start, GatherBuddy.Time.ServerTime, false));
        }
        else
        {
            using var color = ImRaii.PushColor(ImGuiCol.Text, ColorId.AvailableItem.Value());
            ImGui.Text(TimeInterval.DurationString(time.End, GatherBuddy.Time.ServerTime, false));
        }

        CreateTooltip(null, loc, time);
    }

    private static string TooltipText(ILocation? loc, TimeInterval time)
    {
        var sb = new StringBuilder();
        sb.Append(loc == null
            ? "Unknown Location\nUnknown Territory\nUnknown Aetheryte\n"
            : $"{loc.Name}\n{loc.Territory.Name}\n{loc.ClosestAetheryte?.Name ?? "No Aetheryte"}\n");

        sb.Append(time.Equals(TimeInterval.Always)
            ? "Always Up"
            : $"{time.Start}\n{time.End}\n{time.DurationString()}\n{TimeInterval.DurationString(time.Start > GatherBuddy.Time.ServerTime ? time.Start : time.End, GatherBuddy.Time.ServerTime, false)}");

        return sb.ToString();
    }

    private static void CreateTooltip(IGatherable? item, ILocation? loc, TimeInterval time)
    {
        if (!ImGui.IsItemHovered())
            return;

        if (item is not Fish fish)
        {
            ImGui.SetTooltip(TooltipText(loc, time));
            return;
        }

        var extendedFish = Interface.ExtendedFishList.FirstOrDefault(f => f.Data == fish);
        if (extendedFish == null)
        {
            ImGui.SetTooltip(TooltipText(loc, time));
            return;
        }

        using var tt = ImRaii.Tooltip();

        ImGui.Text(TooltipText(loc, time));
        ImGui.NewLine();
        extendedFish.SetTooltip(loc?.Territory ?? Territory.Invalid, ImGuiHelpers.ScaledVector2(40, 40), ImGuiHelpers.ScaledVector2(20, 20),
            ImGuiHelpers.ScaledVector2(30,                                                          30),
            false);
    }

    private readonly List<(IGatherable Item, ILocation Location, TimeInterval Uptime, uint Quantity)> _data = new();

    private void DrawItem(IGatherable item, ILocation loc, TimeInterval time, uint quantity)
    {
        if (GatherBuddy.Config.ShowGatherWindowOnlyAvailable && time.Start > GatherBuddy.Time.ServerTime)
            return;

        var inventoryCount = item.GetInventoryCount();

        if (quantity > 0 && inventoryCount >= quantity && GatherBuddy.Config.HideGatherWindowCompletedItems)
            return;

        if (ImGui.TableNextColumn())
        {
            using var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, ImGui.GetStyle().ItemSpacing / 2);
            if (Icons.DefaultStorage.TryLoadIcon(item.ItemData.Icon, out var icon))
                ImGuiUtil.HoverIcon(icon.ImGuiHandle, icon.Size, new Vector2(ImGui.GetTextLineHeight()));
            else
                ImGui.Dummy(new Vector2(ImGui.GetTextLineHeight()));
            ImGui.SameLine();
            var colorId = time == TimeInterval.Always    ? ColorId.GatherWindowText :
                time.Start > GatherBuddy.Time.ServerTime ? ColorId.GatherWindowUpcoming : ColorId.GatherWindowAvailable;

            if (quantity > 0 && inventoryCount >= quantity)
                colorId = ColorId.DisabledText;
            using var color                         = ImRaii.PushColor(ImGuiCol.Text, colorId.Value());
            var quantityText = quantity > 0 ? $" ({inventoryCount}/{quantity})" : "";
            if (ImGui.Selectable($"{item.Name[GatherBuddy.Language]}{quantityText}", false))
            {
                if (_plugin.Executor.LastItem != item)
                    _plugin.Executor.GatherItem(item);
                else if (item is Gatherable)
                    _plugin.Executor.GatherItemByName("next");
                else
                    _plugin.Executor.GatherFishByName("next");
            }

            var clicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);
            color.Pop();
            CreateTooltip(item, loc, time);

            if (clicked && Functions.CheckModifier(GatherBuddy.Config.GatherWindowDeleteModifier, false))
                if (quantity > 0)
                    for (var i = 0; i < _plugin.AutoGatherListsManager.Lists.Count; ++i)
                    {
                        var list = _plugin.AutoGatherListsManager.Lists[i];
                        if (!list.Enabled)
                            continue;

                        if (item is not Gatherable gatherable)
                            continue;
                        var idx = list.Items.IndexOf(gatherable);
                        if (idx < 0)
                            continue;

                        _deleteSet = i;
                        _deleteItemIdx = idx;
                        _deleteAutoGather = true;
                        break;
                    }
                else
                    for (var i = 0; i < _plugin.GatherWindowManager.Presets.Count; ++i)
                    {
                        var preset = _plugin.GatherWindowManager.Presets[i];
                        if (!preset.Enabled)
                            continue;

                        var idx = preset.Items.IndexOf(item);
                        if (idx < 0)
                            continue;

                        _deleteSet = i;
                        _deleteItemIdx = idx;
                        _deleteAutoGather = false;
                        break;
                    }
            else
                Interface.CreateGatherWindowContextMenu(item, clicked);
        }

        DrawTime(loc, time);
    }

    private void DeleteItem()
    {
        if (_deleteSet < 0 || _deleteItemIdx < 0)
            return;

        if (_deleteAutoGather)
        {
            var list = _plugin.AutoGatherListsManager.Lists[_deleteSet];
            _plugin.AutoGatherListsManager.RemoveItem(list, _deleteItemIdx);
        }
        else
        {
            var preset = _plugin.GatherWindowManager.Presets[_deleteSet];
            _plugin.GatherWindowManager.RemoveItem(preset, _deleteItemIdx);
        }
        _deleteSet     = -1;
        _deleteItemIdx = -1;
    }

    private void CheckHotkeys()
    {
        if (_earliestKeyboardToggle > GatherBuddy.Time.ServerTime || !Functions.CheckKeyState(GatherBuddy.Config.GatherWindowHotkey, false))
            return;

        _earliestKeyboardToggle             = GatherBuddy.Time.ServerTime.AddMilliseconds(500);
        GatherBuddy.Config.ShowGatherWindow = !GatherBuddy.Config.ShowGatherWindow;
        GatherBuddy.Config.Save();
    }

    private static bool CheckHoldKey()
    {
        if (!GatherBuddy.Config.OnlyShowGatherWindowHoldingKey || GatherBuddy.Config.GatherWindowHoldKey == VirtualKey.NO_KEY)
            return false;

        return !Dalamud.Keys[GatherBuddy.Config.GatherWindowHoldKey];
    }

    private static bool CheckDuty()
        => GatherBuddy.Config.HideGatherWindowInDuty && Functions.BoundByDuty();

    private bool CheckAvailable()
    {
        _data.Clear();

        var list = _plugin.AutoGatherListsManager.ActiveItems
            .Select(x => (Item: x.Item as IGatherable, x.Quantity))
            .Union(_plugin.AutoGatherListsManager.ActiveFish.Select(x => (Item: x.Fish as IGatherable, x.Quantity)))
            .Concat(_plugin.GatherWindowManager.ActiveItems.Select(i => (Item: i, Quantity: 0u)))
            .GroupBy(x => x.Item)
            .Select(g => { var (loc, time) = GatherBuddy.UptimeManager.BestLocation(g.Key); return (g.Key, loc, time, (uint)g.Sum(x => x.Quantity)); });

        if (GatherBuddy.Config.SortGatherWindowByUptime)
            list = list.OrderBy(i => i.time, Comparer<TimeInterval>.Create((x, y) => x.Compare(y)));

        _data.AddRange(list);

        return _data.Count == 0 || GatherBuddy.Config.ShowGatherWindowOnlyAvailable && _data.All(f => f.Uptime.Start > GatherBuddy.Time.ServerTime);
    }

    public override void PreOpenCheck()
    {
        CheckHotkeys();
        IsOpen = GatherBuddy.Config.ShowGatherWindow;
    }

    public override bool DrawConditions()
        => !(CheckHoldKey() || CheckDuty() || CheckAvailable()) && Dalamud.ClientState.IsLoggedIn;

    public override void PreDraw()
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, ColorId.GatherWindowBackground.Value());
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.One * 2 * ImGuiHelpers.GlobalScale);
        if (GatherBuddy.Config.LockGatherWindow)
            Flags |= ImGuiWindowFlags.NoMove;
        else
            Flags &= ~
                ImGuiWindowFlags.NoMove;

        if (_newPosition.Y != 0)
        {
            ImGui.SetNextWindowPos(_newPosition);
            _newPosition = Vector2.Zero;
        }
    }

    public override void PostDraw()
    {
        DeleteItem();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    private void CheckAnchorPosition()
    {
        if (!GatherBuddy.Config.GatherWindowBottomAnchor)
            return;

        // Can not use Y size since a single text row is smaller than the minimal window size
        // for some reason. 50 is arbitrary. Default window size was 32,32 for me.
        if (_lastSize.X < 50 * ImGuiHelpers.GlobalScale)
            _lastSize = ImGui.GetWindowSize();

        var size = ImGui.GetWindowSize();
        if (_lastSize == size)
            return;

        _newPosition   =  ImGui.GetWindowPos();
        _newPosition.Y += _lastSize.Y - size.Y;
        _lastSize      =  size;
    }

    public override void Draw()
    {
        var       colorId = GatherBuddy.AutoGather.Enabled ? ColorId.GatherWindowAvailable.Value() : ColorId.GatherWindowText.Value();
        using var color = ImRaii.PushColor(ImGuiCol.Text, colorId);
        if (ImGui.Selectable($"自动采集: {GatherBuddy.AutoGather.AutoStatus}###toggle-button"))
        {
            GatherBuddy.AutoGather.Enabled = !GatherBuddy.AutoGather.Enabled;
        }
        color.Pop();
        ImGuiUtil.HoverTooltip("点击以启用/禁用自动采集");
        using var table = ImRaii.Table("##table", GatherBuddy.Config.ShowGatherWindowTimers ? 2 : 1);
        if (!table)
            return;
        
        foreach (var (item, loc, time, quantity) in _data)
            DrawItem(item, loc, time, quantity);

        CheckAnchorPosition();
    }
}
