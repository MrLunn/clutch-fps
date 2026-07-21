using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ClutchFPS.Environment
{
    /// A named region on the map, used purely for the HUD's "you are here"
    /// label. Footprints are axis-aligned rectangles in the XZ plane.
    public static class MapZones
    {
        public struct Zone
        {
            public string Name;
            public Vector2 Centre; // x, z
            public Vector2 HalfSize;
        }

        private static readonly List<Zone> _zones = new();

        // The static registry outlives scene loads, so it remembers which
        // scene it was built for; a different active scene means the list is
        // stale and ZoneAt reports "no zones" until a MapBuilder repopulates.
        private static string _scene;

        /// Reset for a fresh build, tagging the owning scene.
        public static void Clear(string scene)
        {
            _zones.Clear();
            _scene = scene;
        }

        public static void Add(string name, Vector2 centre, Vector2 size)
        {
            _zones.Add(new Zone { Name = name, Centre = centre, HalfSize = size * 0.5f });
        }

        public static bool HasZones =>
            _zones.Count > 0 && _scene == SceneManager.GetActiveScene().name;

        /// The zone a world position sits in, by nearest rectangle (distance 0
        /// when inside one), or null if no zones apply to the active scene.
        public static string ZoneAt(Vector3 worldPosition)
        {
            if (!HasZones) return null;

            string best = null;
            float bestDistance = float.MaxValue;
            Vector2 p = new(worldPosition.x, worldPosition.z);

            foreach (var zone in _zones)
            {
                float dx = Mathf.Max(0f, Mathf.Abs(p.x - zone.Centre.x) - zone.HalfSize.x);
                float dy = Mathf.Max(0f, Mathf.Abs(p.y - zone.Centre.y) - zone.HalfSize.y);
                float distance = dx * dx + dy * dy;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = zone.Name;
                }
            }
            return best;
        }
    }
}
