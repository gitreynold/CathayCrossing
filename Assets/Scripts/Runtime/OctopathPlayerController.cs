using UnityEngine;
using UnityEngine.InputSystem;

namespace CathayCrossing.HD2D
{
    [DisallowMultipleComponent]
    public class OctopathPlayerController : MonoBehaviour
    {
        [Header("Movement")]
        public float moveSpeed = 5f;
        public float runMultiplier = 1.7f;
        public float acceleration = 18f;
        public float rotationSpeed = 12f;

        [Header("Refs")]
        public Transform spriteRoot;
        [Tooltip("Quad/visual that bobs up and down while walking. Usually the CharacterSprite quad.")]
        public Transform spriteVisual;

        [Header("Walk bob animation")]
        public float bobAmplitude = 0.07f;
        [Tooltip("Step bumps per second when moving at full walk speed.")]
        public float stepsPerSecond = 6f;

        [Header("Collision")]
        public float colliderHeight = 1.6f;
        public float colliderRadius = 0.35f;
        public float gravity = -20f;

        Vector3 _velocity;
        // Zero until the player first moves; the DirectionalBillboardSprite treats
        // a zero facing as "face the camera" (front view).
        Vector3 _facing = Vector3.zero;
        float _bobPhase;
        float _baseSpriteY = float.NaN;
        CharacterController _controller;
        float _verticalVelocity;

        void Reset()
        {
            spriteRoot = transform;
        }

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (_controller == null)
            {
                _controller = gameObject.AddComponent<CharacterController>();
                _controller.height = colliderHeight;
                _controller.radius = colliderRadius;
                _controller.center = new Vector3(0f, colliderHeight * 0.5f, 0f);
                _controller.skinWidth = 0.04f;
                _controller.minMoveDistance = 0f;
                _controller.stepOffset = 0.2f;
            }
        }

        void Update()
        {
            Vector2 input = ReadInput();
            bool running = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;

            // Camera-relative movement on the XZ plane
            Vector3 fwd = Vector3.forward;
            Vector3 right = Vector3.right;
            if (Camera.main != null)
            {
                fwd = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
                right = Vector3.ProjectOnPlane(Camera.main.transform.right, Vector3.up).normalized;
            }

            Vector3 desired = (right * input.x + fwd * input.y);
            if (desired.sqrMagnitude > 1f) desired.Normalize();

            float targetSpeed = moveSpeed * (running ? runMultiplier : 1f);
            Vector3 targetVel = desired * targetSpeed;

            _velocity = Vector3.MoveTowards(_velocity, targetVel, acceleration * Time.deltaTime);

            if (_controller.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            else _verticalVelocity += gravity * Time.deltaTime;

            Vector3 motion = _velocity;
            motion.y = _verticalVelocity;
            _controller.Move(motion * Time.deltaTime);

            if (desired.sqrMagnitude > 0.01f) _facing = desired;

            if (spriteRoot != null && _facing.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(_facing, Vector3.up);
                spriteRoot.rotation = Quaternion.Slerp(spriteRoot.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }

            UpdateWalkBob();
        }

        void UpdateWalkBob()
        {
            if (spriteVisual == null) return;
            if (float.IsNaN(_baseSpriteY)) _baseSpriteY = spriteVisual.localPosition.y;

            float speedRatio = (moveSpeed > 0.01f) ? _velocity.magnitude / moveSpeed : 0f;
            // Phase advances proportionally to speed so running steps faster.
            _bobPhase += stepsPerSecond * Mathf.PI * Time.deltaTime * Mathf.Min(speedRatio, 2f);

            float bob = Mathf.Abs(Mathf.Sin(_bobPhase)) * bobAmplitude * Mathf.Clamp01(speedRatio);

            Vector3 lp = spriteVisual.localPosition;
            lp.y = _baseSpriteY + bob;
            spriteVisual.localPosition = lp;
        }

        static Vector2 ReadInput()
        {
            var kb = Keyboard.current;
            if (kb == null) return Vector2.zero;
            float x = 0, y = 0;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) y -= 1;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) y += 1;
            return new Vector2(x, y);
        }

        public Vector3 Velocity => _velocity;
        public Vector3 Facing => _facing;
    }
}
