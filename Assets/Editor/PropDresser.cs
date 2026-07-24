using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ClutchFPS.EditorTools
{
    /// Populates the baked map with real industrial props from the Industrial
    /// Set pack — placed per zone so each district gets its own character. Each
    /// prop is auto-seated on the ground via its renderer bounds (pivot-safe),
    /// parented under a single "Dressing" root, and saved into the scene as a
    /// prefab instance so it can be hand-tweaked afterwards. Re-running clears
    /// the previous dressing first.
    public static class PropDresser
    {
        private const string ScenePath = "Assets/Scenes/Raid_Complex.unity";
        private const string DressingRoot = "Dressing";

        // prefab name, x, z, yaw
        private static readonly (string prefab, float x, float z, float yaw)[] Placements =
        {
            // --- Container yard (east): oil tanks as landmarks + clutter ---
            ("Oil_tank_v1", 17f, -21f, 0f),
            ("Oil_tank_v1", 40f, -21f, 30f),
            ("Oil_tank_v2", 41f, 11f, 0f),
            ("Barrel_v2_single", 20f, -10f, 0f),
            ("Barrel_v3_single", 21.1f, -9.2f, 40f),
            ("Barrel_v1_LD1", 20.4f, -11f, 10f),
            ("Barrel_v2_single", 34f, 2f, 0f),
            ("Barrel_v3_single", 35.1f, 3f, 25f),
            ("Palet_v1_set", 30f, -22f, 20f),
            ("Dumpsters_v1_empty", 42f, -6f, 90f),
            ("Road_block_v1", 14f, -3f, 90f),
            ("Road_block_v1", 14f, 1f, 90f),

            // --- Warehouse (west): pallets, pipes, power ---
            // Clear of the catwalk ramp, which occupies x -38..-30, z -14..-4.
            ("Palet_v1_set", -22f, -11f, 0f),
            ("Bags_on_pallet_v1_1", -19.5f, -11.5f, 15f),
            ("Generator_v1", -40f, 20f, 45f),
            ("Electric_box_v1", -41f, 4f, 90f),
            ("Pipes_set_v1_H_set_v1", -41f, -6f, 0f),
            ("Industrial_pipe_v1", -41f, 12f, 0f),
            ("Barrel_v1_LD1", -14f, 18f, 0f),
            ("Barrel_v2_single", -15.1f, 17.2f, 30f),

            // --- Offices (north): dumpsters + power boxes ---
            ("Dumpsters_v1_garbadge", 0f, 23f, 0f),
            ("Dumpsters_v1_empty", 37f, 42f, 90f),
            ("Electric_box_v2", 38f, 30f, 90f),
            ("Barrel_v3_single", 2f, 45f, 0f),
            ("Barrel_v1_LD1", 3.1f, 44f, 20f),

            // --- Plaza (centre): sparse cover ---
            ("Road_block_v1", -10f, -14f, 0f),
            ("Road_block_v1", 10f, -14f, 0f),
            ("Barrel_v2_single", 0f, 10f, 0f),
            ("Barrel_v3_single", 1.2f, 11f, 35f),

            // --- Staging area (south spawn): pallets, barrels, blocks ---
            ("Palet_v1_set", -18f, -40f, 0f),
            ("Palet_v1_set", 18f, -40f, 25f),
            ("Bags_on_pallet_v1_2", 10f, -42f, 0f),
            ("Barrel_v2_single", -8f, -38f, 0f),
            ("Barrel_v3_single", -7f, -39f, 40f),
            ("Barrel_v1_LD1", -9.1f, -37f, 15f),
            ("Road_block_v1", -4f, -24f, 0f),
            ("Road_block_v1", 4f, -24f, 0f),
        };

        [MenuItem("Tools/ClutchFPS/Dress Map With Props")]
        public static void Dress()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // Clear a previous dressing pass.
            var existing = GameObject.Find(DressingRoot);
            if (existing != null) Object.DestroyImmediate(existing);
            var root = new GameObject(DressingRoot);

            var obstacles = CollectMapObstacles();

            int placed = 0, missing = 0, nudged = 0, dropped = 0;
            foreach (var p in Placements)
            {
                var prefab = FindPrefab(p.prefab);
                if (prefab == null) { Debug.LogWarning("PropDresser: prefab not found " + p.prefab); missing++; continue; }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                instance.transform.SetPositionAndRotation(new Vector3(p.x, 0f, p.z), Quaternion.Euler(0f, p.yaw, 0f));
                SeatOnGround(instance);

                // Don't let props bury themselves in walls, ramps or racking:
                // search outward for the nearest clear spot, and bin the prop if
                // there isn't one rather than leaving it clipping.
                if (Overlaps(instance, obstacles))
                {
                    if (TryFindClearSpot(instance, obstacles)) { nudged++; }
                    else
                    {
                        Debug.LogWarning($"PropDresser: no clear spot for {p.prefab} near ({p.x}, {p.z}) - skipped.");
                        Object.DestroyImmediate(instance);
                        dropped++;
                        continue;
                    }
                }
                placed++;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"PropDresser: placed {placed} props ({nudged} nudged clear, {dropped} skipped, {missing} missing) under '{DressingRoot}'.");
        }

        /// World-space bounds of the baked map's solid geometry. Flat things
        /// (floors, gravel, road markings) are ignored — every prop sits on
        /// those by definition.
        private static List<Bounds> CollectMapObstacles()
        {
            var list = new List<Bounds>();
            var builder = Object.FindFirstObjectByType<ClutchFPS.Environment.MapBuilder>();
            if (builder == null) return list;

            foreach (var renderer in builder.GetComponentsInChildren<Renderer>())
            {
                var b = renderer.bounds;
                if (b.max.y < 0.35f) continue;   // ground plane, decals, painted lines
                list.Add(b);
            }
            return list;
        }

        /// Slightly shrunk so props may touch geometry without counting as
        /// clipping through it.
        private static bool Overlaps(GameObject instance, List<Bounds> obstacles)
        {
            if (!TryGetWorldBounds(instance, out var bounds)) return false;
            bounds.Expand(-0.3f);
            if (bounds.size.x <= 0f || bounds.size.z <= 0f) return false;

            for (int i = 0; i < obstacles.Count; i++)
            {
                if (obstacles[i].Intersects(bounds)) return true;
            }
            return false;
        }

        /// Spiral outward from the intended spot looking for open ground.
        private static bool TryFindClearSpot(GameObject instance, List<Bounds> obstacles)
        {
            Vector3 origin = instance.transform.position;
            for (float radius = 1.5f; radius <= 9f; radius += 1.5f)
            {
                for (int step = 0; step < 12; step++)
                {
                    float angle = step * Mathf.PI * 2f / 12f;
                    instance.transform.position = origin + new Vector3(
                        Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                    SeatOnGround(instance);
                    if (!Overlaps(instance, obstacles)) return true;
                }
            }
            instance.transform.position = origin;
            return false;
        }

        private static bool TryGetWorldBounds(GameObject instance, out Bounds bounds)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) { bounds = default; return false; }
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return true;
        }

        /// Drop the prop so the bottom of its combined renderer bounds rests on
        /// the ground plane (y=0), whatever the prefab's pivot is.
        private static void SeatOnGround(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            float lift = -bounds.min.y; // move so min.y lands on 0
            instance.transform.position += new Vector3(0f, lift, 0f);
        }

        private static GameObject FindPrefab(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets($"t:Prefab {name}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.Contains("RPG_FPS_game_assets_industrial")) continue;
                if (System.IO.Path.GetFileNameWithoutExtension(path) == name)
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return null;
        }
    }
}
