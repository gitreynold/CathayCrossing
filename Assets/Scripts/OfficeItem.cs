using UnityEngine;

namespace OfficeLife
{
    public class OfficeItem : Interactable
    {
        public string itemId;
        public bool destroyOnPickup = true;

        public override void Interact(PlayerController player)
        {
            var inventory = player.GetComponent<Inventory>();
            if (inventory != null)
            {
                inventory.AddItem(itemId);
            }

            if (destroyOnPickup)
            {
                Destroy(gameObject);
            }
        }
    }
}
