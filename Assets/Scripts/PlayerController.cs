using UnityEngine;

namespace OfficeLife
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public float moveSpeed = 4.5f;
        public float rotateSpeed = 540f;
        public float gravity = -9.81f;

        private CharacterController controller;
        private Vector3 velocity;
        private Camera mainCamera;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            mainCamera = Camera.main;
        }

        private void Update()
        {
            HandleMovement();
        }

        private void HandleMovement()
        {
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");

            Vector3 input = new Vector3(h, 0f, v);
            if (input.sqrMagnitude > 0.001f)
            {
                Vector3 camForward = mainCamera != null ? mainCamera.transform.forward : Vector3.forward;
                Vector3 camRight = mainCamera != null ? mainCamera.transform.right : Vector3.right;
                camForward.y = 0f;
                camRight.y = 0f;
                camForward.Normalize();
                camRight.Normalize();

                Vector3 moveDir = (camForward * input.z + camRight * input.x).normalized;
                controller.Move(moveDir * (moveSpeed * Time.deltaTime));

                Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, rotateSpeed * Time.deltaTime);
            }

            if (controller.isGrounded && velocity.y < 0f)
            {
                velocity.y = -2f;
            }

            velocity.y += gravity * Time.deltaTime;
            controller.Move(velocity * Time.deltaTime);
        }
    }
}
