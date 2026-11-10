using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace PoVHallsChests
{

/// <summary>
/// Compatibility filter for Path of Valheiman 6.0.6.  Halls of Oaths reward
/// generation bypasses PoV's normal "Equipment from chests" setting; this
/// plugin removes only equipment from that special reward route.
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("treextr.PathOfValheiman")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "Dorn.PoVHallsChests";
    public const string PluginName = "PoV Halls Chests";
    public const string PluginVersion = "1.0.2";

    internal static ManualLogSource Log;
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<EquipmentFilterScope> FilterScope;
    internal static ConfigEntry<bool> LogRemovedItems;
    internal static ConfigEntry<bool> ReplaceEmptyRolls;
    internal static ConfigEntry<float> CurrencyReplacementChance;

    private void Awake()
    {
        Log = Logger;
        BindConfiguration();
        try
        {
            new Harmony(PluginGuid).PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo("Loaded. Halls of Oaths equipment rewards will be removed; currencies, runes, gems and materials remain.");
        }
        catch (Exception exception)
        {
            Logger.LogError($"Could not install Path of Valheiman compatibility filter: {exception}");
        }
    }

    private void BindConfiguration()
    {
        Enabled = Config.Bind(
            "General",
            "Enabled",
            true,
            "When enabled, filters selected equipment from Path of Valheiman Halls of Oaths rewards.");

        FilterScope = Config.Bind(
            "General",
            "Filter scope",
            EquipmentFilterScope.AllEquipment,
            "Selects which equipment types are removed: AllEquipment, WeaponsOnly, ArmorOnly, or WeaponsAndArmor.");

        LogRemovedItems = Config.Bind(
            "Debug",
            "Log removed items",
            false,
            "Logs the prefab names and total count of equipment removed from each Halls reward roll.");

        ReplaceEmptyRolls = Config.Bind(
            "Replacement",
            "Replace empty rolls",
            true,
            "When every item in a Halls reward roll was removed as equipment, add one tier-appropriate currency or material reward.");

        CurrencyReplacementChance = Config.Bind(
            "Replacement",
            "Currency replacement chance",
            0.50f,
            new ConfigDescription("Chance that an empty Halls roll is replaced with a Path of Valheiman currency. Otherwise it receives a tier-appropriate material.", new AcceptableValueRange<float>(0f, 1f)));
    }
}

public enum EquipmentFilterScope
{
    AllEquipment,
    WeaponsOnly,
    ArmorOnly,
    WeaponsAndArmor
}

[HarmonyPatch]
internal static class HallsMaterializePatch
{
    private static readonly BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static MethodInfo _canHave;
    private static MethodInfo _isJewel;
    private static MethodInfo _isCurrency;
    private static MethodInfo _hallsAt;
    private static MethodInfo _currencyFor;
    private static MethodInfo _materialize;
    private static bool _resolved;

    private static MethodBase TargetMethod()
    {
        // Reflection intentionally avoids a compile-time dependency on PoV internals.
        var type = AccessTools.TypeByName("PassiveTree.Loot.LootDrops");
        var method = type == null ? null : AccessTools.Method(type, "Materialize");
        if (method == null)
            Plugin.Log.LogError("PoV LootDrops.Materialize was not found. This patch supports Path of Valheiman 6.0.6.");
        return method;
    }

    private static void Postfix(object __0, ref List<ItemDrop.ItemData> __result)
    {
        if (!Plugin.Enabled.Value || __result == null || __result.Count == 0 || !IsHallsContext(__0))
            return;

        ResolvePoVMethods();
        if (_canHave == null || _isJewel == null || _isCurrency == null)
        {
            Plugin.Log.LogWarning("PoV item classifiers could not be resolved; Halls rewards were left unchanged.");
            return;
        }

        var removedPrefabs = new List<string>();
        var removed = __result.RemoveAll(item => ShouldRemove(item, removedPrefabs));
        if (removed > 0)
        {
            if (Plugin.LogRemovedItems.Value)
                Plugin.Log.LogInfo($"Removed {removed} Halls equipment reward(s): {string.Join(", ", removedPrefabs)}");
            else
                Plugin.Log.LogDebug($"Removed {removed} equipment reward(s) from a Halls of Oaths roll.");

            if (__result.Count == 0 && Plugin.ReplaceEmptyRolls.Value)
            {
                var replacement = CreateReplacement(__0, removed);
                if (replacement.Count > 0)
                {
                    __result.AddRange(replacement);
                    if (Plugin.LogRemovedItems.Value)
                        Plugin.Log.LogInfo($"Added empty-roll replacement: {string.Join(", ", replacement.ConvertAll(ItemName))}");
                }
                else
                    Plugin.Log.LogWarning("A Halls roll became empty after equipment filtering, but no replacement could be created.");
            }
        }
    }

    private static bool IsHallsContext(object context)
    {
        if (context == null)
            return false;

        var eventId = ReadString(context, "EventId");
        if (!string.IsNullOrEmpty(eventId) && eventId.StartsWith("halls:", StringComparison.OrdinalIgnoreCase))
            return true;

        // Boss rewards inherit a normal kill event id, so identify the Halls by location too.
        ResolvePoVMethods();
        if (_hallsAt == null)
            return false;

        var position = ReadMember(context, "Pos");
        try { return position != null && _hallsAt.Invoke(null, new[] { position }) != null; }
        catch { return false; }
    }

    private static bool ShouldRemove(ItemDrop.ItemData item, List<string> removedPrefabs)
    {
        try
        {
            var canHaveRarity = (bool)(_canHave.Invoke(null, new object[] { item }) ?? false);
            if (!canHaveRarity)
                return false;

            // PoV's own gear predicate preserves jewels and runic currencies.
            var isJewel = (bool)(_isJewel.Invoke(null, new object[] { item }) ?? false);
            var isCurrency = (bool)(_isCurrency.Invoke(null, new object[] { item }) ?? false);
            if (isJewel || isCurrency || !MatchesScope(item))
                return false;

            if (Plugin.LogRemovedItems.Value)
                removedPrefabs.Add(item.m_dropPrefab == null ? item.m_shared.m_name : item.m_dropPrefab.name);
            return true;
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"Could not classify a Halls reward: {exception.Message}");
            return false;
        }
    }

    private static bool MatchesScope(ItemDrop.ItemData item)
    {
        var itemType = item.m_shared.m_itemType;
        var isWeapon = itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon
            || itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon
            || itemType == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft
            || itemType == ItemDrop.ItemData.ItemType.Bow
            || itemType == ItemDrop.ItemData.ItemType.Torch;
        var isArmor = itemType == ItemDrop.ItemData.ItemType.Helmet
            || itemType == ItemDrop.ItemData.ItemType.Chest
            || itemType == ItemDrop.ItemData.ItemType.Legs
            || itemType == ItemDrop.ItemData.ItemType.Shoulder;

        switch (Plugin.FilterScope.Value)
        {
            case EquipmentFilterScope.WeaponsOnly:
                return isWeapon;
            case EquipmentFilterScope.ArmorOnly:
                return isArmor;
            case EquipmentFilterScope.WeaponsAndArmor:
                return isWeapon || isArmor;
            default:
                return true;
        }
    }

    private static List<ItemDrop.ItemData> CreateReplacement(object context, int removedCount)
    {
        var eventId = ReadString(context, "EventId") ?? "pov-halls";
        var random = new Random(StableSeed(eventId, removedCount));
        var currencyChance = Math.Max(0f, Math.Min(1f, Plugin.CurrencyReplacementChance.Value));

        if (random.NextDouble() < currencyChance)
        {
            var currency = TryCreateCurrency(context, random);
            if (currency.Count > 0)
                return currency;
        }

        var material = TryCreateTierMaterial(context, random);
        if (material != null)
            return new List<ItemDrop.ItemData> { material };

        // Currency is a safer final fallback than leaving a filtered Hall reward empty.
        return TryCreateCurrency(context, random);
    }

    private static List<ItemDrop.ItemData> TryCreateCurrency(object context, Random random)
    {
        try
        {
            ResolvePoVMethods();
            if (_currencyFor == null || _materialize == null)
                return new List<ItemDrop.ItemData>();

            var reward = _currencyFor.Invoke(null, new object[] { context, random });
            if (reward == null)
                return new List<ItemDrop.ItemData>();

            var rewards = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(reward.GetType()));
            rewards.Add(reward);
            var materialized = _materialize.Invoke(null, new object[] { context, rewards, random }) as List<ItemDrop.ItemData>;
            return materialized ?? new List<ItemDrop.ItemData>();
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"Could not create PoV currency replacement: {exception.Message}");
            return new List<ItemDrop.ItemData>();
        }
    }

    private static ItemDrop.ItemData TryCreateTierMaterial(object context, Random random)
    {
        var level = ReadInt(context, "Level", 1);
        string prefab;
        int min;
        int max;

        if (level >= 181) { prefab = "FlametalOreNew"; min = 3; max = 6; }
        else if (level >= 97) { prefab = "BlackMetalScrap"; min = 4; max = 8; }
        else if (level >= 77) { prefab = "SilverOre"; min = 4; max = 8; }
        else if (level >= 43) { prefab = "IronScrap"; min = 4; max = 8; }
        else if (random.Next(2) == 0) { prefab = "CopperOre"; min = 5; max = 9; }
        else { prefab = "TinOre"; min = 4; max = 8; }

        try
        {
            var itemPrefab = ObjectDB.instance == null ? null : ObjectDB.instance.GetItemPrefab(prefab);
            var itemDrop = itemPrefab == null ? null : itemPrefab.GetComponent<ItemDrop>();
            if (itemDrop == null || itemDrop.m_itemData == null)
                return null;

            var item = itemDrop.m_itemData.Clone();
            item.m_stack = random.Next(min, max + 1);
            return item;
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"Could not create tier material replacement {prefab}: {exception.Message}");
            return null;
        }
    }

    private static int StableSeed(string eventId, int removedCount)
    {
        unchecked
        {
            var hash = 17;
            foreach (var character in eventId)
                hash = hash * 31 + character;
            return hash * 31 + removedCount;
        }
    }

    private static string ItemName(ItemDrop.ItemData item)
    {
        return item == null || item.m_dropPrefab == null ? "unknown" : item.m_dropPrefab.name;
    }

    private static void ResolvePoVMethods()
    {
        if (_resolved)
            return;
        _resolved = true;

        _canHave = FindStatic("Rarity", "CanHave");
        _isJewel = FindStatic("Jewels", "Is");
        _isCurrency = FindStatic("Runes", "IsCurrency");

        var lootDrops = FindType("LootDrops");
        _currencyFor = FindStatic("LootDrops", "CurrencyFor", 2);
        _materialize = lootDrops == null ? null : AccessTools.Method(lootDrops, "Materialize");

        var hallsRun = FindType("HallsRun");
        _hallsAt = hallsRun == null ? null : AccessTools.Method(hallsRun, "At");
    }

    private static MethodInfo FindStatic(string typeSuffix, string methodName, int parameterCount = -1)
    {
        var type = FindType(typeSuffix);
        if (type == null)
            return null;

        foreach (var method in type.GetMethods(AnyStatic))
            if (method.Name == methodName && (parameterCount < 0 || method.GetParameters().Length == parameterCount))
                return method;
        return null;
    }

    private static Type FindType(string simpleName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException exception) { types = Array.FindAll(exception.Types, t => t != null); }
            catch { continue; }

            foreach (var type in types)
                if (type != null && type.Name == simpleName && type.Namespace != null && type.Namespace.StartsWith("PassiveTree", StringComparison.Ordinal))
                    return type;
        }
        return null;
    }

    private static object ReadMember(object instance, string memberName)
    {
        var type = instance.GetType();
        var property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property != null)
            return property.GetValue(instance);
        var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return field == null ? null : field.GetValue(instance);
    }

    private static string ReadString(object instance, string memberName) { return ReadMember(instance, memberName) as string; }

    private static int ReadInt(object instance, string memberName, int fallback)
    {
        var value = ReadMember(instance, memberName);
        return value is int integer ? integer : fallback;
    }
}
}
