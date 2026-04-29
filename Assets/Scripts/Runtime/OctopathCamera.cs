using UnityEngine;
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
        public float orbitSpeed = 120f;
        public bool allowScrollZoom = true;
        public float zoomSpeed = 6f;

        Camera _cam;
        Vector3 _posVel;
        float _yawVel, _pitchVel;
        float _yawTarget, _pitchTarget, _distTarget;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            _cam.orthographic = false;
            _cam.fieldOfView = fov;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 200f;
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

            yaw = Mathf.SmoothDampAngle(yaw, _yawTarget, ref _yawVel, rotationSmoothTime);
            pitch = Mathf.SmoothDampAngle(pitch, _pitchTarget, ref _pitchVel, rotationSmoothTime);
            distance = Mathf.Lerp(distance, _distTarget, 1f - Mathf.Exp(-8f * Time.deltaTime));

            if (_cam.fieldOfView != fov) _cam.fieldOfView = fov;

            Vector3 focus = (target != null ? target.position : Vector3.zero) + targetOffset;
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 desiredPos = focus - rot * Vector3.forward * distance;

            transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref _posVel, positionSmoothTime);
            transform.rotation = rot;
        }

        void HandleInput()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (allowMouseOrbit && mouse.rightButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue();
                _yawTarget += d.x * orbitSpeed * Time.deltaTime * 0.05f;
                _pitchTarget = Mathf.Clamp(_pitchTarget - d.y * orbitSpeed * Time.deltaTime * 0.05f, 15f, 75f);
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
    }
}
