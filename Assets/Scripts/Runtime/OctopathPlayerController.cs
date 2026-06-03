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

        [Header("Sit / Typing")]
        [Tooltip("Toggle key: from standing → sit down (and stay seated); " +
                 "while seated → stand back up. All other movement/action keys " +
                 "are locked out while seated, except the typing key.")]
        public Key sitKey = Key.G;
        [Tooltip("While seated, starts the continuous typing loop. Has no " +
                 "effect when standing.")]
        public Key typeKey = Key.T;

        [Tooltip("Only let the player sit when within this distance of a chair. " +
                 "Set <= 0 to disable the proximity gate.")]
        public float sitRange = 1.5f;
        [Tooltip("A GameObject counts as a chair seat when it carries this " +
                 "tag. The tag must exist in the Tag Manager.")]
        public string chairTag = "Chair";


        [Header("Collision")]
        public float colliderHeight = 1.6f;
        public float colliderRadius = 0.35f;
        public float gravity = -20f;

        [Header("Network")]
        public bool isLocalPlayer = true;
        [HideInInspector] public Vector3 targetPosition;
        [HideInInspector] public float targetRotationY;

        Vector3 _velocity;
        Vector3 _lastPosition;
        float _remoteSpeed;
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
        static readonly int SitHash        = Animator.StringToHash("Sit");
        static readonly int TypingHash     = Animator.StringToHash("Typing");
        // Layer 0 state hashes for the action clips. Compared against
        // Animator.GetCurrentAnimatorStateInfo / GetNextAnimatorStateInfo so
        // we can suppress movement while the character is mid-performance.
        static readonly int WavingStateHash = Animator.StringToHash("Waving");
        static readonly int DanceStateHash  = Animator.StringToHash("Dance");
        // Seated states — used to keep movement/actions locked through the
        // whole sit-down / seated / typing / stand-up sequence.
        static readonly int SitDownStateHash   = Animator.StringToHash("SitDown");
        static readonly int SitTypingStateHash = Animator.StringToHash("SitTyping");
        static readonly int SitStandStateHash  = Animator.StringToHash("SitStand");

        // Seated control state. _sitMode is true from the moment G is pressed
        // to sit until G is pressed again to stand. _typing tracks the typing
        // loop (only meaningful while seated). _seatedLocked is recomputed each
        // frame and gates movement + the H/F action keys.
        bool _sitMode;
        bool _typing;
        bool _seatedLocked;

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
            if (isLocalPlayer)
            {
                UpdateLocalPlayer();
            }
            else
            {
                UpdateRemotePlayer();
            }
        }

        void UpdateLocalPlayer()
        {
            Vector2 input = ReadInput();
            bool running = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;

            _runningInput = running && input.sqrMagnitude > 0.01f;

            // G/T sit + typing handling. Recomputes _seatedLocked so movement
            // and the H/F action keys stay disabled through the whole seated
            // sequence (sit-down → seated → typing → stand-up).
            HandleSeatedInput();

            if (IsPerformingAction() || _seatedLocked)
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

            SyncPositionWithServer();

            if (desired.sqrMagnitude > 0.01f) _facing = desired;

            if (spriteRoot != null && _facing.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(_facing, Vector3.up);
                spriteRoot.rotation = Quaternion.Slerp(spriteRoot.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }

            UpdateAnimator();
            UpdateWalkBob();
        }

        void UpdateRemotePlayer()
        {
            // 1. 平滑移動到目標位置 (解決瞬間移動)
            // 使用 Lerp 讓位置更新更平滑，15f 是平滑係數，數值越大越跟腳，數值越小越平滑
            Vector3 nextPos = Vector3.Lerp(transform.position, targetPosition, 15f * Time.deltaTime);
            
            // 2. 計算這一幀實際移動的位移量來驅動動畫
            Vector3 moveDelta = nextPos - transform.position;
            transform.position = nextPos;

            // 3. 平滑轉向目標角度
            if (spriteRoot != null)
            {
                Quaternion targetRot = Quaternion.Euler(0, targetRotationY, 0);
                spriteRoot.rotation = Quaternion.Slerp(spriteRoot.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }

            // 4. 計算速度值供 Animator 使用
            // 將每幀位移轉換為每秒速度
            float currentFrameSpeed = moveDelta.magnitude / Time.deltaTime;
            _remoteSpeed = Mathf.Lerp(_remoteSpeed, currentFrameSpeed, 10f * Time.deltaTime);
            
            // 模擬 OctopathPlayerController 原本使用的 _velocity.magnitude
            _velocity = transform.forward * _remoteSpeed;

            UpdateAnimator();
            UpdateWalkBob();
        }

        // --- 多人連線同步變數 ---
        private float _lastSyncTime;
        private Vector3 _lastSyncedPos;
        private const float SyncInterval = 0.05f; // 每秒發送 20 次更新

        private void SyncPositionWithServer()
        {
            if (CathayCrossing.Network.NetworkManager.Instance == null) return;
            
            // 如果移動距離極小且時間沒到，就不發送
            if (Time.time - _lastSyncTime < SyncInterval) return;
            if (Vector3.Distance(transform.position, _lastSyncedPos) < 0.01f) return;

            _lastSyncTime = Time.time;
            _lastSyncedPos = transform.position;
            
            CathayCrossing.Network.NetworkManager.Instance.SendMove(transform.position, spriteRoot.eulerAngles.y);
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

            // Wave/Dance are locked out while seated (or transitioning in/out
            // of the seat). Only G (stand up) and T (typing) work then — those
            // are handled in HandleSeatedInput().
            if (_seatedLocked) return;

            var kb = Keyboard.current;
            if (kb != null && kb[greetKey].wasPressedThisFrame)
            {
                animator.SetTrigger(WaveHash);
                CathayCrossing.Network.NetworkManager.Instance?.SendAction("WAVE");
            }
            if (kb != null && kb[danceKey].wasPressedThisFrame)
            {
                animator.SetTrigger(DanceHash);
                CathayCrossing.Network.NetworkManager.Instance?.SendAction("DANCE");
            }
        }

        // G/T handling for the local player. Drives the Animator's Sit/Typing
        // bools and keeps _seatedLocked in sync so the rest of Update() knows
        // to suppress movement and the other action keys.
        //
        //   G (standing) → sit down, then hold the seated pose indefinitely.
        //   T (seated)   → start the continuous typing loop.
        //   G (seated/typing) → stand back up; control unlocks once the
        //                       stand-up clip finishes (SitStand → Idle).
        void HandleSeatedInput()
        {
            if (animator == null) { _seatedLocked = false; return; }

            var kb = Keyboard.current;
            bool inSeatedState = IsInSeatedState();

            if (kb != null && kb[sitKey].wasPressedThisFrame)
            {
                if (!_sitMode)
                {
                    // Stand → sit. Ignore if we're still mid stand-up so a
                    // stray G doesn't bounce the character back down.
                    if (!inSeatedState && IsNearChair())
                    {
                        _sitMode = true;
                        _typing  = false;
                        animator.SetBool(SitHash, true);
                        animator.SetBool(TypingHash, false);
                        CathayCrossing.Network.NetworkManager.Instance?.SendAction("SIT");
                    }
                }
                else
                {
                    // Seated/typing → stand up.
                    _sitMode = false;
                    _typing  = false;
                    animator.SetBool(SitHash, false);
                    animator.SetBool(TypingHash, false);
                    CathayCrossing.Network.NetworkManager.Instance?.SendAction("STAND");
                }
            }

            // T only matters while seated — start the typing loop.
            if (_sitMode && !_typing && kb != null && kb[typeKey].wasPressedThisFrame)
            {
                _typing = true;
                animator.SetBool(TypingHash, true);
                CathayCrossing.Network.NetworkManager.Instance?.SendAction("TYPE");
            }

            // Locked while we intend to be seated OR while any seated clip is
            // still playing (covers the stand-up tail after G is released).
            _seatedLocked = _sitMode || IsInSeatedState();
        }

        // True while the Animator is in (or transitioning into) any seated
        // state: sit-down, seated typing, or stand-up.
        bool IsInSeatedState()
        {
            if (animator == null) return false;
            var cur = animator.GetCurrentAnimatorStateInfo(0);
            if (cur.shortNameHash == SitDownStateHash
                || cur.shortNameHash == SitTypingStateHash
                || cur.shortNameHash == SitStandStateHash) return true;
            if (animator.IsInTransition(0))
            {
                var nxt = animator.GetNextAnimatorStateInfo(0);
                if (nxt.shortNameHash == SitDownStateHash
                    || nxt.shortNameHash == SitTypingStateHash
                    || nxt.shortNameHash == SitStandStateHash) return true;
            }
            return false;
        }

        // True when a chair (GameObject whose name contains chairNameContains)
        // sits within sitRange of the player. Only evaluated on the G keypress
        // that initiates sitting, so the per-frame cost is zero. A non-positive
        // sitRange disables the gate (sit anywhere).
        // True when a GameObject tagged chairTag sits within sitRange of the
        // player. Only evaluated on the G keypress that initiates sitting, so
        // the per-frame cost is zero. A non-positive sitRange disables the
        // gate (sit anywhere).
        bool IsNearChair()
        {
            if (sitRange <= 0f || string.IsNullOrEmpty(chairTag)) return true;

            GameObject[] chairs;
            try { chairs = GameObject.FindGameObjectsWithTag(chairTag); }
            catch (UnityException) { return true; } // tag not defined → don't block

            float bestSqr = sitRange * sitRange;
            Vector3 me = transform.position;
            for (int i = 0; i < chairs.Length; i++)
            {
                // Compare on the XZ plane so chair height doesn't matter.
                Vector3 d = chairs[i].transform.position - me; d.y = 0f;
                if (d.sqrMagnitude <= bestSqr) return true;
            }
            return false;
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

        // ─── Public seated hooks (used by NetworkManager for remote players
        //     and by UI buttons). These only drive the Animator bools; the
        //     movement lock only runs for the local player in Update().
        public void Sit()
        {
            _sitMode = true;
            _typing  = false;
            if (animator != null) { animator.SetBool(SitHash, true); animator.SetBool(TypingHash, false); }
        }

        public void StartTyping()
        {
            if (!_sitMode) return;
            _typing = true;
            if (animator != null) animator.SetBool(TypingHash, true);
        }

        public void StandUp()
        {
            _sitMode = false;
            _typing  = false;
            if (animator != null) { animator.SetBool(SitHash, false); animator.SetBool(TypingHash, false); }
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
