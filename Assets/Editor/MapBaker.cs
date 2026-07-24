using ClutchFPS.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ClutchFPS.EditorTools
{
    /// One-shot: turns MapBuilder's procedurally generated map into real,
    /// hand-editable scene objects and stops runtime regeneration. Run from the
    /// menu, or headless via -executeMethod ClutchFPS.EditorTools.MapBaker.Bake.
    public static class MapBaker
    {
        private const string ScenePath = "Assets/Scenes/Raid_Complex.unity";

        [MenuItem("Tools/ClutchFPS/Bake Map Into Scene")]
        public static void Bake()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var builder = Object.FindFirstObjectByType<MapBuilder>();
            if (builder == null)
            {
                Debug.LogError("MapBaker: no MapBuilder found in " + ScenePath);
                return;
            }

            builder.EditorBakeIntoScene();

            int baked = 0;
            for (int i = 0; i < builder.transform.childCount; i++)
            {
                var child = builder.transform.GetChild(i);
                if (child.name == "GeneratedMap") baked = child.childCount;
            }

            EditorUtility.SetDirty(builder);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log($"MapBaker: baked {baked} objects into {ScenePath} and disabled runtime rebuild.");
        }
    }
}
