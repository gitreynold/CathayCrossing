using UnityEngine;

namespace CathayCrossing.Bootstrap
{
    /// <summary>
    /// Spawns the persistent infra GameObject (SceneSwitcher + OfficeDoorSpawner
    /// + OfficePlayerSpawner) once when the game starts, before any scene loads.
    /// This avoids the need for a hand-edited Bootstrap.unity scene file — the
    /// user just plays the office scene normally and the gateway logic boots
    /// itself.
    /// </summary>
    public static class GameInfraBootstrap
    {
        const string InfraName = "__GameInfra";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            if (GameObject.Find(InfraName) != null) return;

            var infra = new GameObject(InfraName);
            Object.DontDestroyOnLoad(infra);
            infra.AddComponent<SceneSwitcher>();
            infra.AddComponent<OfficeDoorSpawner>();
            infra.AddComponent<OfficePlayerSpawner>();
            Debug.Log($"[{InfraName}] Spawned. SceneSwitcher + OfficeDoorSpawner + OfficePlayerSpawner online.");
        }
    }
}
