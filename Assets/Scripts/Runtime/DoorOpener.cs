using UnityEngine;
using UnityEngine.InputSystem;

namespace CathayCrossing.HD2D
{
    /// <summary>
    /// Single swing-door opener for the Door.prefab layout (a moving Door
    /// panel + Door_Handle inside a static frame). When the player is near and
    /// presses O while the door is CLOSED, only the assigned <see cref="movingParts"/>
    /// swing open around the panel's hinge edge, opening AWAY from the player
    /// (push direction). The door auto-closes after <see cref="autoCloseDelay"/>
    /// seconds. Frames never move.
    /// </summary>
    [DisallowMultipleComponent]
    public class DoorOpener : MonoBehaviour
    {
        [Header("Parts that swing (e.g. Door, Door_Handle)")]
        [Tooltip("Only these transforms rotate. Leave the frame parts out.")]
        public Transform[] movingParts;
        [Tooltip("The door panel used to derive the hinge + facing. " +
                 "Defaults to movingParts[0].")]
        public Transform doorPanel;

        [Header("Hinge")]
        [Tooltip("Which local-X edge of the panel is the hinge: +1 = +X edge, " +
                 "-1 = -X edge. The hinge should be OPPOSITE the handle.")]
        public float hingeLocalSide = +1f;

        [Header("Open behaviour")]
        public Key openKey = Key.O;
        [Tooltip("Swing angle when fully open (degrees).")]
        public float openAngle = 95f;
        [Tooltip("Swing speed (degrees / second).")]
        public float openSpeed = 220f;
        [Tooltip("Player must be within this distance (m) of the door.")]
        public float range = 1.8f;
        [Tooltip("Seconds the door stays fully open before auto-closing.")]
        public float autoCloseDelay = 2f;
        public string playerTag = "Player";
        [Header("Auto-align")]
        [Tooltip("At runtime, snap the whole door to the nearest wall opening " +
                 "(handles maps that get rearranged/flipped after load).")]
        public bool autoAlign = false;
        [Tooltip("Max distance the door may snap sideways to reach an opening.")]
        public float maxAlignShift = 1.6f;
        [Tooltip("A gap must be at least this wide (m) to count as an opening.")]
        public float minOpeningWidth = 0.55f;

        Vector3 _hinge;       // world hinge point (constant while closed-authored)
        Vector3 _normal;      // door facing (world)
        Vector3 _freeDir;     // hinge -> free edge (world)
        float _curAngle;
        bool _open;
        float _swingSign = 1f;
        float _closeTimer;
        Collider[] _cols;       // colliders on the moving parts (toggled open/closed)

        void Start()
        {
            if (doorPanel == null && movingParts != null && movingParts.Length > 0)
                doorPanel = movingParts[0];
            if (doorPanel == null) { enabled = false; return; }

            if (autoAlign) AlignToOpening();

            // Hinge = the chosen local-X edge of the panel; axis is world up.
            float halfW = 0.5f;
            var mf = doorPanel.GetComponent<MeshFilter>();
            Vector3 panelCenter = doorPanel.position;
            if (mf != null && mf.sharedMesh != null)
            {
                var b = mf.sharedMesh.bounds;
                panelCenter = doorPanel.TransformPoint(b.center);
                halfW = b.extents.x * doorPanel.lossyScale.x;
            }
            _hinge = panelCenter + doorPanel.right * (hingeLocalSide * halfW);
            _normal = doorPanel.forward;
            _freeDir = panelCenter - _hinge; // points from hinge toward free edge

            // Cache the moving parts' colliders so we can drop them while the
            // door is open (a swept static collider doesn't reliably clear the
            // CharacterController, which is why the player got stuck).
            var cols = new System.Collections.Generic.List<Collider>();
            if (movingParts != null)
                foreach (var t in movingParts)
                    if (t != null) { var c = t.GetComponent<Collider>(); if (c != null) cols.Add(c); }
            _cols = cols.ToArray();
        }

void Update()
        {
            var kb = Keyboard.current;
            // O only OPENS, and only while the door is currently closed.
            // Typing in the chat input must not trigger the door.
            if (!_open && kb != null && kb[openKey].wasPressedThisFrame && !ChatInputUI.IsTyping)
            {
                var player = GameObject.FindWithTag(playerTag);
                if (player != null)
                {
                    Vector3 d = player.transform.position - _hinge;
                    d.y = 0f;
                    if (d.sqrMagnitude <= range * range)
                    {
                        float playerSide = Mathf.Sign(Vector3.Dot(
                            player.transform.position - _hinge, _normal));
                        Vector3 vPlus = Quaternion.AngleAxis(openAngle, Vector3.up) * _freeDir;
                        float sidePlus = Mathf.Sign(Vector3.Dot(vPlus, _normal));
                        _swingSign = Mathf.Approximately(sidePlus, -playerSide) ? +1f : -1f;
                        _open = true;
                        _closeTimer = autoCloseDelay;
                    }
                }
            }

            // Stay open while the player is nearby; only auto-close after
            // they leave, so the door never closes on someone mid-passage
            // (which would re-enable the collider on top of them).
            if (_open)
            {
                var pl = GameObject.FindWithTag(playerTag);
                bool near = false;
                if (pl != null)
                {
                    Vector3 dd = pl.transform.position - _hinge; dd.y = 0f;
                    near = dd.sqrMagnitude <= (range + 1.5f) * (range + 1.5f);
                }
                if (near) _closeTimer = autoCloseDelay;
                else _closeTimer -= Time.deltaTime;
                if (_closeTimer <= 0f) _open = false;
            }

            // Animate: rotate every moving part about the shared hinge.
            float goal = _open ? _swingSign * openAngle : 0f;
            if (!Mathf.Approximately(_curAngle, goal))
            {
                float next = Mathf.MoveTowards(_curAngle, goal, openSpeed * Time.deltaTime);
                float delta = next - _curAngle;
                if (movingParts != null)
                    foreach (var t in movingParts)
                        if (t != null) t.RotateAround(_hinge, Vector3.up, delta);
                _curAngle = next;
            }

            // Block only when fully closed; drop the moving colliders the moment
            // the door starts opening so the player can walk through.
            bool blocking = !_open && Mathf.Approximately(_curAngle, 0f);
            if (_cols != null)
                foreach (var c in _cols)
                    if (c != null && c.enabled != blocking) c.enabled = blocking;
        }

        // Snap the whole door sideways so its opening lines up with the nearest
        // gap in the surrounding walls. Runs once at Start, after the scene's
        // load-time rearrangement (180° flip / room setup) has settled.
        void AlignToOpening()
        {
            Physics.SyncTransforms();
            var col = doorPanel.GetComponent<Collider>();
            Vector3 center = col != null ? col.bounds.center : doorPanel.position;
            Vector3 right = doorPanel.right; right.y = 0f;
            if (right.sqrMagnitude < 1e-4f) return; right.Normalize();

            // Sample clearance along the width axis at the door plane.
            float step = 0.1f, probeR = 0.16f;
            float bestCenter = 0f, bestScore = float.MaxValue; bool found = false;
            float runStart = 0f, prevW = 0f; bool inRun = false;
            for (float w = -maxAlignShift; w <= maxAlignShift + 1e-3f; w += step)
            {
                Vector3 p = center + right * w;
                var hits = Physics.OverlapCapsule(new Vector3(p.x, 0.35f, p.z),
                                                  new Vector3(p.x, 1.4f, p.z), probeR);
                bool blocked = false;
                foreach (var h in hits)
                {
                    if (h.isTrigger || h.transform.IsChildOf(transform)) continue;
                    if (h.bounds.size.y < 0.5f) continue;
                    blocked = true; break;
                }
                if (!blocked) { if (!inRun) { runStart = w; inRun = true; } prevW = w; }
                else if (inRun)
                {
                    float gapC = (runStart + prevW) * 0.5f, gapLen = prevW - runStart;
                    if (gapLen >= minOpeningWidth && Mathf.Abs(gapC) < bestScore) { bestScore = Mathf.Abs(gapC); bestCenter = gapC; found = true; }
                    inRun = false;
                }
            }
            if (inRun)
            {
                float gapC = (runStart + prevW) * 0.5f, gapLen = prevW - runStart;
                if (gapLen >= minOpeningWidth && Mathf.Abs(gapC) < bestScore) { bestCenter = gapC; found = true; }
            }
            if (found && Mathf.Abs(bestCenter) > 0.05f)
                transform.position += right * bestCenter;
        }

        public void Open() { if (!_open) { _open = true; _closeTimer = autoCloseDelay; } }
        public void Close() { _open = false; }
    }
}
