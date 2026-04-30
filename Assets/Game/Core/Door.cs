using System;
using UnityEngine;

namespace CathayCrossing.Core
{
    /// <summary>
    /// Trigger volume that fires when a tagged player enters. Pure data: it
    /// only declares the destination scene + spawn point, then publishes a
    /// static event. The actual scene-switching is handled by a listener
    /// (e.g. CathayCrossing.Bootstrap.SceneSwitcher), so this component
    /// stays free of any cross-module dependency.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Door : MonoBehaviour
    {
        public string targetSceneName;
        public string spawnPointName;
        public string playerTag = "Player";

        public static event Action<Door> AnyPlayerEntered;

        bool _fired;

        void OnEnable()
        {
            _fired = false;
        }

        void OnTriggerEnter(Collider other)
        {
            if (_fired) return;
            if (!other.CompareTag(playerTag)) return;
            _fired = true;
            AnyPlayerEntered?.Invoke(this);
        }
    }
}
