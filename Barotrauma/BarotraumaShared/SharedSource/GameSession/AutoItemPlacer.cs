﻿using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    static class AutoItemPlacer
    {
        public static bool OutputDebugInfo = false;

        /// <summary>
        /// If we are spawning in an area where difficulty should not be a factor, assume difficulty is at the exact "middle"
        /// </summary>
        public const float DefaultDifficultyModifier = 0f;

        public static void PlaceIfNeeded()
        {
            if (GameMain.NetworkMember != null && !GameMain.NetworkMember.IsServer) { return; }

            for (int i = 0; i < Submarine.MainSubs.Length; i++)
            {
                if (Submarine.MainSubs[i] == null || Submarine.MainSubs[i].Info.InitialSuppliesSpawned) { continue; }
                List<Submarine> subs = new List<Submarine>() { Submarine.MainSubs[i] };
                subs.AddRange(Submarine.MainSubs[i].DockedTo.Where(d => !d.Info.IsOutpost));
                Place(subs);
                subs.ForEach(s => s.Info.InitialSuppliesSpawned = true);
            }

            float difficultyModifier = GetLevelDifficultyModifier();
            foreach (var sub in Submarine.Loaded)
            {
                if (sub.Info.Type == SubmarineType.Player || 
                    sub.Info.Type == SubmarineType.Outpost || 
                    sub.Info.Type == SubmarineType.OutpostModule ||
                    sub.Info.Type == SubmarineType.EnemySubmarine)
                {
                    continue;
                }
                Place(sub.ToEnumerable(), difficultyModifier: difficultyModifier);
            }

            if (Level.Loaded?.StartOutpost != null && Level.Loaded.Type == LevelData.LevelType.Outpost)
            {
                Rand.SetSyncedSeed(ToolBox.StringToInt(Level.Loaded.StartOutpost.Info.Name));
                Place(Level.Loaded.StartOutpost.ToEnumerable());
            }
        }

        private const float MaxDifficultyModifier = 0.2f;

        /// <summary>
        /// Spawn probability of loot is modified by difficulty, -20% less loot at 0% difficulty and +20% loot at 100% difficulty.
        /// </summary>
        private static float GetLevelDifficultyModifier()
        {
            return Math.Clamp(Level.Loaded?.Difficulty is float difficulty ? (difficulty / 100f) * (MaxDifficultyModifier * 2) - MaxDifficultyModifier : DefaultDifficultyModifier, -MaxDifficultyModifier, MaxDifficultyModifier);
        }

        public static void RegenerateLoot(Submarine sub, ItemContainer regeneratedContainer)
        {
            // Level difficulty currently doesn't affect regenerated loot for the sake of simplicity
            Place(sub.ToEnumerable(), regeneratedContainer: regeneratedContainer);
        }

        private static void Place(IEnumerable<Submarine> subs, ItemContainer regeneratedContainer = null, float difficultyModifier = DefaultDifficultyModifier)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient)
            {
                DebugConsole.ThrowError("Clients are not allowed to use AutoItemPlacer.\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }

            List<Item> spawnedItems = new List<Item>(100);

            int itemCountApprox = MapEntityPrefab.List.Count() / 3;
            var containers = new List<ItemContainer>(70 + 30 * subs.Count());
            var prefabsWithContainer = new List<ItemPrefab>(itemCountApprox / 3);
            var prefabsWithoutContainer = new List<ItemPrefab>(itemCountApprox);
            var removals = new List<ItemPrefab>();

            // generate loot only for a specific container if defined
            if (regeneratedContainer != null)
            {
                containers.Add(regeneratedContainer);
            }
            else
            {
                foreach (Item item in Item.ItemList)
                {
                    if (!subs.Contains(item.Submarine)) { continue; }
                    if (item.GetRootInventoryOwner() is Character) { continue; }
                    containers.AddRange(item.GetComponents<ItemContainer>());
                }
                containers.Shuffle(Rand.RandSync.Server);
            }

            foreach (MapEntityPrefab prefab in MapEntityPrefab.List)
            {
                if (!(prefab is ItemPrefab ip)) { continue; }

                if (ip.ConfigElement.Elements().Any(e => string.Equals(e.Name.ToString(), typeof(ItemContainer).Name.ToString(), StringComparison.OrdinalIgnoreCase)))
                {
                    prefabsWithContainer.Add(ip);
                }
                else
                {
                    prefabsWithoutContainer.Add(ip);
                }
            }

            var validContainers = new Dictionary<ItemContainer, PreferredContainer>();
            prefabsWithContainer.Shuffle(Rand.RandSync.Server);
            // Spawn items that have an ItemContainer component first so we can fill them up with items if needed (oxygen tanks inside the spawned diving masks, etc)
            for (int i = 0; i < prefabsWithContainer.Count; i++)
            {
                var itemPrefab = prefabsWithContainer[i];
                if (itemPrefab == null) { continue; }
                if (SpawnItems(itemPrefab))
                {
                    removals.Add(itemPrefab);
                }
            }
            // Remove containers that we successfully spawned items into so that they are not counted in in the second pass.
            removals.ForEach(i => prefabsWithContainer.Remove(i));
            // Another pass for items with containers because also they can spawn inside other items (like smg magazine)
            prefabsWithContainer.ForEach(i => SpawnItems(i));
            // Spawn items that don't have containers last
            prefabsWithoutContainer.Shuffle(Rand.RandSync.Server);
            prefabsWithoutContainer.ForEach(i => SpawnItems(i));

            if (OutputDebugInfo)
            {
                var subNames = subs.Select(s => s.Info.Name).ToList();
                DebugConsole.NewMessage($"Automatically placed items in { string.Join(", ", subNames) }:");
                foreach (string itemName in spawnedItems.Select(it => it.Name).Distinct())
                {
                    DebugConsole.NewMessage(" - " + itemName + " x" + spawnedItems.Count(it => it.Name == itemName));
                }
            }

            if (GameMain.GameSession?.Level != null &&
                GameMain.GameSession.Level.Type == LevelData.LevelType.Outpost &&
                GameMain.GameSession.StartLocation?.TakenItems != null)
            {
                foreach (Location.TakenItem takenItem in GameMain.GameSession.StartLocation.TakenItems)
                {
                    var matchingItem = spawnedItems.Find(it => takenItem.Matches(it));
                    if (matchingItem == null) { continue; }
                    var containedItems = spawnedItems.FindAll(it => it.ParentInventory?.Owner == matchingItem);
                    matchingItem.Remove();
                    spawnedItems.Remove(matchingItem);
                    foreach (Item containedItem in containedItems)
                    {
                        containedItem.Remove();
                        spawnedItems.Remove(containedItem);
                    }
                }
            }
#if SERVER
            foreach (Item spawnedItem in spawnedItems)
            {
                Entity.Spawner.CreateNetworkEvent(spawnedItem, remove: false);
            }
#endif
            bool SpawnItems(ItemPrefab itemPrefab)
            {
                if (itemPrefab == null)
                {
                    string errorMsg = "Error in AutoItemPlacer.SpawnItems - itemPrefab was null.\n"+Environment.StackTrace.CleanupStackTrace();
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("AutoItemPlacer.SpawnItems:ItemNull", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    return false;
                }
                bool success = false;
                foreach (PreferredContainer preferredContainer in itemPrefab.PreferredContainers)
                {
                    if (preferredContainer.SpawnProbability <= 0.0f || preferredContainer.MaxAmount <= 0) { continue; }
                    validContainers = GetValidContainers(preferredContainer, containers, validContainers, primary: true);
                    if (validContainers.None())
                    {
                        validContainers = GetValidContainers(preferredContainer, containers, validContainers, primary: false);
                    }
                    foreach (var validContainer in validContainers)
                    {
                        var newItems = SpawnItem(itemPrefab, containers, validContainer, difficultyModifier);
                        if (newItems.Any())
                        {
                            spawnedItems.AddRange(newItems);
                            success = true;
                        }
                    }
                }
                return success;
            }
        }

        private static Dictionary<ItemContainer, PreferredContainer> GetValidContainers(PreferredContainer preferredContainer, IEnumerable<ItemContainer> allContainers, Dictionary<ItemContainer, PreferredContainer> validContainers, bool primary)
        {
            validContainers.Clear();
            foreach (ItemContainer container in allContainers)
            {
                if (!container.AutoFill) { continue; }
                if (primary)
                {
                    if (!ItemPrefab.IsContainerPreferred(preferredContainer.Primary, container)) { continue; }
                }
                else
                {
                    if (!ItemPrefab.IsContainerPreferred(preferredContainer.Secondary, container)) { continue; }
                }
                if (!validContainers.ContainsKey(container))
                {
                    validContainers.Add(container, preferredContainer);
                }
            }
            return validContainers;
        }

        private static readonly (int quality, float commonness)[] qualityCommonnesses = new (int quality, float commonness)[Quality.MaxQuality + 1]
        {
            (0, 0.85f),
            (1, 0.125f),
            (2, 0.0225f),
            (3, 0.0025f),
        };

        private static List<Item> SpawnItem(ItemPrefab itemPrefab, List<ItemContainer> containers, KeyValuePair<ItemContainer, PreferredContainer> validContainer, float difficultyModifier)
        {
            List<Item> spawnedItems = new List<Item>();
            if (Rand.Value(Rand.RandSync.Server) > validContainer.Value.SpawnProbability * (1f + difficultyModifier)) { return spawnedItems; }
            // Don't add dangerously reactive materials in thalamus wrecks 
            if (validContainer.Key.Item.Submarine.WreckAI != null && itemPrefab.Tags.Contains("explodesinwater"))
            {
                return spawnedItems;
            }
            int amount = Rand.Range(validContainer.Value.MinAmount, validContainer.Value.MaxAmount + 1, Rand.RandSync.Server);
            for (int i = 0; i < amount; i++)
            {
                if (validContainer.Key.Inventory.IsFull(takeStacksIntoAccount: true))
                {
                    containers.Remove(validContainer.Key);
                    break;
                }

                var existingItem = validContainer.Key.Inventory.AllItems.FirstOrDefault(it => it.prefab == itemPrefab);
                int quality = 
                    existingItem?.Quality ??
                    ToolBox.SelectWeightedRandom(
                        qualityCommonnesses.Select(q => q.quality).ToList(),
                        qualityCommonnesses.Select(q => q.commonness).ToList(),
                        Rand.RandSync.Server);
                if (!validContainer.Key.Inventory.CanBePut(itemPrefab, quality: quality)) { break; }
                var item = new Item(itemPrefab, validContainer.Key.Item.Position, validContainer.Key.Item.Submarine)
                {
                    SpawnedInOutpost = validContainer.Key.Item.SpawnedInOutpost,
                    AllowStealing = validContainer.Key.Item.AllowStealing,
                    Quality = quality,
                    OriginalModuleIndex = validContainer.Key.Item.OriginalModuleIndex,
                    OriginalContainerIndex = 
                        Item.ItemList.Where(it => it.Submarine == validContainer.Key.Item.Submarine && it.OriginalModuleIndex == validContainer.Key.Item.OriginalModuleIndex).ToList().IndexOf(validContainer.Key.Item)
                };
                foreach (WifiComponent wifiComponent in item.GetComponents<WifiComponent>())
                {
                    wifiComponent.TeamID = validContainer.Key.Item.Submarine.TeamID;
                }
                spawnedItems.Add(item);
                validContainer.Key.Inventory.TryPutItem(item, null, createNetworkEvent: false);
                containers.AddRange(item.GetComponents<ItemContainer>());
            }
            return spawnedItems;
        }
    }
}
