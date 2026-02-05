using UnityEngine;

namespace OfficeLife
{
    public class CameraFollow : MonoBehaviour
    {
        public Transform target;
        public Vector3 offset = new Vector3(0f, 6f, -6f);
        public float followSpeed = 6f;
        public float rotateSpeed = 120f;

        private void LateUpdate()
        {
            if (target == null) return;

            Vector3 desiredPos = target.position + offset;
            transform.position = Vector3.Lerp(transform.position, desiredPos, followSpeed * Time.deltaTime);

            Quaternion lookRot = Quaternion.LookRotation((target.position - transform.position).normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, lookRot, rotateSpeed * Time.deltaTime);
        }
    }
}
