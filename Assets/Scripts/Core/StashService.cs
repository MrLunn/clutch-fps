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
            public int ownedSlots = 1;
            public int[] weaponVariants = new int[0];
            public int[] itemIds = new int[0];
            public int[] itemCounts = new int[0];
        }

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
