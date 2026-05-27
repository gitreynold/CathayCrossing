using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace CathayCrossing.HD2D
{
    /// HD-2D style camera: tilted perspective look-down with smooth follow,
    /// optional zoom and orbit, mimicking Octopath Traveler's diorama feel.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class OctopathCamera : MonoBehaviour
    {
        [Header("Target")]
        public Transform target;
        public Vector3 targetOffset = new Vector3(0f, 1.0f, 0f);

        [Header("Framing")]
        [Tooltip("Tilt angle (degrees) — Octopath uses ~30-40°")]
        [Range(15f, 75f)] public float pitch = 33f;
        [Range(-180f, 180f)] public float yaw = 0f;
        [Tooltip("Distance from target along the look vector")]
        public float distance = 14f;
        public float minDistance = 6f;
        public float maxDistance = 26f;

        [Header("Lens (perspective)")]
        [Tooltip("Low FOV gives the diorama / tilt-shift look")]
        [Range(10f, 60f)] public float fov = 22f;

        [Header("Smoothing")]
        public float positionSmoothTime = 0.18f;
        public float rotationSmoothTime = 0.12f;

        [Header("Input")]
        public bool allowMouseOrbit = true;
        [Tooltip("Degrees per pixel of mouse delta while right-click " +
                 "dragging. Matches the customize-scene PreviewCameraOrbit " +
                 "feel: no frame-rate-dependent multiplication, no extra " +
                 "smoothing — the cursor 'pushes' the camera 1:1.")]
        public float dragSensitivity = 0.5f;
        public bool allowScrollZoom = true;
        public float zoomSpeed = 6f;
        // Kept for backwards compatibility with inspector-serialized
        // OctopathCamera components in older scenes. Not used at runtime
        // any more — the new dragSensitivity field replaces it.
        [HideInInspector] public float orbitSpeed = 120f;

        [Tooltip("When true, right-click drag only rotates yaw; pitch stays " +
                 "at its current value (i.e. the camera's vertical tilt is " +
                 "locked). Used by the office scene to fix the camera at a " +
                 "fixed downward look while still letting the player orbit " +
                 "horizontally.")]
        public bool lockPitch = false;

        Camera _cam;
        Vector3 _posVel;
        float _yawVel, _pitchVel;
        float _yawTarget, _pitchTarget, _distTarget;
        bool _dragging;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            _cam.orthographic = false;
            _cam.fieldOfView = fov;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 200f;
            SyncTargetsFromFields();
        }

        /// Re-seed the smoothing targets from the public yaw/pitch/distance
        /// fields. Call this after a script writes those fields post-Awake,
        /// otherwise LateUpdate will drag the camera back to the values
        /// captured on the first frame.
        public void SyncTargetsFromFields()
        {
            _yawTarget = yaw;
            _pitchTarget = pitch;
            _distTarget = distance;
        }

        void OnValidate()
        {
            if (_cam == null) _cam = GetComponent<Camera>();
            if (_cam != null)
            {
                _cam.orthographic = false;
                _cam.fieldOfView = fov;
            }
        }

        void LateUpdate()
        {
            HandleInput();

            // SmoothDampAngle divides by smoothTime internally; when
            // rotationSmoothTime is 0 it returns NaN and the rotation
            // gets stuck on the first frame (mouse drag has no effect
            // because yaw becomes NaN and SmoothDampAngle in the next
            // frame poisons _yawTarget too). Skip smoothing when 0.
            if (rotationSmoothTime > 1e-4f)
            {
                yaw = Mathf.SmoothDampAngle(yaw, _yawTarget, ref _yawVel, rotationSmoothTime);
                pitch = Mathf.SmoothDampAngle(pitch, _pitchTarget, ref _pitchVel, rotationSmoothTime);
            }
            else
            {
                yaw = _yawTarget;
                pitch = _pitchTarget;
                _yawVel = 0f;
                _pitchVel = 0f;
            }
            distance = Mathf.Lerp(distance, _distTarget, 1f - Mathf.Exp(-8f * Time.deltaTime));

            if (_cam.fieldOfView != fov) _cam.fieldOfView = fov;

            // Position + look math matches PreviewCameraOrbit exactly so
            // the office and customize scenes feel identical when the
            // player drags. Customize works:
            //   pos = focus + rot * (0, 0, -distance)
            //   transform.LookAt(focus)
            // No SmoothDamp on the position — any rotation jitter would
            // otherwise lag behind the target's tiny per-frame moves
            // (player idle sway / Animator root drift) and visually
            // look like the camera was wobbling.
            Vector3 focus = (target != null ? target.position : Vector3.zero) + targetOffset;
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 offset = rot * new Vector3(0f, 0f, -distance);
            Vector3 desiredPos = focus + offset;

            if (positionSmoothTime > 1e-4f)
            {
                transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref _posVel, positionSmoothTime);
            }
            else
            {
                transform.position = desiredPos;
                _posVel = Vector3.zero;
            }
            // LookAt instead of assigning rot directly — same final
            // orientation, but immune to accidental roll drift if any
            // future code path nudges yaw/pitch off-axis.
            transform.LookAt(focus);
        }

        void HandleInput()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            // Match PreviewCameraOrbit's input model:
            //   • Either left OR right mouse button starts a drag.
            //   • Drag continues until both buttons are released.
            //   • Pointer-over-UI on the press frame cancels the drag
            //     so HUD buttons stay clickable.
            // Previously we required rightButton specifically, which
            // didn't match what players expected after using the
            // customize scene's preview where any drag works.
            if (allowMouseOrbit)
            {
                if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
                {
                    if (!IsPointerOverUi()) _dragging = true;
                }
                if (!mouse.leftButton.isPressed && !mouse.rightButton.isPressed)
                {
                    _dragging = false;
                }
                if (_dragging)
                {
                    // Direct delta × sensitivity. 1 pixel of mouse
                    // movement → dragSensitivity degrees of yaw/pitch.
                    // No Time.deltaTime — would make it frame-rate
                    // dependent.
                    Vector2 d = mouse.delta.ReadValue();
                    _yawTarget += d.x * dragSensitivity;
                    if (!lockPitch)
                    {
                        _pitchTarget = Mathf.Clamp(_pitchTarget - d.y * dragSensitivity, 15f, 75f);
                    }
                }
            }
            else
            {
                _dragging = false;
            }

            if (allowScrollZoom)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    _distTarget = Mathf.Clamp(_distTarget - Mathf.Sign(scroll) * zoomSpeed * 0.5f, minDistance, maxDistance);
                }
            }
        }

        // Lets HUD / UI overlays consume clicks without yanking the
        // camera. No EventSystem in the scene → returns false (we just
        // accept all clicks). Matches PreviewCameraOrbit's helper.
        static bool IsPointerOverUi()
        {
            var es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject();
        }
    }
}
