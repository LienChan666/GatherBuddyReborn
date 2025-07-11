﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Game.Text.SeStringHandling;
using GatherBuddy.Alarms;
using GatherBuddy.AutoGather.Lists;
using GatherBuddy.Classes;
using GatherBuddy.Config;
using GatherBuddy.GatherHelper;
using GatherBuddy.Interfaces;
using GatherBuddy.Plugin;
using GatherBuddy.Structs;
using ImGuiNET;
using ImRaii = OtterGui.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class Interface
{
    private const string AutomaticallyGenerated = "从右键菜单中自动生成";

    private void DrawAddAlarm(IGatherable item)
    {
        // Only timed items.
        if (item.InternalLocationId <= 0)
            return;

        var current = _alarmCache.Selector.EnsureCurrent();
        if (ImGui.Selectable("添加至闹钟预设"))
        {
            if (current == null)
            {
                _plugin.AlarmManager.AddGroup(new AlarmGroup()
                {
                    Description = AutomaticallyGenerated,
                    Enabled     = true,
                    Alarms      = new List<Alarm> { new(item) { Enabled = true } },
                });
                current = _alarmCache.Selector.EnsureCurrent();
            }
            else
            {
                _plugin.AlarmManager.AddAlarm(current, new Alarm(item));
            }
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                $"添加 {item.Name[GatherBuddy.Language]} 至 {(current == null ? "一个新的闹钟预设" : CheckUnnamed(current.Name))}");
    }

    private void DrawAddToGatherGroup(IGatherable item)
    {
        var       current = _gatherGroupCache.Selector.EnsureCurrent();
        using var color   = ImRaii.PushColor(ImGuiCol.Text, ColorId.DisabledText.Value(), current == null);
        if (ImGui.Selectable("添加至采集列表") && current != null)
            if (_plugin.GatherGroupManager.ChangeGroupNode(current, current.Nodes.Count, item, null, null, null, false))
                _plugin.GatherGroupManager.Save();

        color.Pop();

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(current == null
                ? "需要任一采集列表被选中"
                : $"添加 {item.Name[GatherBuddy.Language]} 至 {current.Name}");
    }

    private void DrawAddGatherWindow(IGatherable item)
    {
        var current = _gatherWindowCache.Selector.EnsureCurrent();

        if (ImGui.Selectable("添加至采集窗口预设"))
        {
            if (current == null)
                _plugin.GatherWindowManager.AddPreset(new GatherWindowPreset
                {
                    Enabled     = true,
                    Items       = new List<IGatherable> { item },
                    Description = AutomaticallyGenerated,
                });
            else
                _plugin.GatherWindowManager.AddItem(current, item);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                $"添加 {item.Name[GatherBuddy.Language]} 至 {(current == null ? "一个新的采集窗口预设" : CheckUnnamed(current.Name))}");
    }

    private static string TeamCraftAddressEnd(string type, uint id)
    {
        var lang = GatherBuddy.Language switch
        {
            ClientLanguage.English  => "en",
            ClientLanguage.German   => "de",
            ClientLanguage.French   => "fr",
            ClientLanguage.Japanese => "ja",
            (ClientLanguage)4       => "chs",
            _                       => "en",
        };

        return $"db/{lang}/{type}/{id}";
    }

    private static string TeamCraftAddressEnd(FishingSpot s)
        => s.Spearfishing
            ? TeamCraftAddressEnd("spearfishing-spot", s.SpearfishingSpotData!.Value.GatheringPointBase.RowId)
            : TeamCraftAddressEnd("fishing-spot",      s.Id);

    private static string GarlandToolsItemAddress(uint itemId)
        => $"https://www.garlandtools.cn/db/#item/{itemId}";

    private static void DrawOpenInGarlandTools(uint itemId)
    {
        if (itemId == 0)
            return;

        if (!ImGui.Selectable("查询 GarlandTools"))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(GarlandToolsItemAddress(itemId)) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            GatherBuddy.Log.Error($"无法打开 GarlandTools:\n{e.Message}");
        }
    }

    private static void DrawOpenInTeamCraft(uint itemId)
    {
        if (itemId == 0)
            return;

        if (ImGui.Selectable("在 TeamCraft 中打开 (浏览器)"))
            OpenInTeamCraftWeb(TeamCraftAddressEnd("item", itemId));

        if (ImGui.Selectable("在 TeamCraft 中打开 (App)"))
            OpenInTeamCraftLocal(TeamCraftAddressEnd("item", itemId));
    }

    private static void OpenInTeamCraftWeb(string addressEnd)
    {
        Process.Start(new ProcessStartInfo($"https://ffxivteamcraft.com/{addressEnd}")
        {
            UseShellExecute = true,
        });
    }

    private static void OpenInTeamCraftLocal(string addressEnd)
    {
        Task.Run(() =>
        {
            try
            {
                using var request  = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:14500/{addressEnd}");
                using var response = GatherBuddy.HttpClient.Send(request);
            }
            catch
            {
                try
                {
                    if (Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ffxiv-teamcraft")))
                        Process.Start(new ProcessStartInfo($"teamcraft:///{addressEnd}")
                        {
                            UseShellExecute = true,
                        });
                }
                catch
                {
                    GatherBuddy.Log.Error("无法打开本地 teamcraft 程序");
                }
            }
        });
    }

    private static void DrawOpenInTeamCraft(FishingSpot fs)
    {
        if (fs.Id == 0)
            return;

        if (ImGui.Selectable("在 TeamCraft 中打开 (浏览器)"))
            OpenInTeamCraftWeb(TeamCraftAddressEnd(fs));

        if (ImGui.Selectable("在 TeamCraft 中打开 (App)"))
            OpenInTeamCraftLocal(TeamCraftAddressEnd(fs));
    }

    public void CreateContextMenu(IGatherable item)
    {
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup(item.Name[GatherBuddy.Language]);

        using var popup = ImRaii.Popup(item.Name[GatherBuddy.Language]);
        if (!popup)
            return;

        DrawAddAlarm(item);
        DrawAddToGatherGroup(item);
        DrawAddGatherWindow(item);
        DrawAddToAutoGather(item);
        if (ImGui.Selectable("创建物品链接"))
            Communicator.Print(SeString.CreateItemLink(item.ItemId));
        DrawOpenInGarlandTools(item.ItemId);
        DrawOpenInTeamCraft(item.ItemId);
    }

    private const string PresetName = "来自可采集物品列表";

    private void DrawAddToAutoGather(IGatherable item)
    {
        var current = _autoGatherListsCache.Selector.EnsureCurrent();

        if (ImGui.Selectable("添加至自动采集列表"))
        {
            if (current == null)
                CreateAndAddPreset(item);
            else
                _plugin.AutoGatherListsManager.AddItem(current, item);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                $"添加 {item.Name[GatherBuddy.Language]} 至 {(current == null ? "一个新的采集窗口预设" : CheckUnnamed(current.Name))}");
    }

    private static AutoGatherList CreateAndAddPreset(IGatherable item)
    {
        var preset = new AutoGatherList
        {
            Enabled     = true,
            Description = AutomaticallyGenerated,
            Name        = PresetName
        };
        preset.Add(item);
        _plugin.AutoGatherListsManager.AddList(preset);

        return preset;
    }

    public static void CreateGatherWindowContextMenu(IGatherable item, bool clicked)
    {
        if (clicked)
            ImGui.OpenPopup(item.Name[GatherBuddy.Language]);

        using var popup = ImRaii.Popup(item.Name[GatherBuddy.Language]);
        if (!popup)
            return;

        if (ImGui.Selectable("创建物品链接"))
            Communicator.Print(SeString.CreateItemLink(item.ItemId));
        DrawOpenInGarlandTools(item.ItemId);
        DrawOpenInTeamCraft(item.ItemId);
    }

    public static void CreateContextMenu(Bait bait)
    {
        if (bait.Id == 0)
            return;

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup(bait.Name);

        using var popup = ImRaii.Popup(bait.Name);
        if (!popup)
            return;

        if (ImGui.Selectable("创建物品链接"))
            Communicator.Print(SeString.CreateItemLink(bait.Id));
        DrawOpenInGarlandTools(bait.Id);
        DrawOpenInTeamCraft(bait.Id);
    }

    public static void CreateContextMenu(FishingSpot? spot)
    {
        if (spot == null)
            return;

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup(spot.Name);

        using var popup = ImRaii.Popup(spot.Name);
        if (!popup)
            return;

        DrawOpenInTeamCraft(spot);
    }
}
