using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CathayCrossing.Customization
{
    /// <summary>
    /// Renders a single rotating 3D part model into a RenderTexture and drives
    /// a <see cref="RawImage"/> with it. Each instance builds its own off-screen
    /// rig (model + camera) far from the scene origin so thumbnails never see
    /// each other or the main preview — no dedicated layers needed.
    ///
    /// Used by the customise scene for both the category tab icons (head / body)
    /// and the per-variant option boxes. Pass the source character's FBX body
    /// plus the part names for the slot; the renderer isolates those meshes,
    /// frames a camera on them, and spins the model so it reads as a live 3D
    /// preview that fills the option box.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class PartThumbnailRenderer : MonoBehaviour
    {
        public float rotateSpeed = 45f;
        public int textureSize = 256;
        public Color background = new Color(0.18f, 0.18f, 0.22f, 1f);

        // Hand every rig a fresh, well-separated world slot so cameras stay
        // isolated without dedicated culling layers. Far clip (below) keeps a
        // camera from ever seeing its neighbours.
        static int _rigCount;
        static readonly Vector3 RigOrigin = new Vector3(12000f, 0f, 0f);
        const float RigSpacing = 200f;

        RawImage _img;
        RenderTexture _rt;
        Camera _cam;
        GameObject _rig;
        Transform _spin;
        bool _built;

        /// <summary>
        /// (Re)build the thumbnail. <paramref name="partNames"/> null/empty keeps
        /// the whole model; otherwise only meshes whose GameObject name matches
        /// are shown (so the Head tab frames just the head, etc.).
        /// </summary>
        public void Build(GameObject sourceBody, string[] partNames)
        {
            Dispose();

            _img = GetComponent<RawImage>();
            if (sourceBody == null) return;

            Vector3 rigPos = RigOrigin + new Vector3((_rigCount++) * RigSpacing, 0f, 0f);

            _rig = new GameObject("ThumbRig");
            _rig.transform.position = rigPos;
            _rig.hideFlags = HideFlags.HideAndDontSave;

            _spin = new GameObject("Spin").transform;
            _spin.SetParent(_rig.transform, false);

            var model = Instantiate(sourceBody, _spin);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            // Hold the bind pose — these raw FBX bodies carry no runtime
            // controller, and a live Animator would only jitter the framing.
            var anim = model.GetComponentInChildren<Animator>();
            if (anim != null) anim.enabled = false;

            IsolateParts(model, partNames);

            Bounds b;
            if (!TryGetVisibleBounds(model, out b)) b = new Bounds(rigPos, Vector3.one);

            // Centre the visible parts on the spin pivot so rotation stays put.
            Vector3 shift = rigPos - b.center;
            model.transform.position += shift;
            b.center += shift;

            // Camera (square RT → vertical FOV == horizontal).
            var camGo = new GameObject("ThumbCam");
            camGo.transform.SetParent(_rig.transform, false);
            _cam = camGo.AddComponent<Camera>();
            EnsureUrpCameraData(camGo);
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = background;
            _cam.fieldOfView = 30f;
            _cam.nearClipPlane = 0.01f;
            _cam.farClipPlane = 50f;
            _cam.cullingMask = ~0;
            _cam.allowHDR = false;
            _cam.allowMSAA = false;

            float radius = Mathf.Max(0.05f, b.extents.magnitude);
            float halfFov = _cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float dist = radius / Mathf.Sin(halfFov) * 1.05f;

            // Slightly raised front view; the model spins so the exact start
            // angle is cosmetic.
            Vector3 dir = new Vector3(0f, 0.12f, -1f).normalized;
            camGo.transform.position = b.center - dir * dist;
            camGo.transform.LookAt(b.center);

            _rt = new RenderTexture(textureSize, textureSize, 16, RenderTextureFormat.ARGB32);
            _rt.antiAliasing = 1;
            _rt.Create();
            _cam.targetTexture = _rt;

            _img.texture = _rt;
            _img.color = Color.white;

            _built = true;
        }

        void Update()
        {
            if (_built && _spin != null)
                _spin.Rotate(0f, rotateSpeed * Time.deltaTime, 0f, Space.World);
        }

        static void IsolateParts(GameObject model, string[] partNames)
        {
            if (partNames == null || partNames.Length == 0) return;
            var keep = new HashSet<string>(partNames);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = keep.Contains(r.gameObject.name);
                var smr = r as SkinnedMeshRenderer;
                if (smr != null) smr.updateWhenOffscreen = true;
            }
        }

        static bool TryGetVisibleBounds(GameObject model, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var smr = r as SkinnedMeshRenderer;
                if (smr != null) smr.updateWhenOffscreen = true;
                if (!found) { bounds = r.bounds; found = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return found;
        }

        // URP needs a UniversalAdditionalCameraData alongside every Camera.
        // Added via reflection so this script doesn't take a compile-time
        // dependency on the URP assembly.
        static void EnsureUrpCameraData(GameObject camGo)
        {
            var t = System.Type.GetType(
                "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (t != null && camGo.GetComponent(t) == null) camGo.AddComponent(t);
        }

        void OnDestroy() => Dispose();

        void Dispose()
        {
            if (_cam != null) _cam.targetTexture = null;
            if (_img != null) _img.texture = null;
            if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; }
            if (_rig != null) { Destroy(_rig); _rig = null; }
            _built = false;
        }
    }
}
