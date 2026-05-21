using System.Collections;
using CathayCrossing.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CathayCrossing.Bootstrap
{
    /// <summary>
    /// Owns map-scene transitions. Listens for any Door firing, single-mode
    /// loads the target scene, persists the player + main camera across the
    /// load via DontDestroyOnLoad, then teleports the player to the matching
    /// SpawnPoint in the new scene.
    /// </summary>
    public class SceneSwitcher : MonoBehaviour
    {
        public static SceneSwitcher Instance { get; private set; }

        GameObject _persistentPlayer;
        GameObject _persistentCamera;
        bool _switching;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Door.AnyPlayerEntered += HandleDoor;
        }

        void OnDestroy()
        {
            Door.AnyPlayerEntered -= HandleDoor;
            if (Instance == this) Instance = null;
        }

        void HandleDoor(Door door)
        {
            if (_switching || door == null) return;
            if (string.IsNullOrEmpty(door.targetSceneName)) return;
            StartCoroutine(SwitchRoutine(door.targetSceneName, door.spawnPointName));
        }

        IEnumerator SwitchRoutine(string targetScene, string spawnPointName)
        {
            _switching = true;
            Debug.Log($"[SceneSwitcher] Switching to '{targetScene}' (spawn={spawnPointName}).");

            // Adopt the current scene's player + main camera so they survive
            // the upcoming Single-mode scene load.
            if (_persistentPlayer == null) _persistentPlayer = GameObject.FindGameObjectWithTag("Player");
            if (_persistentCamera == null && Camera.main != null) _persistentCamera = Camera.main.gameObject;

            Debug.Log($"[SceneSwitcher] Adopted player={(_persistentPlayer != null ? _persistentPlayer.name : "<none>")}, camera={(_persistentCamera != null ? _persistentCamera.name : "<none>")}.");

            // DontDestroyOnLoad only works on root GameObjects. Player + Camera
            // are children of OctopathOffice_Root, so we detach them first.
            // SetParent(null) keeps world-space transform by default.
            if (_persistentPlayer != null)
            {
                if (_persistentPlayer.transform.parent != null) _persistentPlayer.transform.SetParent(null);
                DontDestroyOnLoad(_persistentPlayer);
            }
            if (_persistentCamera != null)
            {
                if (_persistentCamera.transform.parent != null) _persistentCamera.transform.SetParent(null);
                DontDestroyOnLoad(_persistentCamera);
            }

            var load = SceneManager.LoadSceneAsync(targetScene, LoadSceneMode.Single);
            if (load == null)
            {
                Debug.LogError($"[SceneSwitcher] LoadSceneAsync('{targetScene}') returned null. Add the scene to Build Settings.", this);
                _switching = false;
                yield break;
            }
            while (!load.isDone) yield return null;
            Debug.Log($"[SceneSwitcher] Scene '{targetScene}' loaded.");

            // Wait one frame so SpawnPoints in the new scene have run OnEnable.
            yield return null;

            var newScene = SceneManager.GetSceneByName(targetScene);
            int destroyed = DestroyDuplicatesIn(newScene);
            if (destroyed > 0) Debug.Log($"[SceneSwitcher] Destroyed {destroyed} duplicate Player/Camera object(s) in '{targetScene}'.");

            // Move our persistents into the new scene so the Hierarchy stays tidy.
            if (newScene.IsValid() && newScene.isLoaded)
            {
                if (_persistentPlayer != null && _persistentPlayer.scene != newScene)
                {
                    SceneManager.MoveGameObjectToScene(_persistentPlayer, newScene);
                }
                if (_persistentCamera != null && _persistentCamera.scene != newScene)
                {
                    SceneManager.MoveGameObjectToScene(_persistentCamera, newScene);
                }
            }

            if (!string.IsNullOrEmpty(spawnPointName) && _persistentPlayer != null)
            {
                var spawn = SpawnPoint.Find(spawnPointName);
                if (spawn != null)
                {
                    var cc = _persistentPlayer.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;
                    _persistentPlayer.transform.SetPositionAndRotation(spawn.transform.position, spawn.transform.rotation);
                    if (cc != null) cc.enabled = true;
                    Debug.Log($"[SceneSwitcher] Teleported player to '{spawnPointName}' at {spawn.transform.position}.");
                }
                else
                {
                    Debug.LogWarning($"[SceneSwitcher] No SpawnPoint named '{spawnPointName}' found in '{targetScene}'. Player stays at {_persistentPlayer.transform.position}.");
                }
            }

            Debug.Log($"[SceneSwitcher] Switch to '{targetScene}' complete.");
            _switching = false;
        }

        int DestroyDuplicatesIn(Scene scene)
        {
            int destroyed = 0;
            // The newly-loaded scene may have its own baked-in Player + Camera
            // (e.g. SampleScene). We keep our persistent ones and delete the
            // duplicates so the world has a single player and a single main camera.
            foreach (var go in GameObject.FindGameObjectsWithTag("Player"))
            {
                if (go != _persistentPlayer && go.scene == scene)
                {
                    Destroy(go);
                    destroyed++;
                }
            }

            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (cam == null) continue;
                if (cam.gameObject == _persistentCamera) continue;
                if (!cam.CompareTag("MainCamera")) continue;
                if (cam.gameObject.scene != scene) continue;
                Destroy(cam.gameObject);
                destroyed++;
            }
            return destroyed;
        }
    }
}
