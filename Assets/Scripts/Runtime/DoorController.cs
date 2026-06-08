using UnityEngine;
using UnityEngine.InputSystem;

namespace CathayCrossing.HD2D
{
    /// <summary>
    /// Double swing-door controller. When the player is within range and
    /// presses O, both leaves swing open around their hinge (outer jamb),
    /// opening AWAY from the player (push direction). Pressing O again closes.
    /// Each leaf's transform origin is authored at its hinge edge, so the
    /// leaves rotate about the world-up axis through their own pivot.
    /// </summary>
    [DisallowMultipleComponent]
    public class DoorController : MonoBehaviour
    {
        [Header("Leaves (auto-found if empty)")]
        public Transform leafA;
        public Transform leafB;

        [Header("Open behaviour")]
        [Tooltip("Key that toggles the door when the player is near.")]
        public Key openKey = Key.O;
        [Tooltip("Swing angle when fully open (degrees).")]
        public float openAngle = 95f;
        [Tooltip("Swing speed (degrees per second).")]
        public float openSpeed = 220f;
        [Tooltip("Player must be within this distance (m) of the door centre.")]
        public float range = 3.5f;
        [Tooltip("Tag used to find the player.")]
        public string playerTag = "Player";
        [Tooltip("Seconds the door stays fully open before auto-closing.")]
        public float autoCloseDelay = 2f;

        // Per-leaf hinge data resolved at Start.
        Transform[] _leaves;
        Vector3[] _pivot;       // world hinge point (constant — rotation keeps it fixed)
        float[] _leafSign;      // +1 / -1 so the two leaves part symmetrically
        float[] _curAngle;      // current applied swing angle
        float _target;          // signed target magnitude shared (sign folded into leafSign*swingSign)
        bool _open;
        float _swingSign = 1f;  // +1 opens toward -Z, -1 toward +Z (set from player side)
        float _closeTimer;      // counts down once fully open, then auto-closes
        float _centerX;
        Collider[] _cols;       // leaf colliders, dropped while the door is open

void Start()
        {
            if (leafA == null || leafB == null) AutoFindLeaves();
            _leaves = new[] { leafA, leafB };
            _pivot = new Vector3[2];
            _leafSign = new float[2];
            _curAngle = new float[2];

            _centerX = transform.position.x;
            for (int i = 0; i < 2; i++)
            {
                _pivot[i] = _leaves[i].position;
                _leafSign[i] = (_pivot[i].x < _centerX) ? +1f : -1f;
                _curAngle[i] = 0f;
            }

            // Cache leaf colliders so we can drop them while the door is open
            // (a swept static collider doesn't reliably clear the player's
            // CharacterController, which left the player stuck in the doorway).
            var cols = new System.Collections.Generic.List<Collider>();
            foreach (var leaf in _leaves)
                if (leaf != null) cols.AddRange(leaf.GetComponentsInChildren<Collider>());
            _cols = cols.ToArray();
        }

        void AutoFindLeaves()
        {
            int n = 0;
            foreach (Transform c in transform)
            {
                if (c.GetComponentInChildren<Renderer>() == null) continue;
                if (n == 0) leafA = c; else if (n == 1) leafB = c;
                n++;
            }
        }

void Update()
        {
            var kb = Keyboard.current;
            // O only OPENS, and only while the door is currently closed.
            if (!_open && kb != null && kb[openKey].wasPressedThisFrame)
            {
                var player = GameObject.FindWithTag(playerTag);
                if (player != null)
                {
                    Vector3 d = player.transform.position - transform.position;
                    d.y = 0f;
                    if (d.sqrMagnitude <= range * range)
                    {
                        _swingSign = (player.transform.position.z > transform.position.z) ? +1f : -1f;
                        _open = true;
                        _closeTimer = autoCloseDelay;
                    }
                }
            }

            // Stay open while the player is nearby; only auto-close after
            // they leave, so the door never closes on someone mid-passage.
            if (_open)
            {
                var pl = GameObject.FindWithTag(playerTag);
                bool near = false;
                if (pl != null)
                {
                    Vector3 dd = pl.transform.position - transform.position; dd.y = 0f;
                    near = dd.sqrMagnitude <= (range + 1.5f) * (range + 1.5f);
                }
                if (near) _closeTimer = autoCloseDelay;
                else _closeTimer -= Time.deltaTime;
                if (_closeTimer <= 0f) _open = false;
            }

            // Animate each leaf toward its target angle by rotating about the
            // world-up axis through its hinge.
            for (int i = 0; i < _leaves.Length; i++)
            {
                float goal = _open ? _leafSign[i] * _swingSign * openAngle : 0f;
                if (Mathf.Approximately(_curAngle[i], goal)) continue;
                float next = Mathf.MoveTowards(_curAngle[i], goal, openSpeed * Time.deltaTime);
                float delta = next - _curAngle[i];
                _leaves[i].RotateAround(_pivot[i], Vector3.up, delta);
                _curAngle[i] = next;
            }

            // Block only when fully closed; drop the leaf colliders while the
            // door is open so the player can walk through.
            bool blocking = !_open;
            if (blocking)
                for (int i = 0; i < _curAngle.Length; i++)
                    if (!Mathf.Approximately(_curAngle[i], 0f)) { blocking = false; break; }
            if (_cols != null)
                foreach (var c in _cols)
                    if (c != null && c.enabled != blocking) c.enabled = blocking;
        }

        // Public hooks (UI / network).
        public void Open(bool away_minusZ)
        {
            _swingSign = away_minusZ ? +1f : -1f;
            _open = true;
        }
        public void Close() { _open = false; }
        public void Toggle() { _open = !_open; }
    }
}
