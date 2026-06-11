using UnityEngine;
using UnityEngine.SceneManagement;

namespace CathayCrossing.Bootstrap
{
    /// <summary>
    /// Spawns the Afa-riding-horse NPC into the office scene at runtime and lets
    /// it gallop around randomly (see <see cref="NpcWanderController"/>).
    /// Mirrors OfficeNpcSpawner (Snoopy); registered after it in
    /// GameInfraBootstrap so room flip + furniture colliders already exist.
    ///
    /// Assets are pulled from Resources, no inspector wiring:
    ///   • NPC/AfaRideHorse/afa_ride_horse        — rigged + animated FBX (gallop loop)
    ///   • NPC/AfaRideHorse/AfaRideHorseAnimator  — controller, default state loops Gallop
    ///   • NPC/AfaRideHorse/afa_ride_horse_tex    — base colour texture
    /// </summary>
    public class OfficeAfaRideHorseSpawner : MonoBehaviour
    {
        public string officeSceneName = "OfficeScene";
        public int npcCount = 1;

        [Tooltip("Spawn near the player's position instead of a random floor tile.")]
        public bool spawnAtPlayer = true;
        public float spawnOffsetFromPlayer = 2.4f;

        const string NpcObjectPrefix = "__OfficeNpc_AfaRideHorse";
        const string FbxResource     = "NPC/AfaRideHorse/afa_ride_horse";
        const string CtrlResource    = "NPC/AfaRideHorse/AfaRideHorseAnimator";
        const string TexResource     = "NPC/AfaRideHorse/afa_ride_horse_tex";

        // Current FBX export (Blender -Y forward) imports with the mesh front
        // on +Z, same convention as Snoopy, so no extra yaw is needed — the
        // horse naturally leads the motion.
        public float visualYawOffset = 0f;

        // Galloping horse moves faster than the walking NPCs.
        public float moveSpeed = 3.2f;

        // Match Snoopy sizing: NPC ends up at half the player's height.
        public float playerHeight = 1.72f;
        public float sizeFractionOfPlayer = 0.5f;
        const float NativeHeight = 1.4f;   // measured mesh bounds height

        Material _npcMaterial;

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            var existing = SceneManager.GetSceneByName(officeSceneName);
            if (existing.IsValid() && existing.isLoaded) SpawnInto(existing);
        }

        void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == officeSceneName) SpawnInto(scene);
        }

        void SpawnInto(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.name.StartsWith(NpcObjectPrefix)) return;

            var fbx = Resources.Load<GameObject>(FbxResource);
            if (fbx == null)
            {
                Debug.LogError($"[OfficeAfaRideHorseSpawner] Missing FBX at Resources/{FbxResource}.");
                return;
            }
            var ctrl = Resources.Load<RuntimeAnimatorController>(CtrlResource);

            if (!ComputeFloorBounds(scene, out Bounds floor))
            {
                Debug.LogWarning("[OfficeAfaRideHorseSpawner] No Floor_* found; spawning at origin.");
                floor = new Bounds(Vector3.zero, new Vector3(8, 0, 8));
            }

            Vector3 anchor = floor.center;
            bool haveAnchor = spawnAtPlayer && TryGetPlayerPosition(scene, out anchor);

            for (int i = 0; i < Mathf.Max(1, npcCount); i++)
            {
                float sx, sz;
                if (haveAnchor)
                {
                    float a = Random.value * Mathf.PI * 2f;
                    sx = anchor.x + Mathf.Cos(a) * spawnOffsetFromPlayer;
                    sz = anchor.z + Mathf.Sin(a) * spawnOffsetFromPlayer;
                    sx = Mathf.Clamp(sx, floor.min.x + 1.5f, floor.max.x - 1.5f);
                    sz = Mathf.Clamp(sz, floor.min.z + 1.5f, floor.max.z - 1.5f);
                }
                else
                {
                    sx = Random.Range(floor.min.x + 1.5f, floor.max.x - 1.5f);
                    sz = Random.Range(floor.min.z + 1.5f, floor.max.z - 1.5f);
                }

                float sy = floor.max.y;
                if (Physics.Raycast(new Vector3(sx, floor.max.y + 5f, sz), Vector3.down,
                                    out RaycastHit hit, 20f) &&
                    hit.collider.name.StartsWith("Floor_Tile"))
                    sy = hit.point.y;
                Vector3 pos = new Vector3(sx, sy, sz);

                var npc = new GameObject(npcCount > 1 ? $"{NpcObjectPrefix}_{i}" : NpcObjectPrefix);
                npc.transform.position = pos;

                float scale = (playerHeight * sizeFractionOfPlayer) / NativeHeight;
                npc.transform.localScale = Vector3.one * scale;

                var cc = npc.AddComponent<CharacterController>();
                cc.height = 1.3f;
                cc.radius = 0.5f;
                cc.center = new Vector3(0f, 0.7f, 0f);
                cc.skinWidth = 0.04f;
                cc.stepOffset = 0.3f;

                var visual = Instantiate(fbx, npc.transform);
                visual.name = "Visual";
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;

                ApplyMaterial(visual);

                var anim = visual.GetComponentInChildren<Animator>();
                if (anim == null) anim = visual.AddComponent<Animator>();
                if (ctrl != null) anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;

                var wander = npc.AddComponent<NpcWanderController>();
                wander.visual = visual.transform;
                wander.visualYawOffset = visualYawOffset;
                wander.moveSpeed = moveSpeed;

                // R-key: circle the player once, park in front; R again resumes.
                var summon = npc.AddComponent<AfaRideHorseSummon>();
                summon.visual = visual.transform;
                summon.visualYawOffset = visualYawOffset;
                summon.runSpeed = moveSpeed * 1.15f;

                SceneManager.MoveGameObjectToScene(npc, scene);
            }

            Debug.Log($"[OfficeAfaRideHorseSpawner] Spawned {Mathf.Max(1, npcCount)} Afa-ride-horse NPC(s) in '{scene.name}'.");
        }

        void ApplyMaterial(GameObject visual)
        {
            if (_npcMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _npcMaterial = new Material(shader);
                var tex = Resources.Load<Texture2D>(TexResource);
                if (tex != null)
                {
                    if (_npcMaterial.HasProperty("_BaseMap")) _npcMaterial.SetTexture("_BaseMap", tex);
                    if (_npcMaterial.HasProperty("_MainTex")) _npcMaterial.SetTexture("_MainTex", tex);
                }
                if (_npcMaterial.HasProperty("_Smoothness")) _npcMaterial.SetFloat("_Smoothness", 0.15f);
                if (_npcMaterial.HasProperty("_Glossiness")) _npcMaterial.SetFloat("_Glossiness", 0.15f);
            }
            foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = _npcMaterial;
                r.sharedMaterials = mats;
            }
        }

        static bool TryGetPlayerPosition(Scene scene, out Vector3 pos)
        {
            pos = Vector3.zero;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "__OfficePlayer" || root.CompareTag("Player"))
                {
                    pos = root.transform.position;
                    return true;
                }
            }
            var tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged != null)
            {
                pos = tagged.transform.position;
                return true;
            }
            return false;
        }

        static bool ComputeFloorBounds(Scene scene, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(false))
                {
                    if (!t.name.StartsWith("Floor_Tile")) continue;
                    foreach (var r in t.GetComponentsInChildren<Renderer>(false))
                    {
                        if (!found) { bounds = r.bounds; found = true; }
                        else bounds.Encapsulate(r.bounds);
                    }
                }
            return found;
        }
    }
}
