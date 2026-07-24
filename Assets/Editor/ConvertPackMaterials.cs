using UnityEditor;
using UnityEngine;

namespace ClutchFPS.EditorTools
{
    /// Converts imported asset-pack materials from the Built-in Standard shader
    /// to URP Lit, remapping the texture/colour slots so they stop rendering
    /// magenta under URP. Skips anything already on a URP shader and particle
    /// materials (which need their own shaders). Re-runnable and safe.
    public static class ConvertPackMaterials
    {
        private static readonly string[] Folders =
        {
            "Assets/RPG_FPS_game_assets_industrial",
            "Assets/Barking_Dog",
        };

        [MenuItem("Tools/ClutchFPS/Convert Pack Materials To URP")]
        public static void Convert()
        {
            var urp = Shader.Find("Universal Render Pipeline/Lit");
            if (urp == null) { Debug.LogError("ConvertPackMaterials: URP Lit shader not found."); return; }

            int converted = 0, skipped = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", Folders))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

                string shaderName = mat.shader != null ? mat.shader.name : "";
                if (shaderName.StartsWith("Universal Render Pipeline")) { skipped++; continue; }
                if (shaderName.Contains("Particle") || shaderName.Contains("Additive")
                    || path.Contains("/Particles/")) { skipped++; continue; }

                // Grab everything off the Standard material before switching.
                Texture main = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                Color color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                Texture bump = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
                Texture metalGloss = mat.HasProperty("_MetallicGlossMap") ? mat.GetTexture("_MetallicGlossMap") : null;
                Texture occlusion = mat.HasProperty("_OcclusionMap") ? mat.GetTexture("_OcclusionMap") : null;
                Texture emissionMap = mat.HasProperty("_EmissionMap") ? mat.GetTexture("_EmissionMap") : null;
                Color emissionColor = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;
                float metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;
                float glossiness = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : 0.5f;
                bool hadEmission = mat.IsKeywordEnabled("_EMISSION");

                mat.shader = urp;

                if (main != null) mat.SetTexture("_BaseMap", main);
                mat.SetColor("_BaseColor", color);
                if (bump != null) { mat.SetTexture("_BumpMap", bump); mat.EnableKeyword("_NORMALMAP"); }
                if (metalGloss != null) { mat.SetTexture("_MetallicGlossMap", metalGloss); mat.EnableKeyword("_METALLICSPECGLOSSMAP"); }
                if (occlusion != null) { mat.SetTexture("_OcclusionMap", occlusion); mat.EnableKeyword("_OCCLUSIONMAP"); }
                mat.SetFloat("_Metallic", metallic);
                mat.SetFloat("_Smoothness", glossiness);

                if (emissionMap != null || (hadEmission && emissionColor.maxColorComponent > 0f))
                {
                    if (emissionMap != null) mat.SetTexture("_EmissionMap", emissionMap);
                    mat.SetColor("_EmissionColor", emissionColor);
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }

                EditorUtility.SetDirty(mat);
                converted++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"ConvertPackMaterials: converted {converted} materials to URP Lit, skipped {skipped}.");
        }
    }
}
