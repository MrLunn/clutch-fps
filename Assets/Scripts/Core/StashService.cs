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
