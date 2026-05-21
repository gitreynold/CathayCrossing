using CathayCrossing.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CathayCrossing.Bootstrap
{
    /// <summary>
    /// Injects a doorway + return-spawn into the office scene every time it
    /// is loaded, without modifying the office scene file. The office team
    /// can later replace this with a real door GameObject placed in their
    /// scene; until then, this keeps the office content fully owned by them
    /// and the gateway logic owned by Bootstrap.
    /// </summary>
    public class OfficeDoorSpawner : MonoBehaviour
    {
        [Tooltip("Office scene this spawner injects into.")]
        public string officeSceneName = "SampleScene";

        [Tooltip("World position of the door inside the office (top-right corner).")]
        public Vector3 doorPosition = new Vector3(27f, 1.5f, 18f);

        [Tooltip("World position where the player appears when returning from the game room.")]
        public Vector3 returnSpawnPosition = new Vector3(24f, 0f, 18f);

        [Tooltip("Scene the door takes the player to.")]
        public string targetSceneName = "GameRoom";

        [Tooltip("Spawn point name in the target scene where the player appears.")]
        public string targetSpawnPointName = "OfficeEntry";

        [Tooltip("Spawn point name placed in the office for return trips.")]
        public string returnSpawnPointName = "OfficeReturn";

        const string DoorObjectName = "__GameRoomDoor";
        const string SpawnObjectName = "__OfficeReturnSpawn";

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            Debug.Log($"[OfficeDoorSpawner] Listening for scene '{officeSceneName}' to inject door at {doorPosition}.");

            // Office may already be loaded by the time we wake up (Bootstrap
            // additively loads it before this OnEnable fires).
            var existing = SceneManager.GetSceneByName(officeSceneName);
            if (existing.IsValid() && existing.isLoaded) InjectInto(existing);
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != officeSceneName) return;
            InjectInto(scene);
        }

        void InjectInto(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == DoorObjectName)
                {
                    Debug.Log($"[OfficeDoorSpawner] Door already present in '{scene.name}', skipping.");
                    return;
                }
            }

            Debug.Log($"[OfficeDoorSpawner] Injecting door into '{scene.name}' at {doorPosition}. Look for a tall orange door at the top-right of the office.");

            // Borrow a working material from the loaded scene so the runtime
            // cubes inherit the project's URP/Lit shader. This works in WebGL
            // builds where Shader.Find on its own can fail to locate URP shaders.
            var baseMaterial = FindLoadedMaterial(scene);

            // A single tall door-shaped cube. Center y=1.5 + scale y=3 puts it
            // flush with the floor and 3m tall.
            var door = GameObject.CreatePrimitive(PrimitiveType.Cube);
            door.name = DoorObjectName;
            door.transform.position = doorPosition;
            door.transform.localScale = new Vector3(1.5f, 3f, 0.4f);
            ApplyColor(door.GetComponent<MeshRenderer>(), baseMaterial, new Color(0.85f, 0.45f, 0.15f));

            // Re-use the primitive's existing BoxCollider as a trigger — DON'T
            // Destroy + AddComponent (Destroy is deferred to end-of-frame, which
            // would leave a solid collider blocking the player for that frame).
            // Trigger size is in local space; with scale (1.5, 3, 0.4) the
            // world trigger ends up roughly 3.0 x 3.6 x 2.4 m.
            var col = door.GetComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(2f, 1.2f, 6f);
            col.center = Vector3.zero;

            var doorComp = door.AddComponent<Door>();
            doorComp.targetSceneName = targetSceneName;
            doorComp.spawnPointName = targetSpawnPointName;

            SceneManager.MoveGameObjectToScene(door, scene);

            var spawn = new GameObject(SpawnObjectName);
            spawn.transform.position = returnSpawnPosition;
            var spawnComp = spawn.AddComponent<SpawnPoint>();
            spawnComp.pointName = returnSpawnPointName;

            SceneManager.MoveGameObjectToScene(spawn, scene);
        }

        static Material FindLoadedMaterial(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var renderers = root.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
                foreach (var r in renderers)
                {
                    if (r != null && r.sharedMaterial != null && r.sharedMaterial.shader != null)
                    {
                        return r.sharedMaterial;
                    }
                }
            }
            return null;
        }

        static void ApplyColor(MeshRenderer renderer, Material baseMaterial, Color color)
        {
            if (renderer == null) return;

            Material mat;
            if (baseMaterial != null)
            {
                mat = new Material(baseMaterial);
            }
            else
            {
                var shader =
                    Shader.Find("Universal Render Pipeline/Lit") ??
                    Shader.Find("Universal Render Pipeline/Unlit") ??
                    Shader.Find("Standard");
                mat = shader != null ? new Material(shader) : new Material(renderer.sharedMaterial);
            }

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            mat.color = color;

            // Strip any albedo texture inherited from the borrowed material so the
            // tint actually shows.
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", null);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", null);

            renderer.sharedMaterial = mat;
        }
    }
}
