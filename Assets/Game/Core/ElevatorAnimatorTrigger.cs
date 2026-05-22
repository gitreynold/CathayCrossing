using System.Collections;
using UnityEngine;

namespace CathayCrossing.Core
{
    /// <summary>
    /// Plays the elevator's built-in GLB door animation when the player
    /// enters the trigger collider. Requires an Animator on this GameObject
    /// (or a parent) that has the "OpenDoor" trigger parameter wired to the
    /// ElevatorDoorAnimator controller.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ElevatorAnimatorTrigger : MonoBehaviour
    {
        [Tooltip("Tag used to identify the player.")]
        public string playerTag = "Player";

        [Tooltip("Minimum seconds between successive open triggers.")]
        public float cooldown = 7f;

        Animator _animator;
        bool _onCooldown;

        void Awake()
        {
            // Look for Animator on self, then walk up the hierarchy
            _animator = GetComponentInParent<Animator>();
            if (_animator == null)
                Debug.LogWarning($"[ElevatorAnimatorTrigger] No Animator found on {gameObject.name} or its parents.", this);

            // Ensure the collider attached to this component is a trigger
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        void OnEnable()
        {
            _onCooldown = false;
        }

        void OnTriggerEnter(Collider other)
        {
            if (_onCooldown) return;
            if (!other.CompareTag(playerTag)) return;
            if (_animator == null) return;

            _animator.SetTrigger("OpenDoor");
            StartCoroutine(CooldownRoutine());
        }

        IEnumerator CooldownRoutine()
        {
            _onCooldown = true;
            yield return new WaitForSeconds(cooldown);
            _onCooldown = false;
        }

        void OnDrawGizmosSelected()
        {
            var col = GetComponent<Collider>();
            if (col is BoxCollider bc)
            {
                Gizmos.color = new Color(0f, 1f, 0.5f, 0.3f);
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawCube(bc.center, bc.size);
                Gizmos.color = new Color(0f, 1f, 0.5f, 0.9f);
                Gizmos.DrawWireCube(bc.center, bc.size);
            }
        }
    }
}
