using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ClutchFPS.Core
{
    /// Host-side persistent stashes, keyed by player name, stored as JSON in
    /// persistentDataPath. Only the server reads/writes these.
    public static class StashService
    {
        [System.Serializable]
        public class StashEntry
        {
            public string playerName;
            public int credits = 0;
            public int ownedSlots = 1;
            public int[] weaponVariants = new int[0];

            // Safe stash: stays at base, never at risk.
            public int[] itemIds = new int[0];
            public int[] itemCounts = new int[0];

            // Loadout: the kit you've equipped to take into the next raid.
            // Weapons here are a subset of ownedSlots (bit 0 always in).
            public int loadoutSlots = 1;
            public int[] loadoutItemIds = new int[0];
            public int[] loadoutItemCounts = new int[0];

            // Lifetime stats. Backend-agnostic: these ride along into cloud
            // storage unchanged once accounts land.
            public int totalKills;
            public int totalDeaths;
            public int raidsRun;
            public int raidsSurvived;
            public int lifetimeCredits;
        }

        // What a brand-new operator (or a fresh start) walks away with.
        public const int StarterCredits = 3000;

        [System.Serializable]
        private class StashFile
        {
            public List<StashEntry> entries = new();
        }

        private static string FilePath => Path.Combine(Application.persistentDataPath, "stashes.json");
        private static StashFile _cache;

        private static StashFile Load()
        {
            if (_cache != null) return _cache;
            _cache = new StashFile();
            if (File.Exists(FilePath))
            {
                try
                {
                    _cache = JsonUtility.FromJson<StashFile>(File.ReadAllText(FilePath)) ?? new StashFile();
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Stash file unreadable, starting fresh: {e.Message}");
                }
            }
            return _cache;
        }

        public static StashEntry Get(string playerName)
        {
            return Load().entries.Find(entry => entry.playerName == playerName);
        }

        public static StashEntry GetOrCreate(string playerName)
        {
            return Get(playerName) ?? new StashEntry { playerName = playerName };
        }

        // ---------- starter kit / fresh start ----------

        private static StashEntry MakeStarter(string playerName) => new()
        {
            playerName = playerName,
            credits = StarterCredits,
            ownedSlots = 1, // the free starter rifle
            weaponVariants = new[] { -1 },
            // Safe stash starts with spare ammo; the medkit is pre-equipped so a
            // first raid is kitted, and the rifle is equipped by default.
            itemIds = new[] { (int)ItemType.Ammo556, (int)ItemType.Ammo9mm },
            itemCounts = new[] { 60, 30 },
            loadoutSlots = 1,
            loadoutItemIds = new[] { (int)ItemType.Medkit },
            loadoutItemCounts = new[] { 1 }
        };

        /// First-time operators (no stash on record) get a starter kit so they
        /// can actually play. Existing stashes are left untouched.
        public static void EnsureStarter(string playerName)
        {
            if (Get(playerName) == null) Save(MakeStarter(playerName));
        }

        /// Wipe back to the starter kit — for when someone loses everything and
        /// wants a clean slate.
        public static void ResetToStarter(string playerName)
        {
            Save(MakeStarter(playerName));
        }

        // ---------- credits ----------

        public static int Credits(string playerName) => Get(playerName)?.credits ?? 0;

        public static void AddCredits(string playerName, int amount)
        {
            var stash = GetOrCreate(playerName);
            stash.credits = Mathf.Max(0, stash.credits + amount);
            if (amount > 0) stash.lifetimeCredits += amount;
            Save(stash);
        }

        // ---------- lifetime stats ----------

        public static void RecordKill(string playerName)
        {
            var stash = GetOrCreate(playerName);
            stash.totalKills++;
            Save(stash);
        }

        public static void RecordDeath(string playerName)
        {
            var stash = GetOrCreate(playerName);
            stash.totalDeaths++;
            Save(stash);
        }

        public static void RecordRaidStart(string playerName)
        {
            var stash = GetOrCreate(playerName);
            stash.raidsRun++;
            Save(stash);
        }

        public static void RecordExtract(string playerName)
        {
            var stash = GetOrCreate(playerName);
            stash.raidsSurvived++;
            Save(stash);
        }

        public static bool TrySpend(string playerName, int amount)
        {
            var stash = Get(playerName);
            if (stash == null || stash.credits < amount) return false;
            stash.credits -= amount;
            Save(stash);
            return true;
        }

        // ---------- item mutation ----------

        public static int ItemCount(string playerName, int itemId)
        {
            var stash = Get(playerName);
            if (stash?.itemIds == null) return 0;
            for (int i = 0; i < stash.itemIds.Length; i++)
            {
                if (stash.itemIds[i] == itemId) return stash.itemCounts[i];
            }
            return 0;
        }

        public static void AddItem(string playerName, int itemId, int count)
        {
            if (count <= 0) return;
            var stash = GetOrCreate(playerName);
            var ids = new List<int>(stash.itemIds ?? new int[0]);
            var counts = new List<int>(stash.itemCounts ?? new int[0]);
            int at = ids.IndexOf(itemId);
            if (at >= 0) counts[at] += count;
            else { ids.Add(itemId); counts.Add(count); }
            stash.itemIds = ids.ToArray();
            stash.itemCounts = counts.ToArray();
            Save(stash);
        }

        public static void RemoveItem(string playerName, int itemId, int count)
        {
            var stash = Get(playerName);
            if (stash?.itemIds == null || count <= 0) return;
            var ids = new List<int>(stash.itemIds);
            var counts = new List<int>(stash.itemCounts);
            int at = ids.IndexOf(itemId);
            if (at < 0) return;
            counts[at] -= count;
            if (counts[at] <= 0) { ids.RemoveAt(at); counts.RemoveAt(at); }
            stash.itemIds = ids.ToArray();
            stash.itemCounts = counts.ToArray();
            Save(stash);
        }

        public static void AddWeapon(string playerName, int slot, int variant)
        {
            if (slot < 0) return;
            var stash = GetOrCreate(playerName);
            stash.ownedSlots |= 1 << slot;
            var variants = new List<int>(stash.weaponVariants ?? new int[0]);
            while (variants.Count <= slot) variants.Add(-1);
            if (variant > variants[slot]) variants[slot] = variant;
            stash.weaponVariants = variants.ToArray();
            Save(stash);
        }

        // ---------- loadout (equip / unequip) ----------

        /// True when a weapon slot is marked to take into the raid.
        public static bool IsSlotEquipped(string playerName, int slot)
        {
            var stash = Get(playerName);
            return stash != null && ((stash.loadoutSlots >> slot) & 1) == 1;
        }

        public static void EquipWeapon(string playerName, int slot)
        {
            var stash = Get(playerName);
            if (stash == null || ((stash.ownedSlots >> slot) & 1) != 1) return;
            stash.loadoutSlots |= 1 << slot;
            Save(stash);
        }

        public static void UnequipWeapon(string playerName, int slot)
        {
            // Slot 0 (the starter rifle) is always taken — never go in unarmed.
            if (slot == 0) return;
            var stash = Get(playerName);
            if (stash == null) return;
            stash.loadoutSlots &= ~(1 << slot);
            Save(stash);
        }

        /// Move a whole item stack from the safe stash into the loadout.
        public static void EquipItem(string playerName, int itemId)
        {
            var stash = Get(playerName);
            if (stash == null) return;
            int count = TakeAll(ref stash.itemIds, ref stash.itemCounts, itemId);
            if (count <= 0) return;
            AddTo(ref stash.loadoutItemIds, ref stash.loadoutItemCounts, itemId, count);
            Save(stash);
        }

        /// Move a whole item stack from the loadout back into the safe stash.
        public static void UnequipItem(string playerName, int itemId)
        {
            var stash = Get(playerName);
            if (stash == null) return;
            int count = TakeAll(ref stash.loadoutItemIds, ref stash.loadoutItemCounts, itemId);
            if (count <= 0) return;
            AddTo(ref stash.itemIds, ref stash.itemCounts, itemId, count);
            Save(stash);
        }

        /// Check the equipped loadout out for a raid: returns the equipped weapon
        /// mask (always incl. slot 0) and the equipped items, removing the items
        /// from the loadout so they're at risk. Extract deposits them back to the
        /// safe stash; dying loses them. Owned weapon slots are untouched.
        public static void CheckoutLoadout(string playerName, out int[] ids, out int[] counts, out int slots)
        {
            var stash = Get(playerName);
            if (stash == null)
            {
                ids = new int[0];
                counts = new int[0];
                slots = 1;
                return;
            }
            slots = stash.loadoutSlots | 1;
            ids = stash.loadoutItemIds != null ? (int[])stash.loadoutItemIds.Clone() : new int[0];
            counts = stash.loadoutItemCounts != null ? (int[])stash.loadoutItemCounts.Clone() : new int[0];
            stash.loadoutItemIds = new int[0];
            stash.loadoutItemCounts = new int[0];
            Save(stash);
        }

        // Small array helpers for the parallel id/count lists.
        private static int TakeAll(ref int[] ids, ref int[] counts, int itemId)
        {
            var idList = new List<int>(ids ?? new int[0]);
            var countList = new List<int>(counts ?? new int[0]);
            int at = idList.IndexOf(itemId);
            if (at < 0) return 0;
            int count = countList[at];
            idList.RemoveAt(at);
            countList.RemoveAt(at);
            ids = idList.ToArray();
            counts = countList.ToArray();
            return count;
        }

        private static void AddTo(ref int[] ids, ref int[] counts, int itemId, int count)
        {
            var idList = new List<int>(ids ?? new int[0]);
            var countList = new List<int>(counts ?? new int[0]);
            int at = idList.IndexOf(itemId);
            if (at >= 0) countList[at] += count;
            else { idList.Add(itemId); countList.Add(count); }
            ids = idList.ToArray();
            counts = countList.ToArray();
        }

        /// Merges a raid's haul into the player's stash: item counts add up,
        /// weapon slots are unioned, and each slot you carried out banks the
        /// exact weapon you extracted with. Slots you left home are untouched.
        public static void Deposit(string playerName, int ownedSlots, int[] variants,
            int[] itemIds, int[] itemCounts)
        {
            var stash = Get(playerName) ?? new StashEntry { playerName = playerName };

            stash.ownedSlots |= ownedSlots;

            if (variants != null)
            {
                var merged = new List<int>(stash.weaponVariants ?? new int[0]);
                while (merged.Count < variants.Length) merged.Add(-1);
                for (int i = 0; i < variants.Length; i++)
                {
                    // Only the slots you actually brought out update — so a
                    // deliberate downgrade sticks, but weapons left safe in the
                    // stash aren't overwritten by an empty in-raid slot.
                    if (((ownedSlots >> i) & 1) == 1) merged[i] = variants[i];
                }
                stash.weaponVariants = merged.ToArray();
            }

            var ids = new List<int>(stash.itemIds ?? new int[0]);
            var counts = new List<int>(stash.itemCounts ?? new int[0]);
            if (itemIds != null)
            {
                for (int i = 0; i < itemIds.Length; i++)
                {
                    int existing = ids.IndexOf(itemIds[i]);
                    if (existing >= 0) counts[existing] += itemCounts[i];
                    else { ids.Add(itemIds[i]); counts.Add(itemCounts[i]); }
                }
            }
            stash.itemIds = ids.ToArray();
            stash.itemCounts = counts.ToArray();

            Save(stash);
        }

        public static void Save(StashEntry entry)
        {
            var file = Load();
            file.entries.RemoveAll(existing => existing.playerName == entry.playerName);
            file.entries.Add(entry);
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(file, true));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to write stash file: {e.Message}");
            }
        }
    }
}
