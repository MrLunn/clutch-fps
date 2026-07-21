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
            public int[] itemIds = new int[0];
            public int[] itemCounts = new int[0];
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
            itemIds = new[] { (int)ItemType.Ammo556, (int)ItemType.Ammo9mm, (int)ItemType.Medkit },
            itemCounts = new[] { 60, 30, 1 }
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

        /// Pull every consumable out of the stash for a raid loadout, emptying
        /// the stash of them. They ride into the raid; extracting deposits them
        /// (and any loot) back, dying loses them. Weapons are not checked out —
        /// owned slots persist across raids.
        public static void CheckoutItems(string playerName, out int[] ids, out int[] counts)
        {
            var stash = Get(playerName);
            if (stash?.itemIds == null || stash.itemIds.Length == 0)
            {
                ids = new int[0];
                counts = new int[0];
                return;
            }
            ids = (int[])stash.itemIds.Clone();
            counts = (int[])stash.itemCounts.Clone();
            stash.itemIds = new int[0];
            stash.itemCounts = new int[0];
            Save(stash);
        }

        /// Merges a raid's haul into the player's stash: item counts add up,
        /// weapon slots are unioned, and better (higher-index) variants win.
        /// This is what makes the stash grow across raids.
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
                    if (variants[i] > merged[i]) merged[i] = variants[i];
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
