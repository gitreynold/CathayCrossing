using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CathayCrossing.Bootstrap
{
    /// <summary>
    /// Runtime room setup for the office scene:
    ///   • Adds a BoxCollider that fits the renderer bounds onto every
    ///     Desk_*/Chair_* root that doesn't already have one.
    ///   • Lifts ambient light and drops a few ceiling Point Lights so the
    ///     interior reads as an indoor office instead of an open lot under
    ///     the lone Directional Light.
    ///
    /// Called from OfficePlayerSpawner.SpawnInto AFTER the 180° pivot rotation
    /// so the lights stay aligned with the desks.
    /// </summary>
    public static class OfficeRoomSetup
    {
        const float RoomHeight   = 3.0f;

        public static void Apply(Scene scene)
        {
            AddFurnitureColliders(scene);
            ConfigureIndoorLighting(scene);
        }

        // ─── Furniture colliders ───────────────────────────────────────────
        static void AddFurnitureColliders(Scene scene)
        {
            int added = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive: false))
                {
                    string n = t.name;
                    if (!(n.StartsWith("Desk_") || n.StartsWith("Chair_"))) continue;
                    if (t.GetComponent<Collider>() != null) continue;

                    var renderers = t.GetComponentsInChildren<Renderer>(includeInactive: false);
                    if (renderers.Length == 0) continue;

                    Bounds worldBounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) worldBounds.Encapsulate(renderers[i].bounds);

                    var box = t.gameObject.AddComponent<BoxCollider>();
                    box.center = t.InverseTransformPoint(worldBounds.center);

                    Vector3 ls = t.lossyScale;
                    box.size = new Vector3(
                        worldBounds.size.x / Mathf.Max(0.0001f, Mathf.Abs(ls.x)),
                        worldBounds.size.y / Mathf.Max(0.0001f, Mathf.Abs(ls.y)),
                        worldBounds.size.z / Mathf.Max(0.0001f, Mathf.Abs(ls.z))
                    );
                    added++;
                }
            }
            Debug.Log($"[OfficeRoomSetup] Added BoxCollider to {added} Desk/Chair objects.");
        }

        static bool ComputeFloorBounds(Scene scene, out Bounds combined)
        {
            combined = default;
            bool found = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive: false))
                {
                    if (!t.name.StartsWith("Floor")) continue;
                    foreach (var r in t.GetComponentsInChildren<Renderer>(includeInactive: false))
                    {
                        if (!found) { combined = r.bounds; found = true; }
                        else combined.Encapsulate(r.bounds);
                    }
                }
            }
            return found;
        }

        // ─── Indoor lighting ───────────────────────────────────────────────
        static void ConfigureIndoorLighting(Scene scene)
        {
            // Warmer, brighter ambient so shaded sides of furniture aren't pitch black.
            RenderSettings.ambientMode      = AmbientMode.Flat;
            RenderSettings.ambientLight     = new Color(0.55f, 0.55f, 0.60f);
            RenderSettings.ambientIntensity = 1.0f;

            if (!ComputeFloorBounds(scene, out Bounds floor)) return;

            var lightRoot = new GameObject("__OfficeCeilingLights");
            SceneManager.MoveGameObjectToScene(lightRoot, scene);

            float ceilingY = floor.min.y + RoomHeight - 0.2f;
            // 3 × 2 grid of ceiling point lights covering the floor.
            const int cols = 3, rows = 2;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    float u = (c + 0.5f) / cols;
                    float v = (r + 0.5f) / rows;
                    Vector3 pos = new Vector3(
                        Mathf.Lerp(floor.min.x, floor.max.x, u),
                        ceilingY,
                        Mathf.Lerp(floor.min.z, floor.max.z, v)
                    );
                    CreateCeilingLight(lightRoot.transform, $"CeilingLight_{r}_{c}", pos);
                }
            }
        }

        static void CreateCeilingLight(Transform parent, string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = pos;
            var l = go.AddComponent<Light>();
            l.type      = LightType.Point;
            l.color     = new Color(1.0f, 0.96f, 0.86f);
            l.intensity = 1.2f;
            l.range     = 8f;
            l.shadows   = LightShadows.None;
        }
    }
}
