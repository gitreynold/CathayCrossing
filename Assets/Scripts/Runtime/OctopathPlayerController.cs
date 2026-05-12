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
        [Tooltip("Optional. Bobs up/down while moving — used as a fake walk-cue " +
                 "for the ProceduralCharacter (primitive body). Leave null when " +
                 "an Animator is driving real walk/idle clips.")]
        public Transform spriteVisual;
        [Tooltip("Optional. Real animator on the character mesh (Tencent rigged " +
                 "FBX + PlayerAnimator.controller). Set by OfficePlayerSpawner. " +
                 "When present, drives a Speed float and a Wave trigger; the " +
                 "vertical bob is suppressed.")]
        public Animator animator;

        [Header("Walk bob animation (procedural fallback)")]
        public float bobAmplitude = 0.07f;
        [Tooltip("Step bumps per second when moving at full walk speed.")]
        public float stepsPerSecond = 6f;

        [Header("Greeting")]
        [Tooltip("Key that fires the 'Wave' trigger on the Animator.")]
        public Key greetKey = Key.H;

        [Header("Dance")]
        [Tooltip("Key that fires the 'Dance' trigger on the Animator.")]
        public Key danceKey = Key.F;

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

        // Cached Animator parameter IDs — string lookups every frame add up.
        static readonly int SpeedHash      = Animator.StringToHash("Speed");
        static readonly int WaveHash       = Animator.StringToHash("Wave");
        static readonly int DanceHash      = Animator.StringToHash("Dance");
        static readonly int IsRunningHash  = Animator.StringToHash("IsRunning");
        // Layer 0 state hashes for the action clips. Compared against
        // Animator.GetCurrentAnimatorStateInfo / GetNextAnimatorStateInfo so
        // we can suppress movement while the character is mid-performance.
        static readonly int WavingStateHash = Animator.StringToHash("Waving");
        static readonly int DanceStateHash  = Animator.StringToHash("Dance");

        // Tracked separately so UpdateAnimator() can see what ReadInput() saw
        // this frame (Shift held + WASD/arrows pressed) — that's the trigger
        // for the Animator's `IsRunning` bool.
        bool _runningInput;

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

            // Animator's `IsRunning` should only be true when the player is
            // actually moving with Shift held — Shift alone (no WASD/arrows)
            // shouldn't kick the character into the running clip.
            _runningInput = running && input.sqrMagnitude > 0.01f;

            // While the character is performing a one-shot action (Wave or
            // Dance), zero the input so the controller doesn't translate or
            // rotate this frame. Animator drives the action clip independently.
            // Check both the current state AND the next state (Animator returns
            // mid-transition info on both sides), so movement is locked the
            // instant the trigger fires too.
            if (IsPerformingAction())
            {
                input         = Vector2.zero;
                _runningInput = false;
                running       = false;
            }

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

            UpdateAnimator();
            UpdateWalkBob();
        }

        // Drive the rigged-character Animator. The procedural body has no
        // animator and is driven by the bob below instead.
        void UpdateAnimator()
        {
            if (animator == null) return;
            animator.SetFloat(SpeedHash, _velocity.magnitude);
            // Animator transitions: Idle ↔ Walking is driven by Speed alone
            // (original behaviour); IsRunning escalates Walking → Running.
            animator.SetBool(IsRunningHash, _runningInput);

            var kb = Keyboard.current;
            if (kb != null && kb[greetKey].wasPressedThisFrame)
            {
                animator.SetTrigger(WaveHash);
            }
            if (kb != null && kb[danceKey].wasPressedThisFrame)
            {
                animator.SetTrigger(DanceHash);
            }
        }

        public void Wave()
        {
            // Public hook so UI buttons / NPC interactions can also trigger Wave
            // without simulating a key press.
            if (animator != null) animator.SetTrigger(WaveHash);
        }

        public void Dance()
        {
            // Public hook so UI buttons / NPC interactions can also trigger Dance.
            if (animator != null) animator.SetTrigger(DanceHash);
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

        // True while the Animator is in (or transitioning into) any one-shot
        // action state — currently Waving or Dance — on the base layer.
        // Movement and rotation are suppressed for that window so the player
        // doesn't slide while greeting or dancing.
        bool IsPerformingAction()
        {
            if (animator == null) return false;
            var cur = animator.GetCurrentAnimatorStateInfo(0);
            if (cur.shortNameHash == WavingStateHash || cur.shortNameHash == DanceStateHash) return true;
            if (animator.IsInTransition(0))
            {
                var nxt = animator.GetNextAnimatorStateInfo(0);
                if (nxt.shortNameHash == WavingStateHash || nxt.shortNameHash == DanceStateHash) return true;
            }
            return false;
        }

        public Vector3 Velocity => _velocity;
        public Vector3 Facing => _facing;
    }
}
