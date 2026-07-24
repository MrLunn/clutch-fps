using UnityEditor;
using UnityEngine;

namespace ClutchFPS.EditorTools
{
    /// Grit pass: pushes the map's environment materials darker, more
    /// desaturated and dead-matte so the complex reads grimy rather than clean.
    /// Reads each material's own baseline first so it's safe to re-run without
    /// compounding. Leaves glass, neon accents and weapon metal alone.
    public static class GritPass
    {
        // How far to go. Tune these and re-run.
        private const float Darken = 0.78f;     // multiply base colour
        private const float Desaturate = 0.24f; // lerp toward grey
        private const float Smoothness = 0.04f; // near-zero = dusty matte

        private static readonly string[] Materials =
        {
            "ComplexWall", "ComplexConcrete", "ComplexGround", "ComplexOffice",
            "ComplexRoof", "ComplexCrate", "ComplexMetal", "ComplexGravel",
            "ComplexCarpet", "ComplexPaint", "ContainerRed", "ContainerBlue",
            "ContainerGreen",
        };

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int Smooth = Shader.PropertyToID("_Smoothness");
        // Where we stash the untouched original colour so re-runs stay stable.
        private static readonly int GritBase = Shader.PropertyToID("_GritBaseColor");

        [MenuItem("Tools/ClutchFPS/Apply Grit Pass")]
        public static void Apply()
        {
            int touched = 0;
            foreach (var name in Materials)
            {
                var mat = FindMaterial(name);
                if (mat == null) { Debug.LogWarning("GritPass: missing " + name); continue; }

                // Capture the pristine colour once, then always grade from it.
                Color original = mat.HasProperty(GritBase) ? mat.GetColor(GritBase) : mat.GetColor(BaseColor);
                mat.SetColor(GritBase, original);

                float grey = original.r * 0.299f + original.g * 0.587f + original.b * 0.114f;
                Color graded = Color.Lerp(original, new Color(grey, grey, grey), Desaturate) * Darken;
                graded.a = 1f;

                mat.SetColor(BaseColor, graded);
                if (mat.HasProperty(Smooth)) mat.SetFloat(Smooth, Smoothness);
                EditorUtility.SetDirty(mat);
                touched++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"GritPass: graded {touched} materials (darken {Darken}, desat {Desaturate}, smooth {Smoothness}).");
        }

        private static Material FindMaterial(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets($"t:Material {name}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == name)
                    return AssetDatabase.LoadAssetAtPath<Material>(path);
            }
            return null;
        }
    }
}
