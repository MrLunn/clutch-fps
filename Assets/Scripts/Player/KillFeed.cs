using System.Collections.Generic;
using UnityEngine;

namespace ClutchFPS.Player
{
    /// Client-side rolling list of recent kills, drawn by PlayerHUD.
    public static class KillFeed
    {
        private struct Entry
        {
            public string Text;
            public float Time;
        }

        private const float MaxAge = 6f;
        private static readonly List<Entry> Entries = new();

        /// Statics persist across sessions; clear on raid start.
        public static void Clear() => Entries.Clear();

        public static void Add(string attackerName, string victimName, bool suicide)
        {
            Entries.Add(new Entry
            {
                Text = suicide
                    ? $"{victimName} died"
                    : $"{attackerName}  ▶  {victimName}",
                Time = Time.time
            });
        }

        public static List<string> Recent()
        {
            Entries.RemoveAll(e => Time.time - e.Time > MaxAge);
            var texts = new List<string>(Entries.Count);
            foreach (var entry in Entries) texts.Add(entry.Text);
            return texts;
        }
    }
}
