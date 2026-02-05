using UnityEngine;

namespace OfficeLife
{
    public abstract class Interactable : MonoBehaviour
    {
        [TextArea]
        public string prompt = "Interact";

        public abstract void Interact(PlayerController player);
    }
}
