using UnityEditor;
using UnityEngine;

namespace ClutchFPS.EditorTools
{
    /// Dresses the map's environment materials with the Yughues free concrete /
    /// metal PBR sets — albedo + normal, with metallic/smoothness tuned per
    /// surface. Chosen per zone so the districts read differently: plaster in
    /// the offices, scratched metal in the warehouse, sand + corrugated metal
    /// in the container yard, spalled concrete on the walls.
    ///
    /// Baked objects update automatically since they share these materials, and
    /// the per-object StaticTile still drives tiling.
    public static class TexturePass
    {
        private const string Concrete = "Assets/YughuesFreeConcreteMaterials/Textures";
        private const string Metal = "Assets/YughuesFreeMetalMaterials/Textures";

        // material, texture-dir, texture-base, metallic, smoothness, tint rgb
        private static readonly (string mat, string dir, string tex, float metallic, float smooth, float r, float g, float b)[] Table =
        {
            ("ComplexWall",     Concrete, "T_YFCM_Spalling",   0f,   0.12f, 0.86f, 0.84f, 0.80f),
            ("ComplexConcrete", Concrete, "T_YFCM_Slab1x1",    0f,   0.12f, 0.85f, 0.85f, 0.86f),
            ("ComplexGround",   Concrete, "T_YFCM_Stamped01",  0f,   0.10f, 0.80f, 0.80f, 0.80f),
            ("ComplexOffice",   Concrete, "T_YFCM_Plastered",  0f,   0.16f, 0.88f, 0.87f, 0.86f),
            ("ComplexGravel",   Concrete, "T_YFCM_ColoredSand",0f,   0.08f, 0.82f, 0.78f, 0.70f),
            ("ComplexPaint",    Concrete, "T_YFCM_Plastered",  0f,   0.18f, 0.72f, 0.70f, 0.58f),
            ("ComplexRoof",     Metal,    "T_YFMeM_03",        0.55f,0.30f, 0.55f, 0.55f, 0.58f),
            ("ComplexMetal",    Metal,    "T_YFMeM_02",        0.65f,0.34f, 0.66f, 0.68f, 0.70f),
            ("ComplexCrate",    Metal,    "T_YFMeM_09",        0.30f,0.22f, 0.55f, 0.45f, 0.32f),
            ("ContainerRed",    Metal,    "T_YFMeM_01",        0.55f,0.30f, 0.62f, 0.24f, 0.20f),
            ("ContainerBlue",   Metal,    "T_YFMeM_01",        0.55f,0.30f, 0.20f, 0.34f, 0.60f),
            ("ContainerGreen",  Metal,    "T_YFMeM_01",        0.55f,0.30f, 0.22f, 0.44f, 0.28f),
        };

        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int BumpMap = Shader.PropertyToID("_BumpMap");
        private static readonly int BumpScale = Shader.PropertyToID("_BumpScale");
        private static readonly int Metallic = Shader.PropertyToID("_Metallic");
        private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [MenuItem("Tools/ClutchFPS/Apply Texture Pass")]
        public static void Apply()
        {
            int done = 0;
            foreach (var row in Table)
            {
                var mat = FindMaterial(row.mat);
                if (mat == null) { Debug.LogWarning("TexturePass: missing material " + row.mat); continue; }

                string dPath = $"{row.dir}/{row.tex}_d.tga";
                string nPath = $"{row.dir}/{row.tex}_n.tga";

                var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(dPath);
                if (albedo == null) { Debug.LogWarning("TexturePass: missing albedo " + dPath); continue; }
                mat.SetTexture(BaseMap, albedo);

                EnsureNormalImport(nPath);
                var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(nPath);
                if (normal != null)
                {
                    mat.SetTexture(BumpMap, normal);
                    mat.EnableKeyword("_NORMALMAP");
                    mat.SetFloat(BumpScale, 1f);
                }

                mat.SetFloat(Metallic, row.metallic);
                mat.SetFloat(Smoothness, row.smooth);
                mat.SetColor(BaseColor, new Color(row.r, row.g, row.b, 1f));
                EditorUtility.SetDirty(mat);
                done++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"TexturePass: dressed {done}/{Table.Length} materials with Yughues PBR textures.");
        }

        private static void EnsureNormalImport(string path)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter importer
                && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
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
