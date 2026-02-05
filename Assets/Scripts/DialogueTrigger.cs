using UnityEngine;

namespace OfficeLife
{
    public class DialogueTrigger : Interactable
    {
        public string dialogueId;

        public override void Interact(PlayerController player)
        {
            if (string.IsNullOrEmpty(dialogueId))
            {
                Debug.Log("DialogueTrigger: no dialogue id assigned.");
                return;
            }

            if (DialogueSystem.Instance != null)
            {
                DialogueSystem.Instance.Play(dialogueId);
            }
        }
    }
}
