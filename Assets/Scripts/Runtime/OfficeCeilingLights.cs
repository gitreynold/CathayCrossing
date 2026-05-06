using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CathayCrossing.HD2D
{
    /// <summary>
    /// Procedurally spawns a grid of invisible Point Lights to act as ceiling
    /// fluorescent panels for an indoor office. The grid is recalculated from
    /// roomSize + spacing, so the same component can cover any room footprint
    /// — change the inspector values and the lights re-pack themselves.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class OfficeCeilingLights : MonoBehaviour
    {
        [Header("Room Footprint (local space, centred on this transform)")]
        [Tooltip("X = width, Y = depth (along Z axis)")]
        public Vector2 roomSize = new Vector2(20f, 12f);

        [Tooltip("Distance from the floor (this transform's Y) up to where the lights sit.")]
        public float ceilingHeight = 3.3f;

        [Tooltip("Approximate distance between adjacent lights. Final count is rounded to keep an integer grid.")]
        public float spacing = 5f;

        [Header("Light Settings")]
        public Color color = new Color(1.0f, 0.98f, 0.94f);
        public float intensity = 4.5f;
        public float range = 9f;

        [Header("Editor")]
        [Tooltip("Rebuild automatically when fields change in the inspector.")]
        public bool autoRebuild = true;

        const string ChildPrefix = "FluorLight_";

#if UNITY_EDITOR
        void OnValidate()
        {
            if (!autoRebuild) return;
            // Defer: Unity disallows hierarchy mutation inside OnValidate.
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                Rebuild();
            };
        }
#endif

        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            ClearChildren();

            int cols = Mathf.Max(1, Mathf.RoundToInt(roomSize.x / Mathf.Max(0.01f, spacing)));
            int rows = Mathf.Max(1, Mathf.RoundToInt(roomSize.y / Mathf.Max(0.01f, spacing)));

            float stepX = cols == 1 ? 0f : roomSize.x / cols;
            float stepZ = rows == 1 ? 0f : roomSize.y / rows;
            float startX = -roomSize.x * 0.5f + stepX * 0.5f;
            float startZ = -roomSize.y * 0.5f + stepZ * 0.5f;
            if (cols == 1) startX = 0f;
            if (rows == 1) startZ = 0f;

            int idx = 0;
            for (int z = 0; z < rows; z++)
            for (int x = 0; x < cols; x++)
            {
                var go = new GameObject(ChildPrefix + idx);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(
                    startX + stepX * x,
                    ceilingHeight,
                    startZ + stepZ * z);

                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = color;
                light.intensity = intensity;
                light.range = range;
                light.shadows = LightShadows.None;
                idx++;
            }
        }

        void ClearChildren()
        {
            var doomed = new List<GameObject>();
            for (int i = 0; i < transform.childCount; i++)
            {
                var c = transform.GetChild(i);
                if (c.name.StartsWith(ChildPrefix)) doomed.Add(c.gameObject);
            }
            foreach (var go in doomed)
            {
                if (Application.isPlaying) Destroy(go);
                else DestroyImmediate(go);
            }
        }
    }
}
