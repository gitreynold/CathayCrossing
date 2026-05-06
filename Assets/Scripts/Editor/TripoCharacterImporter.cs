using System.IO;
using UnityEditor;
using UnityEngine;

namespace CathayCrossing.HD2D.EditorTools
{
    /// <summary>
    /// One-click extractor for the tripo_man_1 FBX. Pulls embedded textures
    /// and materials out into sibling folders, then rewrites the extracted
    /// materials to use URP/Lit (Standard shader leaks pink/grey under URP).
    /// AI-generated FBX files (Tripo, Meshy, etc.) reliably need this
    /// post-step because their embedded material bindings drop textures
    /// when the importer auto-creates URP material proxies.
    /// </summary>
    public static class TripoCharacterImporter
    {
        const string FbxPath       = "Assets/Resources/Characters/tripo_man_1.fbx";
        const string TexturesDir   = "Assets/Resources/Characters/Textures";
        const string MaterialsDir  = "Assets/Resources/Characters/Materials";

        [MenuItem("Tools/CathayCrossing/Extract Tripo Character Assets")]
        public static void Extract()
        {
            var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"[TripoCharacterImporter] FBX not found at {FbxPath}");
                return;
            }

            EnsureFolder(TexturesDir);
            EnsureFolder(MaterialsDir);

            bool extractedTex = importer.ExtractTextures(TexturesDir);
            Debug.Log($"[TripoCharacterImporter] ExtractTextures → {extractedTex}  (target: {TexturesDir})");
            AssetDatabase.Refresh();

            int matCount = 0;
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(FbxPath))
            {
                if (sub is Material mat && AssetDatabase.IsSubAsset(mat))
                {
                    string targetPath = AssetDatabase.GenerateUniqueAssetPath($"{MaterialsDir}/{mat.name}.mat");
                    string err = AssetDatabase.ExtractAsset(mat, targetPath);
                    if (string.IsNullOrEmpty(err)) matCount++;
                    else Debug.LogWarning($"[TripoCharacterImporter] Extract '{mat.name}' failed: {err}");
                }
            }
            Debug.Log($"[TripoCharacterImporter] Extracted {matCount} materials → {MaterialsDir}");

            AssetDatabase.WriteImportSettingsIfDirty(FbxPath);
            AssetDatabase.ImportAsset(FbxPath, ImportAssetOptions.ForceUpdate);

            ConvertExtractedMaterialsToUrp();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[TripoCharacterImporter] Done. Hit Play — if mesh is still blank check Console for missing-texture warnings.");
        }

        static void ConvertExtractedMaterialsToUrp()
        {
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null)
            {
                Debug.LogWarning("[TripoCharacterImporter] URP/Lit shader missing — leaving materials on their original shader.");
                return;
            }

            int converted = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialsDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == urpLit) continue;

                Texture albedo  = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                Color   tint    = mat.HasProperty("_Color")   ? mat.GetColor("_Color")     : Color.white;
                Texture normal  = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;

                mat.shader = urpLit;

                if (albedo != null && mat.HasProperty("_BaseMap"))   mat.SetTexture("_BaseMap", albedo);
                if (mat.HasProperty("_BaseColor"))                   mat.SetColor("_BaseColor", tint);
                if (normal != null && mat.HasProperty("_BumpMap"))   mat.SetTexture("_BumpMap", normal);

                EditorUtility.SetDirty(mat);
                converted++;
            }
            Debug.Log($"[TripoCharacterImporter] Converted {converted} materials to URP/Lit");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string name   = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
