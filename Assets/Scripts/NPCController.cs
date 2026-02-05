using UnityEngine;

namespace OfficeLife
{
    [RequireComponent(typeof(DialogueTrigger))]
    public class NPCController : MonoBehaviour
    {
        public Transform lookTarget;
        public float turnSpeed = 180f;

        private void Update()
        {
            if (lookTarget == null) return;

            Vector3 dir = lookTarget.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;

            Quaternion targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
        }
    }
}
