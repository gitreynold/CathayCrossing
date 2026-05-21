using UnityEngine;

namespace CathayCrossing.HD2D
{
    /// HD2D-style 4-direction billboard sprite.
    /// Y-axis billboards toward the camera, and swaps Front / Back / Side
    /// materials based on the character's facing direction relative to the
    /// camera, with horizontal flipping for left vs right.
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class DirectionalBillboardSprite : MonoBehaviour
    {
        [Header("Source of facing direction")]
        public OctopathPlayerController controller;

        [Header("Sprite refs")]
        public MeshRenderer spriteRenderer;
        public Transform spriteQuad;

        [Header("Materials by view")]
        public Material frontMaterial;
        public Material backMaterial;
        public Material sideMaterial;

        [Header("Direction thresholds")]
        [Tooltip("Half-arc (degrees) of front/back. Beyond this, side view is used.")]
        [Range(15f, 75f)] public float frontBackArc = 45f;

        Camera _cam;

        void Reset()
        {
            controller = GetComponentInParent<OctopathPlayerController>();
            spriteRenderer = GetComponentInChildren<MeshRenderer>();
            if (spriteRenderer != null) spriteQuad = spriteRenderer.transform;
        }

        void LateUpdate()
        {
            if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
            if (_cam == null) return;

            // Y-axis billboard
            Vector3 camFwd = _cam.transform.forward;
            camFwd.y = 0f;
            if (camFwd.sqrMagnitude < 1e-6f) return;
            camFwd.Normalize();
            transform.rotation = Quaternion.LookRotation(camFwd, Vector3.up);

            if (spriteRenderer == null) return;

            Vector3 facing = controller != null ? controller.Facing : -camFwd;
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-6f) facing = -camFwd;
            facing.Normalize();

            Vector3 camRight = Vector3.Cross(Vector3.up, camFwd).normalized;
            float dotF = Vector3.Dot(facing, camFwd);   // +1 = facing same way as camera (away) → back view
            float dotR = Vector3.Dot(facing, camRight); // +1 = facing camera-right, −1 = camera-left

            float threshold = Mathf.Cos(frontBackArc * Mathf.Deg2Rad);

            Material chosen;
            bool flipX = false;
            if (dotF >= threshold)        chosen = backMaterial;
            else if (dotF <= -threshold)  chosen = frontMaterial;
            else
            {
                chosen = sideMaterial;
                flipX = dotR < 0f;
            }

            if (chosen != null && spriteRenderer.sharedMaterial != chosen)
                spriteRenderer.sharedMaterial = chosen;

            if (spriteQuad != null)
            {
                Vector3 s = spriteQuad.localScale;
                float absX = Mathf.Abs(s.x);
                s.x = flipX ? -absX : absX;
                if (spriteQuad.localScale != s) spriteQuad.localScale = s;
            }
        }
    }
}
