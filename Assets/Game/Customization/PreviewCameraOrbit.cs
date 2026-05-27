using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace CathayCrossing.Customization
{
    /// <summary>
    /// Orbit camera for the customize-scene preview. Drag with the left or
    /// right mouse button anywhere on screen (except on top of UI) to rotate
    /// the camera around <see cref="target"/>; scroll wheel zooms in/out.
    ///
    /// Uses the new Input System exclusively — the project's
    /// activeInputHandler is set to Both, but this script doesn't depend on
    /// any legacy Input call, so it works whether the user runs Editor or a
    /// build with either input mode active.
    ///
    /// Yaw / pitch are stored as plain floats and re-applied every frame in
    /// LateUpdate, so any external script can poke yaw/pitch/distance and
    /// the next frame's transform will reflect it.
    /// </summary>
    public class PreviewCameraOrbit : MonoBehaviour
    {
        [Header("Target")]
        public Transform target;

        [Tooltip("Vertical offset added to target.position when looking. " +
                 "Bumps the camera focus from the floor up to the chest, " +
                 "matching how character creators usually frame the body.")]
        public Vector3 targetOffset = new Vector3(0f, 1.0f, 0f);

        [Header("Orbit")]
        [Tooltip("Starting yaw in degrees. 180 = camera looks at +Z (back of " +
                 "world), which puts the front of a +Z-facing character " +
                 "toward the camera.")]
        public float yaw = 180f;
        public float pitch = 8f;
        public float distance = 3.2f;

        [Header("Limits")]
        public float minPitch = -25f;
        public float maxPitch = 60f;
        public float minDistance = 1.5f;
        public float maxDistance = 6f;

        [Header("Feel")]
        // 2026-05-26 v3: bumped to 1.0°/px so very short drags spin the
        // preview a lot. Customize-scene rail is on the right; players
        // have less horizontal travel room when dragging from the left
        // of the screen, so a higher per-pixel rate cuts down on having
        // to re-click and re-drag for a 180° spin. Matches the office
        // camera's dragSensitivity.
        public float dragSensitivity = 1.0f;
        public float scrollSensitivity = 0.25f;

        bool _dragging;

        void LateUpdate()
        {
            HandleInput();
            ApplyTransform();
        }

        void HandleInput()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            // Press → start dragging, but ignore if pointer is over UI so the
            // variant buttons stay clickable.
            if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
            {
                if (!IsPointerOverUi()) _dragging = true;
            }
            if (mouse.leftButton.wasReleasedThisFrame && mouse.rightButton.wasReleasedThisFrame)
            {
                _dragging = false;
            }
            // If both buttons came up between frames, still stop dragging.
            if (!mouse.leftButton.isPressed && !mouse.rightButton.isPressed) _dragging = false;

            if (_dragging)
            {
                Vector2 delta = mouse.delta.ReadValue();
                yaw   += delta.x * dragSensitivity;
                pitch -= delta.y * dragSensitivity;
                pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            }

            // Scroll wheel zoom — Mouse.scroll.y is ±120 per notch.
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f && !IsPointerOverUi())
            {
                distance -= scroll * 0.01f * scrollSensitivity;
                distance = Mathf.Clamp(distance, minDistance, maxDistance);
            }
        }

        void ApplyTransform()
        {
            if (target == null) return;
            Vector3 focus = target.position + targetOffset;
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 offset = rot * new Vector3(0f, 0f, -distance);
            transform.position = focus + offset;
            transform.LookAt(focus);
        }

        static bool IsPointerOverUi()
        {
            var es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject();
        }
    }
}
