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
        // 2026-05-26 v11: now consumes Mouse.position deltas computed
        // here, not Mouse.delta.ReadValue() — Unity InputSystem scales
        // the latter by an internal factor (~0.3–0.5×), which is why
        // older versions had sensitivity values pushed up to 60 just
        // to feel snappy. Position-based deltas are raw screen pixels,
        // so a sensitivity of 1.0 means "1° rotation per pixel of
        // mouse movement". A 90-pixel drag now spins 90°. Bump up if
        // you want flicks; bump down for finer control.
        public float dragSensitivity = 1.0f;
        public float scrollSensitivity = 0.25f;

        [Header("Diagnostics")]
        [Tooltip("Logs Mouse.delta vs the computed position-based delta " +
                 "once per frame during drag, so you can see the InputSystem " +
                 "scaling factor in Console. Turn off in production.")]
        public bool logDeltaDiagnostics = false;

        bool _dragging;
        Vector2 _prevMousePos;

        void LateUpdate()
        {
            HandleInput();
            ApplyTransform();
        }

        void HandleInput()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 currentPos = mouse.position.ReadValue();

            // Press → start dragging, but ignore if pointer is over UI so the
            // variant buttons stay clickable. Reset _prevMousePos so the
            // first frame's delta is 0 (not "wherever the cursor moved while
            // we weren't dragging").
            if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
            {
                if (!IsPointerOverUi())
                {
                    _dragging = true;
                    _prevMousePos = currentPos;
                }
            }
            if (mouse.leftButton.wasReleasedThisFrame && mouse.rightButton.wasReleasedThisFrame)
            {
                _dragging = false;
            }
            // If both buttons came up between frames, still stop dragging.
            if (!mouse.leftButton.isPressed && !mouse.rightButton.isPressed) _dragging = false;

            if (_dragging)
            {
                // Position-based delta. Mouse.delta.ReadValue() goes
                // through InputSystem's internal scaling (~0.3-0.5×),
                // which makes high-DPI / high-polling-rate setups feel
                // sluggish. Differencing Mouse.position frame-to-frame
                // sidesteps that — we get raw screen pixels.
                Vector2 delta = currentPos - _prevMousePos;
                yaw   += delta.x * dragSensitivity;
                pitch -= delta.y * dragSensitivity;
                pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

                if (logDeltaDiagnostics)
                {
                    Vector2 inputSystemDelta = mouse.delta.ReadValue();
                    Debug.Log($"[PreviewCameraOrbit] pos-delta={delta} (mag={delta.magnitude:F2})  " +
                              $"Mouse.delta={inputSystemDelta} (mag={inputSystemDelta.magnitude:F2})  " +
                              $"ratio={(inputSystemDelta.magnitude > 0.01f ? delta.magnitude / inputSystemDelta.magnitude : 0f):F2}×");
                }
            }
            _prevMousePos = currentPos;

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
