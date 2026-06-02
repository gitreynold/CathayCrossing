using UnityEngine;

namespace CathayCrossing.Bootstrap
{
    /// <summary>
    /// "Bounce" wander for an office NPC. The NPC walks in a straight heading
    /// until it runs into something — a wall, a desk, or the edge of the floor —
    /// then turns to a fresh RANDOM direction (any of up/down/left/right and
    /// everything between) and carries on. It also re-rolls its heading every
    /// few seconds so it keeps roaming instead of pacing one line.
    ///
    /// It is confined to the OFFICE AREA two ways at once:
    ///   • it only commits to a heading whose short lookahead still lands on a
    ///     walkable Floor_Tile_* inside the room bounds, and
    ///   • a CharacterController stops it dead at any wall/furniture collider,
    ///     which immediately triggers a turn.
    ///
    /// The visual child is rotated to face travel; <see cref="visualYawOffset"/>
    /// aligns the mesh's front (the horse) with the direction of motion.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class NpcWanderController : MonoBehaviour
    {
        [Header("Movement")]
        public float moveSpeed = 3f;
        public float turnSpeed = 10f;
        [Tooltip("Y the NPC sits at when no floor tile is found below it.")]
        public float defaultFloorY = 0.03f;

        [Header("Roaming")]
        [Tooltip("Re-roll the heading on its own after this many seconds, so it keeps wandering even on a clear floor.")]
        public Vector2 headingChangeInterval = new Vector2(4f, 9f);
        [Tooltip("Distance probed ahead to detect a floor edge / leaving the office before stepping there.")]
        public float lookAhead = 0.9f;
        [Tooltip("Directions sampled when choosing a new heading.")]
        public int directionSamples = 24;

        [Header("Bounds")]
        [Tooltip("Keep at least this far inside the floor edges.")]
        public float edgeMargin = 0.8f;

        [Header("Visual")]
        public Transform visual;
        [Tooltip("Yaw added to the look rotation so the mesh's front faces travel.")]
        public float visualYawOffset = 0f;

        CharacterController _cc;
        Vector3 _heading;
        bool _hitObstacle;
        Vector3 _hitNormal;
        float _nextHeadingChange;
        Vector3 _lastPos;
        float _stuckTimer;
        Bounds _bounds;
        bool _hasBounds;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
        }

        void Start()
        {
            _hasBounds = TryGetFloorBounds(out _bounds);
            SnapToFloor();
            _lastPos = transform.position;
            PickNewHeading(Vector3.zero);
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // Stuck detection — if we've barely moved while trying to, we're
            // jammed against something the collision callback didn't flag.
            float moved = (transform.position - _lastPos).magnitude;
            _stuckTimer = moved < moveSpeed * dt * 0.25f ? _stuckTimer + dt : 0f;
            _lastPos = transform.position;

            bool needTurn =
                _hitObstacle ||                       // ran into a wall / desk
                _stuckTimer > 0.35f ||                // wedged
                !StepIsInsideOffice(_heading) ||      // floor edge / room boundary ahead
                Time.time >= _nextHeadingChange;      // scheduled wander turn

            if (needTurn)
            {
                // Bounce away from whatever we hit; otherwise just pick freely.
                Vector3 prefer = _hitObstacle ? FlattenAway(_hitNormal) : -_heading;
                PickNewHeading(prefer);
                _hitObstacle = false;
                _stuckTimer = 0f;
            }

            if (visual != null && _heading.sqrMagnitude > 0.0001f)
            {
                Quaternion want = Quaternion.LookRotation(_heading, Vector3.up)
                                  * Quaternion.Euler(0f, visualYawOffset, 0f);
                visual.rotation = Quaternion.Slerp(visual.rotation, want,
                                                   1f - Mathf.Exp(-turnSpeed * dt));
            }

            _cc.Move(_heading * moveSpeed * dt);
            SnapToFloor();
        }

        // CharacterController tells us what we bumped. Treat near-vertical
        // surfaces (walls, furniture sides) as obstacles; ignore the floor.
        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (Mathf.Abs(hit.normal.y) < 0.6f)
            {
                _hitObstacle = true;
                _hitNormal = hit.normal;
            }
        }

        Vector3 FlattenAway(Vector3 n)
        {
            n.y = 0f;
            return n.sqrMagnitude > 0.0001f ? n.normalized : Vector3.zero;
        }

        // Choose a new horizontal heading. Sample many directions; keep the ones
        // that (a) still land on the office floor a step ahead and (b) don't head
        // straight back into the obstacle. Pick one at random for variety; fall
        // back to "towards room centre" if boxed in.
        void PickNewHeading(Vector3 prefer)
        {
            ScheduleNextTurn();

            Vector3 here = transform.position;
            float startAngle = Random.value * 360f;
            var valid = new System.Collections.Generic.List<Vector3>();

            for (int i = 0; i < Mathf.Max(4, directionSamples); i++)
            {
                float a = (startAngle + i * (360f / directionSamples)) * Mathf.Deg2Rad;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                if (!StepIsInsideOffice(d)) continue;
                if (prefer.sqrMagnitude > 0.0001f && Vector3.Dot(d, prefer) < -0.1f) continue;
                valid.Add(d);
            }

            if (valid.Count > 0)
            {
                _heading = valid[Random.Range(0, valid.Count)];
                return;
            }

            // Boxed in on all sides — aim back toward the centre of the floor.
            if (_hasBounds)
            {
                Vector3 toCenter = _bounds.center - here;
                toCenter.y = 0f;
                _heading = toCenter.sqrMagnitude > 0.0001f ? toCenter.normalized : Vector3.forward;
            }
            else
            {
                _heading = -_heading;
            }
        }

        void ScheduleNextTurn()
        {
            _nextHeadingChange = Time.time + Random.Range(headingChangeInterval.x, headingChangeInterval.y);
        }

        // True only if a step in `dir` keeps us on a Floor_Tile AND inside the
        // room bounds (minus margin) — i.e. still in the office.
        bool StepIsInsideOffice(Vector3 dir)
        {
            if (dir.sqrMagnitude < 0.0001f) return false;
            Vector3 probe = transform.position + dir.normalized * lookAhead;

            if (_hasBounds)
            {
                if (probe.x < _bounds.min.x + edgeMargin || probe.x > _bounds.max.x - edgeMargin ||
                    probe.z < _bounds.min.z + edgeMargin || probe.z > _bounds.max.z - edgeMargin)
                    return false;
            }

            // Must be a walkable tile directly under the look-ahead point.
            Vector3 origin = new Vector3(probe.x, probe.y + 3f, probe.z);
            var hits = Physics.RaycastAll(origin, Vector3.down, 12f);
            foreach (var h in hits)
                if (h.collider.name.StartsWith("Floor_Tile")) return true;
            return false;
        }

        // Pin the capsule's feet onto the floor tiles (flat office, no gravity).
        void SnapToFloor()
        {
            Vector3 p = transform.position;
            float footY = defaultFloorY;
            Vector3 origin = new Vector3(p.x, p.y + 3f, p.z);
            var hits = Physics.RaycastAll(origin, Vector3.down, 12f);
            float best = float.NegativeInfinity;
            foreach (var h in hits)
            {
                if (!h.collider.name.StartsWith("Floor_Tile")) continue;
                if (h.point.y > best) best = h.point.y;
            }
            if (best > float.NegativeInfinity) footY = best;

            if (!Mathf.Approximately(p.y, footY))
            {
                bool wasEnabled = _cc.enabled;
                _cc.enabled = false;
                transform.position = new Vector3(p.x, footY, p.z);
                _cc.enabled = wasEnabled;
            }
        }

        // Walkable Floor_Tile_* only — NOT the collider-less Floor_To_Ceiling
        // windows, which would balloon the bounds outward.
        bool TryGetFloorBounds(out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            var scene = gameObject.scene;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(false))
                {
                    if (!t.name.StartsWith("Floor_Tile")) continue;
                    foreach (var rend in t.GetComponentsInChildren<Renderer>(false))
                    {
                        if (!found) { bounds = rend.bounds; found = true; }
                        else bounds.Encapsulate(rend.bounds);
                    }
                }
            return found;
        }
    }
}
