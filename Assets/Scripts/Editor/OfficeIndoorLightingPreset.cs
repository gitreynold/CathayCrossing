using CathayCrossing.HD2D;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CathayCrossing.HD2D.EditorTools
{
    /// <summary>
    /// One-shot preset that converts the active scene from "outdoor sunlight"
    /// to "indoor office": softens the directional key, raises ambient with a
    /// Trilight gradient, and ensures an OfficeCeilingLights rig exists so the
    /// room gets even overhead fill independent of the directional light.
    /// </summary>
    public static class OfficeIndoorLightingPreset
    {
        const string CeilingRigName = "CeilingLights";

        [MenuItem("Tools/Octopath/Apply Indoor Lighting Preset")]
        public static void Apply()
        {
            var scene = EditorSceneManager.GetActiveScene();

            TuneDirectionalLight();
            ApplyAmbient();
            EnsureCeilingRig();

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[Octopath] Indoor lighting preset applied. Adjust CeilingLights → roomSize/spacing as the play area changes.");
        }

        static void TuneDirectionalLight()
        {
            Light directional = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l != null && l.type == LightType.Directional)
                {
                    directional = l;
                    break;
                }
            }
            if (directional == null) return;

            Undo.RecordObject(directional, "Tune Directional Light");
            directional.color = new Color(0.88f, 0.94f, 1.0f); // cool window tint
            directional.intensity = 0.3f;
            directional.shadows = LightShadows.Soft;
            directional.shadowStrength = 0.3f;
            EditorUtility.SetDirty(directional);
        }

        static void ApplyAmbient()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.64f, 0.68f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.55f, 0.55f);
            RenderSettings.ambientGroundColor = new Color(0.34f, 0.34f, 0.34f);
            RenderSettings.ambientIntensity = 1.0f;
            RenderSettings.fog = false;
        }

        static void EnsureCeilingRig()
        {
            var existing = Object.FindFirstObjectByType<OfficeCeilingLights>();
            if (existing != null)
            {
                existing.Rebuild();
                return;
            }

            var parent = GameObject.Find("Environment");
            var rigGO = new GameObject(CeilingRigName);
            Undo.RegisterCreatedObjectUndo(rigGO, "Create CeilingLights Rig");
            if (parent != null) rigGO.transform.SetParent(parent.transform, false);
            rigGO.transform.localPosition = Vector3.zero;

            var rig = rigGO.AddComponent<OfficeCeilingLights>();
            rig.Rebuild();
            Selection.activeGameObject = rigGO;
        }
    }
}
