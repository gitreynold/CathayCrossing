using UnityEngine;

namespace OfficeLife
{
    [RequireComponent(typeof(PlayerController))]
    public class InteractionController : MonoBehaviour
    {
        public float interactDistance = 2.0f;
        public KeyCode interactKey = KeyCode.E;
        public LayerMask mask = ~0;

        private Camera mainCamera;
        private Interactable current;
        private InteractionPromptUI prompt;
        private PlayerController player;

        private void Awake()
        {
            player = GetComponent<PlayerController>();
            mainCamera = Camera.main;
            prompt = InteractionPromptUI.Instance;
        }

        private void Update()
        {
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
                if (mainCamera == null) return;
            }

            Scan();

            if (current != null && Input.GetKeyDown(interactKey))
            {
                current.Interact(player);
            }
        }

        private void Scan()
        {
            Ray ray = new Ray(mainCamera.transform.position, mainCamera.transform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, mask))
            {
                var interactable = hit.collider.GetComponentInParent<Interactable>();
                if (interactable != current)
                {
                    current = interactable;
                    if (current != null)
                    {
                        if (prompt != null) prompt.Show(current.prompt);
                    }
                    else
                    {
                        if (prompt != null) prompt.Hide();
                    }
                }
            }
            else if (current != null)
            {
                current = null;
                if (prompt != null) prompt.Hide();
            }
        }
    }
}
