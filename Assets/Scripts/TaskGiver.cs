using UnityEngine;

namespace OfficeLife
{
    public class TaskGiver : Interactable
    {
        public string npcId;
        public string fallbackDialogueId;

        public override void Interact(PlayerController player)
        {
            if (TaskManager.Instance != null)
            {
                if (TaskManager.Instance.TryStartWith(npcId)) return;
                if (TaskManager.Instance.TryComplete(npcId, player.GetComponent<Inventory>())) return;
            }

            if (!string.IsNullOrEmpty(fallbackDialogueId) && DialogueSystem.Instance != null)
            {
                DialogueSystem.Instance.Play(fallbackDialogueId);
            }
        }
    }
}
