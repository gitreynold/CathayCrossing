using UnityEngine;
using UnityEngine.InputSystem;

namespace CathayCrossing.Bootstrap
{
    /// <summary>
    /// R-key control for the Afa-ride-horse NPC.
    ///
    ///   • While wandering, press R → the horse gallops to the player, runs one
    ///     full circle around them, then stops dead in FRONT of the player
    ///     (facing them, animation frozen).
    ///   • Press R again → it resumes free roaming via NpcWanderController.
    ///
    /// The lap angle only advances while the horse is physically keeping up
    /// with its slot on the ring, so the lap is real, not virtual. When desks
    /// or walls block the ring the horse sidesteps to slip around corners and,
    /// if an arc is truly impassable, reverses direction and sweeps the other
    /// way until a full 360° worth of arc has been covered.
    ///
    /// While this script drives the horse the NpcWanderController is disabled,
    /// so the two never fight over the CharacterController.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class AfaRideHorseSummon : MonoBehaviour
    {
        [Header("Summon run")]
        [Tooltip("Lap radius around the player. Kept tight so the ring stays on open floor next to them.")]
        public float circleRadius = 1.2f;
        public float runSpeed = 3.6f;
        public float turnSpeed = 12f;

        [Header("Stop in front")]
        [Tooltip("How far in front of the player the horse parks itself.")]
        public float stopDistanceInFront = 1.6f;
        public float arriveThreshold = 0.2f;
        [Tooltip("If, mid-lap, the horse's angle around the player comes within " +
                 "this many degrees of the player's front, it stops circling and " +
                 "settles in front right away — no full lap required.")]
        [Range(1f, 90f)] public float frontAngleTolerance = 20f;
        [Tooltip("Normalized time of the gallop clip to freeze on when parked — " +
                 "chosen so all four hooves are planted on the ground.")]
        [Range(0f, 1f)] public float idleFreezePoint = 0.75f;

        [Header("Visual")]
        public Transform visual;
        public float visualYawOffset = 0f;

        [Header("Robustness")]
        [Tooltip("Safety: if a phase is blocked longer than this, skip to the next one.")]
        public float circleTimeout = 12f;
        [Tooltip("If the lap stalls this long (slot unreachable), reverse the lap direction.")]
        public float reverseAfterStall = 0.8f;
        public int maxReversals = 4;

        enum State { Wander, ToCircle, Circling, ToFront, Idle }
        State _state = State.Wander;

        CharacterController _cc;
        NpcWanderController _wander;
        Animator _animator;
        Transform _player;

        float _angle;        // current polar slot angle around the player (radians)
        float _swept;        // |arc| completed of the lap
        int   _dir = 1;      // lap direction, flips when blocked
        int   _reversals;
        float _gateStall;    // time the slot has been out of reach
        float _stateTime;

        // sidestep steering used to slip around desk corners
        float _moveStall;
        float _sideSign = 1f;
        float _sideUntil;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _wander = GetComponent<NpcWanderController>();
        }

        void Start()
        {
            if (visual == null) visual = transform.Find("Visual");
            if (visual != null && _animator == null)
                _animator = visual.GetComponentInChildren<Animator>();
        }

        void Update()
        {
            // Clamp dt so unfocused-editor frame spikes can't fast-forward the
            // lap angle or the timeouts.
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            _stateTime += dt;

            var kb = Keyboard.current;
            if (kb != null && kb.rKey.wasPressedThisFrame)
            {
                if (_state == State.Wander)
                {
                    _player = FindPlayer();
                    if (_player != null)
                    {
                        TriggerPlayerWhistle();   // summoning → player whistles
                        Begin(State.ToCircle);
                    }
                }
                else
                {
                    ResumeWander();   // releasing — no whistle, just roam again
                }
            }

            switch (_state)
            {
                case State.ToCircle:
                {
                    if (_player == null) { ResumeWander(); break; }
                    // run to the nearest point on the ring around the player
                    Vector3 fromPlayer = Flat(transform.position - _player.position);
                    if (fromPlayer.sqrMagnitude < 0.01f) fromPlayer = -Flat(_player.forward);
                    Vector3 entry = _player.position + fromPlayer.normalized * circleRadius;
                    if (MoveTowards(entry, dt) || _stateTime > circleTimeout)
                    {
                        _angle = PolarAngle();
                        _swept = 0f;
                        _dir = 1;
                        _reversals = 0;
                        _gateStall = 0f;
                        Begin(State.Circling);
                    }
                    break;
                }
                case State.Circling:
                {
                    if (_player == null) { ResumeWander(); break; }

                    Vector3 slot = RingPoint(_angle);
                    bool keepingUp = Flat(slot - transform.position).magnitude < circleRadius * 0.6f;
                    if (keepingUp)
                    {
                        float angStep = (runSpeed / Mathf.Max(0.5f, circleRadius)) * dt;
                        _angle += _dir * angStep;
                        _swept += angStep;
                        _gateStall = 0f;
                    }
                    else
                    {
                        _gateStall += dt;
                        if (_gateStall > reverseAfterStall)
                        {
                            _gateStall = 0f;
                            if (_reversals < maxReversals)
                            {
                                // arc is blocked — sweep the other way instead
                                _reversals++;
                                _dir = -_dir;
                                _angle = PolarAngle();   // re-anchor to where the horse really is
                            }
                            else
                            {
                                Begin(State.ToFront);
                                break;
                            }
                        }
                    }

                    MoveTowards(RingPoint(_angle), dt, runSpeed * 1.5f);

                    // Short-circuit: if the horse is already in front of the
                    // player, settle there now instead of finishing the lap.
                    Vector3 frontDir = Flat(PlayerFacing());
                    if (frontDir.sqrMagnitude > 0.01f)
                    {
                        float frontAngle = Mathf.Atan2(frontDir.z, frontDir.x) * Mathf.Rad2Deg;
                        float curAngle   = PolarAngle() * Mathf.Rad2Deg;
                        if (Mathf.Abs(Mathf.DeltaAngle(curAngle, frontAngle)) <= frontAngleTolerance)
                        {
                            Begin(State.ToFront);
                            break;
                        }
                    }

                    if (_swept >= Mathf.PI * 2f || _stateTime > circleTimeout)
                        Begin(State.ToFront);
                    break;
                }
                case State.ToFront:
                {
                    if (_player == null) { ResumeWander(); break; }
                    // Use the player's VISUAL facing (OctopathPlayerController
                    // rotates a sprite-root child, not the root transform).
                    Vector3 fwd = Flat(PlayerFacing());
                    if (fwd.sqrMagnitude < 0.01f) fwd = Flat(transform.position - _player.position).normalized;
                    Vector3 spot = _player.position + fwd.normalized * stopDistanceInFront;
                    if (MoveTowards(spot, dt) || _stateTime > circleTimeout)
                    {
                        Begin(State.Idle);
                        FreezeAllHoovesDown();
                    }
                    break;
                }
                case State.Idle:
                {
                    // parked — just keep facing the player
                    if (_player != null)
                        Face(_player.position - transform.position, dt);
                    break;
                }
            }
        }

        void Begin(State s)
        {
            _state = s;
            _stateTime = 0f;
            _moveStall = 0f;
            _sideUntil = 0f;
            if (s != State.Wander && _wander != null) _wander.enabled = false;
            if (_animator != null && s != State.Idle) _animator.speed = 1f;
        }

        void ResumeWander()
        {
            _state = State.Wander;
            _stateTime = 0f;
            if (_animator != null) _animator.speed = 1f;
            if (_wander != null) _wander.enabled = true;
        }

        Vector3 RingPoint(float angle) =>
            _player.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * circleRadius;

        float PolarAngle()
        {
            Vector3 d = Flat(transform.position - _player.position);
            return Mathf.Atan2(d.z, d.x);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// Move horizontally toward target at speed; returns true when arrived.
        /// When physically blocked, briefly steers sideways (alternating sides)
        /// to slip around furniture corners.
        bool MoveTowards(Vector3 target, float dt, float speedOverride = -1f)
        {
            float spd = speedOverride > 0f ? speedOverride : runSpeed;
            Vector3 to = Flat(target - transform.position);
            float dist = to.magnitude;
            if (dist < arriveThreshold) return true;

            Vector3 dir = to.normalized;
            if (Time.time < _sideUntil)
                dir = (dir + _sideSign * new Vector3(dir.z, 0f, -dir.x) * 1.4f).normalized;

            Vector3 step = dir * Mathf.Min(spd * dt, dist);
            Vector3 before = transform.position;
            _cc.Move(step);
            SnapToFloor();

            float actual = Flat(transform.position - before).magnitude;
            if (actual < step.magnitude * 0.3f) _moveStall += dt; else _moveStall = 0f;
            if (_moveStall > 0.5f)
            {
                _moveStall = 0f;
                _sideSign = -_sideSign;
                _sideUntil = Time.time + 0.8f;
            }

            Face(dir, dt);
            return false;
        }

        void Face(Vector3 dir, float dt)
        {
            dir.y = 0f;
            if (visual == null || dir.sqrMagnitude < 0.0001f) return;
            Quaternion want = Quaternion.LookRotation(dir.normalized, Vector3.up)
                              * Quaternion.Euler(0f, visualYawOffset, 0f);
            visual.rotation = Quaternion.Slerp(visual.rotation, want,
                                               1f - Mathf.Exp(-turnSpeed * dt));
        }

        void SnapToFloor()
        {
            Vector3 p = transform.position;
            if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 10f) &&
                hit.collider.name.StartsWith("Floor_Tile"))
                transform.position = new Vector3(p.x, hit.point.y, p.z);
        }

        /// The player's actual look direction. OctopathPlayerController turns a
        /// visual child (sprite root / model), so read the Animator's transform
        /// rather than the root, which never rotates.
        Vector3 PlayerFacing()
        {
            if (_player == null) return Vector3.forward;
            var anim = _player.GetComponentInChildren<Animator>();
            return anim != null ? anim.transform.forward : _player.forward;
        }

        /// Freeze the gallop clip on the frame where all four hooves touch the
        /// floor, so the parked horse stands naturally instead of mid-leap.
        void FreezeAllHoovesDown()
        {
            if (_animator == null) return;
            _animator.Play(0, 0, idleFreezePoint);   // jump default state to the grounded frame
            _animator.Update(0f);                     // evaluate the pose now
            _animator.speed = 0f;                     // and hold it
        }

        /// Plays the shared "Whistle" one-shot on the player (added to every
        /// PlayerAnimator controller). Only fired when SUMMONING the horse.
        void TriggerPlayerWhistle()
        {
            if (_player == null) return;
            var anim = _player.GetComponentInChildren<Animator>();
            if (anim != null && anim.runtimeAnimatorController != null)
                anim.SetTrigger("Whistle");

            // Play the whistle SFX (louder than the scene BGM) alongside the
            // animation. The controller owns the 2D AudioSource.
            var ctrl = _player.GetComponentInChildren<CathayCrossing.HD2D.OctopathPlayerController>();
            if (ctrl == null) ctrl = _player.GetComponentInParent<CathayCrossing.HD2D.OctopathPlayerController>();
            if (ctrl != null) ctrl.PlayWhistleSfx();
        }

        static Transform FindPlayer()
        {
            var go = GameObject.Find("__OfficePlayer");
            if (go == null) go = GameObject.FindGameObjectWithTag("Player");
            return go != null ? go.transform : null;
        }
    }
}
