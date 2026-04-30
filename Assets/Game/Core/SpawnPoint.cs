using System.Collections.Generic;
using UnityEngine;

namespace CathayCrossing.Core
{
    /// <summary>
    /// Marker placed in a scene to declare a named position for the player.
    /// Used by SceneSwitcher to teleport the player on scene entry.
    /// </summary>
    public class SpawnPoint : MonoBehaviour
    {
        public string pointName;

        static readonly List<SpawnPoint> Active = new();

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        public static SpawnPoint Find(string name)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                var sp = Active[i];
                if (sp != null && sp.pointName == name) return sp;
            }
            return null;
        }
    }
}
