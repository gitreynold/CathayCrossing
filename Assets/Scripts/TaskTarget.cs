using UnityEngine;

namespace OfficeLife
{
    public class TaskTarget : Interactable
    {
        public string targetId;
        public string fallbackDialogueId;

        public override void Interact(PlayerController player)
        {
            if (TaskManager.Instance != null && TaskManager.Instance.TryComplete(targetId, player.GetComponent<Inventory>()))
            {
                return;
            }

            if (!string.IsNullOrEmpty(fallbackDialogueId) && DialogueSystem.Instance != null)
            {
                DialogueSystem.Instance.Play(fallbackDialogueId);
            }
        }
    }
}
