using System.Collections.Generic;
using UnityEngine;

namespace OfficeLife
{
    public class Inventory : MonoBehaviour
    {
        private HashSet<string> items = new HashSet<string>();

        public bool HasItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return false;
            return items.Contains(itemId);
        }

        public void AddItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return;
            if (items.Add(itemId))
            {
                Debug.Log("Inventory: added " + itemId);
            }
        }
    }
}
