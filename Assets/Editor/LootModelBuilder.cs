using System.IO;
using UnityEditor;
using UnityEngine;

namespace ClutchFPS.EditorTools
{
    /// Builds loot "display model" prefabs into Resources/Loot from real meshes,
    /// so dropped loot shows a medkit / ammo crate / gun instead of a cube.
    /// Run headless: Unity -batchmode -quit -executeMethod
    /// ClutchFPS.EditorTools.LootModelBuilder.Build
    public static class LootModelBuilder
    {
        private const string OutDir = "Assets/Resources/Loot";

        public static void Build()
        {
            Directory.CreateDirectory(OutDir);

            const string wep = "Assets/Infima Games/Low Poly Shooter Pack - Free Sample/Art/Meshes/Weapons";

            // Items — Synty props, authored small; scale is a first guess to tune.
            BuildFromFbx("Medkit", "Assets/Synty/PolygonGeneric/Models/SM_Gen_Prop_Medkit_01.fbx", 1.4f);
            BuildFromFbx("Ammo", "Assets/Synty/PolygonGeneric/Models/SM_Gen_Prop_Crate_01.fbx", 0.7f);

            // Weapons — keyed by slot (0 AR, 1 pistol, 2 SMG). SMG reuses the AR.
            BuildFromFbx("Weapon_0", $"{wep}/ARs/SK_AR_01.fbx", 0.9f);
            BuildFromFbx("Weapon_1", $"{wep}/Handguns/SK_Handgun_03.fbx", 1.2f);
            BuildFromFbx("Weapon_2", $"{wep}/ARs/SK_AR_01.fbx", 0.8f);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("LootModelBuilder: done.");
        }

        private static void BuildFromFbx(string key, string fbxPath, float scale)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbx == null) { Debug.LogWarning($"LootModelBuilder: FBX not found {fbxPath}"); return; }

            var root = new GameObject(key);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            inst.transform.SetParent(root.transform, false);
            inst.transform.localPosition = Vector3.zero;
            root.transform.localScale = Vector3.one * scale;

            string prefabPath = $"{OutDir}/{key}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"LootModelBuilder: built {prefabPath} (scale {scale})");
        }
    }
}
