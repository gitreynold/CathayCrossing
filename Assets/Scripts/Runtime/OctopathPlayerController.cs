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

        [Header("Dance Music")]
        [Tooltip("One-shot track that plays whenever this character dances. " +
                 "Auto-loaded from Resources/Audio/Velvet_Sidewalk_Dance when " +
                 "left null. Plays as a 2D source so it sits on top of the scene " +
                 "BGM regardless of distance.")]
        public AudioClip danceMusic;
        [Tooltip("Volume for the dance music. Kept above the scene BGM (0.6) so " +
                 "it clearly stands out while dancing.")]
        [Range(0f, 1f)] public float danceMusicVolume = 1f;
        [Tooltip("Resources path the dance clip is loaded from when danceMusic " +
                 "is not assigned in the inspector.")]
        public string danceMusicResourcePath = "Audio/Velvet_Sidewalk_Dance";
        AudioSource _danceAudio;
        // Dance-music watchdog state. While _danceMusicActive is true the
        // controller tracks the Animator's Dance state each frame so it can cut
        // the track the moment another action (wave/whistle/sit/…) interrupts
        // the dance. A dance that runs to its natural end is left to finish.
        bool  _danceMusicActive;
        bool  _danceStateReached;
        float _danceLastNorm;
        float _danceWatchGrace;
        [Tooltip("How far into the Dance clip (0–1) still counts as 'cancelled' " +
                 "if the character leaves the Dance state. Leaving past this point " +
                 "is treated as a natural finish and the music tail is allowed to " +
                 "play out.")]
        [Range(0f, 1f)] public float danceCancelThreshold = 0.9f;

        [Header("Whistle SFX")]
        [Tooltip("One-shot whistle sound played when this character whistles to " +
                 "summon the horse (R key, via AfaRideHorseSummon). Auto-loaded " +
                 "from Resources/Audio/Calling_Whistle when left null. 2D source " +
                 "so it sits on top of the scene BGM.")]
        public AudioClip whistleSfx;
        [Tooltip("Volume for the whistle SFX. Kept above the scene BGM (0.6) so " +
                 "it clearly stands out.")]
        [Range(0f, 1f)] public float whistleVolume = 1f;
        [Tooltip("Resources path the whistle clip is loaded from when whistleSfx " +
                 "is not assigned in the inspector.")]
        public string whistleResourcePath = "Audio/Calling_Whistle";
        AudioSource _whistleAudio;

        [Header("Sit / Typing")]
        [Tooltip("Toggle key: from standing → sit down (and stay seated); " +
                 "while seated → stand back up. All other movement/action keys " +
                 "are locked out while seated, except the typing key.")]
        public Key sitKey = Key.G;
        [Tooltip("While seated, starts the continuous typing loop. Has no " +
                 "effect when standing.")]
        public Key typeKey = Key.O;

        [Tooltip("Only let the player sit when within this distance of a chair. " +
                 "Set <= 0 to disable the proximity gate.")]
        public float sitRange = 1.5f;
        [Tooltip("A GameObject counts as a chair seat when it carries this " +
                 "tag. The tag must exist in the Tag Manager.")]
        public string chairTag = "Chair";

        [Tooltip("Turn speed (deg/sec) while aligning to the chair before sitting.")]
        public float seatTurnSpeed = 540f;
        [Tooltip("Stop distance from the chair center that counts as 'arrived'.")]
        public float seatArriveDistance = 0.05f;
        [Tooltip("Max angle (deg) from the chair facing that counts as aligned.")]
        public float seatAlignAngle = 4f;
        [Tooltip("Yaw offset (deg) added to the chair's facing when the player " +
                 "sits. Use 180 if the character ends up facing the wrong way.")]
        public float seatYawOffset = 180f;
        [Tooltip("Stand position offset relative to the chair, in the chair's " +
                 "local space (X = chair right, Z = chair forward). The player " +
                 "walks to chair.position + this offset before sitting. " +
                 "Leave (0,0,0) to stop at the chair center.")]
        public Vector3 seatLocalOffset = new Vector3(0f, 0f, -0.2f);
        [Tooltip("Tag of the object whose yaw the player aligns to when sitting " +
                 "(e.g. the desk+chair set). Falls back to the chair's own yaw " +
                 "if no tagged object is nearby. Empty = use the chair.")]
        public string seatAlignTag = "Desk_Set";

        bool _approaching;
        Transform _approachChair;



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
        static readonly int WhistleStateHash = Animator.StringToHash("Whistle");
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

            SetupDanceAudio();
            SetupWhistleAudio();
        }

        // Build the dedicated 2D AudioSource that plays the dance track. The
        // player is assembled entirely in code by OfficePlayerSpawner (no
        // prefab), so we create the source here and pull the clip from
        // Resources when the inspector field is empty.
        void SetupDanceAudio()
        {
            if (danceMusic == null && !string.IsNullOrEmpty(danceMusicResourcePath))
            {
                danceMusic = Resources.Load<AudioClip>(danceMusicResourcePath);
                if (danceMusic == null)
                {
                    Debug.LogWarning($"[OctopathPlayerController] Dance music not found at " +
                                     $"Resources/{danceMusicResourcePath}. Dance will be silent.");
                }
            }

            _danceAudio = gameObject.AddComponent<AudioSource>();
            _danceAudio.clip          = danceMusic;
            _danceAudio.loop          = false;   // 18s one-shot, ~matches the 17.3s dance clip
            _danceAudio.playOnAwake   = false;
            _danceAudio.spatialBlend  = 0f;      // 2D — sits on top of the BGM
            _danceAudio.volume        = danceMusicVolume; // > BGM (0.6)
        }

        // Plays the dance track from the top. Restarts cleanly if the player
        // re-triggers a dance before the previous track finishes.
        void PlayDanceMusic()
        {
            if (_danceAudio == null || _danceAudio.clip == null) return;
            _danceAudio.volume = danceMusicVolume;
            _danceAudio.Play();
            // Arm the watchdog so MonitorDanceMusic() can cut the track if the
            // dance is cancelled before it finishes.
            _danceMusicActive  = true;
            _danceStateReached = false;
            _danceLastNorm     = 0f;
            _danceWatchGrace   = 0f;
        }

        // True while the Animator is in (or transitioning into) the Dance state.
        // Reports the clip's normalized playback position so the watchdog can
        // tell an early cancel from a natural finish.
        bool IsInDanceState(out float normalizedTime)
        {
            normalizedTime = 0f;
            if (animator == null) return false;
            var cur = animator.GetCurrentAnimatorStateInfo(0);
            if (cur.shortNameHash == DanceStateHash)
            {
                normalizedTime = cur.normalizedTime;
                return true;
            }
            if (animator.IsInTransition(0))
            {
                var nxt = animator.GetNextAnimatorStateInfo(0);
                if (nxt.shortNameHash == DanceStateHash)
                {
                    normalizedTime = nxt.normalizedTime;
                    return true;
                }
            }
            return false;
        }

        // Cuts the dance music when the dance is interrupted by another action.
        // Lets a dance that reaches its natural end keep its short music tail.
        void MonitorDanceMusic()
        {
            if (!_danceMusicActive) return;
            if (_danceAudio == null) { _danceMusicActive = false; return; }

            bool inDance = IsInDanceState(out float norm);
            if (inDance)
            {
                _danceStateReached = true;
                _danceLastNorm     = norm;
                return;
            }

            if (!_danceStateReached)
            {
                // Still blending into the Dance state right after the trigger —
                // wait briefly before deciding anything.
                _danceWatchGrace += Time.deltaTime;
                if (_danceWatchGrace > 0.5f) _danceMusicActive = false; // safety
                return;
            }

            // We were dancing and have now left the Dance state. If we left
            // before the clip's tail, another action cancelled it → cut the
            // music. Otherwise it finished naturally → leave the tail playing.
            if (_danceLastNorm < danceCancelThreshold && _danceAudio.isPlaying)
                _danceAudio.Stop();

            _danceMusicActive = false;
        }

        // Build the dedicated 2D AudioSource for the whistle SFX. Mirrors the
        // dance-audio setup: created in code because the player is assembled at
        // runtime with no prefab, clip pulled from Resources when unset.
        void SetupWhistleAudio()
        {
            if (whistleSfx == null && !string.IsNullOrEmpty(whistleResourcePath))
            {
                whistleSfx = Resources.Load<AudioClip>(whistleResourcePath);
                if (whistleSfx == null)
                {
                    Debug.LogWarning($"[OctopathPlayerController] Whistle SFX not found at " +
                                     $"Resources/{whistleResourcePath}. Whistle will be silent.");
                }
            }

            _whistleAudio = gameObject.AddComponent<AudioSource>();
            _whistleAudio.clip         = whistleSfx;
            _whistleAudio.loop         = false;
            _whistleAudio.playOnAwake  = false;
            _whistleAudio.spatialBlend = 0f;            // 2D — sits on top of the BGM
            _whistleAudio.volume       = whistleVolume; // > BGM (0.6)
        }

        // Public hook fired by AfaRideHorseSummon when the player whistles to
        // summon the horse (R key). Plays the whistle SFX from the top.
        public void PlayWhistleSfx()
        {
            if (_whistleAudio == null || _whistleAudio.clip == null) return;
            _whistleAudio.volume = whistleVolume;
            _whistleAudio.Play();
        }

        void Update()
        {
            // Runs for both local and remote avatars so anyone's dance music is
            // cut when their dance is interrupted.
            MonitorDanceMusic();

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
            // Walk-to-seat sequence in progress: drive it, skip normal input.
            if (_approaching)
            {
                UpdateApproach();
                UpdateAnimator();
                UpdateWalkBob();
                return;
            }

            Vector2 input = ReadInput();
            bool running = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;

            _runningInput = running && input.sqrMagnitude > 0.01f;

            // G/O sit + typing handling. Recomputes _seatedLocked so movement
            // and the H/F action keys stay disabled through the whole seated
            // sequence (sit-down → seated → typing → stand-up).
            HandleSeatedInput();

            // 聊天輸入框開著時（ChatInputUI.IsTyping），WASD 是在打字，
            // 不能變成移動 —— 跟動作中/就座中一樣把輸入歸零。
            if (IsPerformingAction() || _seatedLocked || ChatInputUI.IsTyping)
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

            // Skip steering toward _facing while seated/locked — the seated
            // pose already aligned spriteRoot to the Desk_Set and this would
            // pull it back toward the walk-in direction.
            if (!_seatedLocked && spriteRoot != null && _facing.sqrMagnitude > 0.001f)
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
            // of the seat) and while typing in the chat input. Only G (stand
            // up) and O (typing) work when seated — handled in HandleSeatedInput().
            if (_seatedLocked || ChatInputUI.IsTyping) return;

            var kb = Keyboard.current;
            if (kb != null && kb[greetKey].wasPressedThisFrame)
            {
                animator.SetTrigger(WaveHash);
                CathayCrossing.Network.NetworkManager.Instance?.SendAction("WAVE");
            }
            if (kb != null && kb[danceKey].wasPressedThisFrame)
            {
                animator.SetTrigger(DanceHash);
                PlayDanceMusic();
                CathayCrossing.Network.NetworkManager.Instance?.SendAction("DANCE");
            }
        }

        // G/T handling for the local player. Drives the Animator's Sit/Typing
        // bools and keeps _seatedLocked in sync so the rest of Update() knows
        // to suppress movement and the other action keys.
        //
        //   G (standing) → sit down, then hold the seated pose indefinitely.
        //   O (seated)   → start the continuous typing loop.
        //   G (seated/typing) → stand back up; control unlocks once the
        //                       stand-up clip finishes (SitStand → Idle).
void HandleSeatedInput()
        {
            if (animator == null) { _seatedLocked = false; return; }

            // While the computer screen UI or the chat input is open, swallow
            // ALL action keys so typing can't trigger sit/stand/etc.
            // The UIs close themselves with ESC (or ✕ / Enter).
            if (ComputerScreenUI.IsOpen || ChatInputUI.IsTyping)
            {
                _seatedLocked = _approaching || _sitMode || IsInSeatedState();
                return;
            }

            var kb = Keyboard.current;
            bool inSeatedState = IsInSeatedState();

            if (kb != null && kb[sitKey].wasPressedThisFrame)
            {
                if (!_sitMode && !_approaching)
                {
                    if (!inSeatedState)
                    {
                        var chair = FindNearestChair();
                        if (chair != null)
                        {
                            _approaching   = true;
                            _approachChair = chair;

                            Vector3 worldOff = transform.position - chair.position;
                            worldOff.y = 0f;
                            float dbgYaw = chair.eulerAngles.y;
                            if (!string.IsNullOrEmpty(seatAlignTag))
                            {
                                var dbgT = FindNearestWithTag(seatAlignTag, chair.position);
                                if (dbgT != null) dbgYaw = dbgT.eulerAngles.y;
                            }
                            Vector3 localOff = Quaternion.Inverse(
                                Quaternion.Euler(0f, dbgYaw, 0f)) * worldOff;
                            Debug.Log($"[Sit] seatLocalOffset for '{chair.name}': " +
                                      $"({localOff.x:F3}, 0, {localOff.z:F3})");
                        }
                    }
                }
                else if (_sitMode)
                {
                    // Seated/typing -> stand up. Also drop the code screen.
                    var deskSetOff = FindNearestWithTag(seatAlignTag, transform.position);
                    if (deskSetOff != null)
                    {
                        Transform deskOff = deskSetOff.Find("Office_Desk_01");
                        CodeGenScreen.Hide(deskOff != null ? deskOff : deskSetOff);
                    }
                    _sitMode = false;
                    _typing  = false;
                    animator.SetBool(SitHash, false);
                    animator.SetBool(TypingHash, false);
                    CathayCrossing.Network.NetworkManager.Instance?.SendAction("STAND");
                    ComputerScreenUI.Hide();
                }
            }

            // O only works while seated AND with a Desk_Set object nearby.
            // Starts the typing loop and toggles the code-generation screen on
            // the desk's monitor.
            if (_sitMode && kb != null && kb[typeKey].wasPressedThisFrame)
            {
                var deskSet = FindNearestWithTag(seatAlignTag, transform.position);
                bool nearDesk = deskSet != null;
                if (nearDesk && sitRange > 0f)
                {
                    Vector3 d = deskSet.position - transform.position; d.y = 0f;
                    nearDesk = d.sqrMagnitude <= sitRange * sitRange;
                }
                if (nearDesk)
                {
                    if (!_typing)
                    {
                        _typing = true;
                        animator.SetBool(TypingHash, true);
                        CathayCrossing.Network.NetworkManager.Instance?.SendAction("TYPE");
                        // (UI opening moved below — fires on every O press)
                    }
                    ComputerScreenUI.Show(animator);
                    Transform desk = deskSet.Find("Office_Desk_01");
                    if (desk == null) desk = deskSet;
                    CodeGenScreen.Toggle(desk, spriteRoot != null ? spriteRoot : transform);
                }
            }

            _seatedLocked = _approaching || _sitMode || IsInSeatedState();
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
        // Returns the closest GameObject tagged chairTag within sitRange on the
        // XZ plane, or null if none. sitRange <= 0 means unlimited range. Only
        // called on the G keypress that initiates sitting.
        Transform FindNearestChair()
        {
            if (string.IsNullOrEmpty(chairTag)) return null;

            GameObject[] chairs;
            try { chairs = GameObject.FindGameObjectsWithTag(chairTag); }
            catch (UnityException) { return null; } // tag not defined

            float range = sitRange > 0f ? sitRange : float.MaxValue;
            float bestSqr = range * range;
            Transform best = null;
            Vector3 me = transform.position;
            for (int i = 0; i < chairs.Length; i++)
            {
                Vector3 d = chairs[i].transform.position - me; d.y = 0f;
                float s = d.sqrMagnitude;
                if (s <= bestSqr) { bestSqr = s; best = chairs[i].transform; }
            }
            return best;
        }

        // Nearest object carrying `tag` to `origin` on the XZ plane, or null.
        Transform FindNearestWithTag(string tag, Vector3 origin)
        {
            GameObject[] hits;
            try { hits = GameObject.FindGameObjectsWithTag(tag); }
            catch (UnityException) { return null; }

            float bestSqr = float.MaxValue;
            Transform best = null;
            for (int i = 0; i < hits.Length; i++)
            {
                Vector3 d = hits[i].transform.position - origin; d.y = 0f;
                float s = d.sqrMagnitude;
                if (s < bestSqr) { bestSqr = s; best = hits[i].transform; }
            }
            return best;
        }


        // Drives the walk-to-seat sequence kicked off by G near a chair:
        //   1) turn in place to match the chair's facing,
        //   2) walk to the chair's center (XZ),
        //   3) snap to center and start the sit-down animation.
void UpdateApproach()
        {
            if (_approachChair == null) { _approaching = false; return; }

            float chairYaw = _approachChair.eulerAngles.y;
            float alignYaw = chairYaw;
            if (!string.IsNullOrEmpty(seatAlignTag))
            {
                var alignT = FindNearestWithTag(seatAlignTag, _approachChair.position);
                if (alignT != null) alignYaw = alignT.eulerAngles.y;
            }
            Quaternion chairRot = Quaternion.Euler(0f, alignYaw + seatYawOffset, 0f);

            Vector3 seatPos = _approachChair.position
                            + Quaternion.Euler(0f, alignYaw, 0f) * seatLocalOffset;

            // Phase 1: rotate to the chair's orientation before moving.
            if (spriteRoot != null)
            {
                float angle = Quaternion.Angle(spriteRoot.rotation, chairRot);
                if (angle > seatAlignAngle)
                {
                    spriteRoot.rotation = Quaternion.RotateTowards(
                        spriteRoot.rotation, chairRot, seatTurnSpeed * Time.deltaTime);
                    _velocity = Vector3.zero; // idle pose while turning
                    ApplyGravityOnly();
                    return;
                }
                spriteRoot.rotation = chairRot;
            }

            // Phase 2: walk to the seat stand position on the XZ plane. The
            // desk now has a solid collider, so a CharacterController.Move
            // would get blocked before reaching the seat. This approach is a
            // fully scripted move to a known point, so we drive the transform
            // directly (controller disabled) to slide past the desk collider.
            Vector3 me = transform.position;
            Vector3 toTarget = seatPos - me; toTarget.y = 0f;
            float dist = toTarget.magnitude;

            if (dist > seatArriveDistance)
            {
                Vector3 dir = toTarget / Mathf.Max(dist, 1e-4f);
                _velocity = dir * moveSpeed; // feeds the walk animation
                float step = Mathf.Min(moveSpeed * Time.deltaTime, dist);
                Vector3 next = me + dir * step;
                _controller.enabled = false;
                transform.position = new Vector3(next.x, me.y, next.z);
                _controller.enabled = true;
                return;
            }

            // Phase 3: arrived — snap to the seat position, then start sitting.
            _velocity = Vector3.zero;
            Vector3 snapped = new Vector3(seatPos.x, me.y, seatPos.z);
            _controller.enabled = false;
            transform.position = snapped;
            _controller.enabled = true;

            spriteRoot.rotation = chairRot;
            _facing = chairRot * Vector3.forward;

            _approaching   = false;
            _approachChair = null;
            _sitMode       = true;
            _typing        = false;
            if (animator != null) { animator.SetBool(SitHash, true); animator.SetBool(TypingHash, false); }
            CathayCrossing.Network.NetworkManager.Instance?.SendAction("SIT");
        }

        void ApplyGravityOnly()
        {
            if (_controller == null) return;
            if (_controller.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            else _verticalVelocity += gravity * Time.deltaTime;
            _controller.Move(new Vector3(0f, _verticalVelocity, 0f) * Time.deltaTime);
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
            // Also drives the remote-player path (NetworkManager calls this on
            // other players' avatars), so their dance music plays locally too.
            if (animator != null) animator.SetTrigger(DanceHash);
            PlayDanceMusic();
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
        // action state — currently Waving, Dance, or Whistle — on the base
        // layer. Movement and rotation are suppressed for that window so the
        // player doesn't slide while greeting, dancing, or whistling for the
        // horse.
        bool IsPerformingAction()
        {
            if (animator == null) return false;
            var cur = animator.GetCurrentAnimatorStateInfo(0);
            if (cur.shortNameHash == WavingStateHash || cur.shortNameHash == DanceStateHash
                || cur.shortNameHash == WhistleStateHash) return true;
            if (animator.IsInTransition(0))
            {
                var nxt = animator.GetNextAnimatorStateInfo(0);
                if (nxt.shortNameHash == WavingStateHash || nxt.shortNameHash == DanceStateHash
                    || nxt.shortNameHash == WhistleStateHash) return true;
            }
            return false;
        }

        public Vector3 Velocity => _velocity;
        public Vector3 Facing => _facing;
    }
}
