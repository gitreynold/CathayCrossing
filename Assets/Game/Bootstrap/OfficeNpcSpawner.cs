using UnityEngine;
using UnityEngine.SceneManagement;

namespace CathayCrossing.Bootstrap
{
    /// <summary>
    /// Spawns the Snoopy NPC(s) into the office scene at runtime and lets them
    /// wander randomly (see <see cref="NpcWanderController"/>). Mirrors
    /// OfficePlayerSpawner's lifecycle but runs *after* it (registered last in
    /// GameInfraBootstrap) so the 180° room flip and furniture colliders are
    /// already in place.
    ///
    /// Assets are pulled from Resources so this works with no inspector wiring:
    ///   • NPC/Snoopy/Snoopy            — rigged + animated FBX
    ///   • NPC/Snoopy/SnoopyAnimator    — controller, default state loops Gallop
    ///   • NPC/Snoopy/Snoopy_tex        — base colour texture
    /// </summary>
    public class OfficeNpcSpawner : MonoBehaviour
    {
        public string officeSceneName = "OfficeScene";
        public int npcCount = 1;

        const string NpcObjectPrefix = "__OfficeNpc_Snoopy";
        const string FbxResource     = "NPC/Snoopy/Snoopy";
        const string CtrlResource    = "NPC/Snoopy/SnoopyAnimator";
        const string TexResource     = "NPC/Snoopy/Snoopy_tex";

        // Snoopy's mesh "front" after FBX axis conversion vs Unity +Z. Tuned so
        // the riding horse LEADS the direction of travel (walks forward).
        public float visualYawOffset = 0f;

        // Final NPC height as a fraction of the player's height. The player
        // CharacterController is 1.72 m tall, so 0.5 → Snoopy ≈ 0.86 m. Snoopy's
        // native mesh is ~1.9 m, so we scale the whole NPC down to suit.
        public float playerHeight = 1.72f;
        public float sizeFractionOfPlayer = 0.5f;
        const float SnoopyNativeHeight = 1.9f;

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
            // Skip if already spawned in this scene load.
            foreach (var root in scene.GetRootGameObjects())
                if (root.name.StartsWith(NpcObjectPrefix)) return;

            var fbx = Resources.Load<GameObject>(FbxResource);
            if (fbx == null)
            {
                Debug.LogError($"[OfficeNpcSpawner] Missing FBX at Resources/{FbxResource}.");
                return;
            }
            var ctrl = Resources.Load<RuntimeAnimatorController>(CtrlResource);

            if (!ComputeFloorBounds(scene, out Bounds floor))
            {
                Debug.LogWarning("[OfficeNpcSpawner] No Floor_* found; spawning at origin.");
                floor = new Bounds(Vector3.zero, new Vector3(8, 0, 8));
            }

            for (int i = 0; i < Mathf.Max(1, npcCount); i++)
            {
                float sx = Random.Range(floor.min.x + 1.5f, floor.max.x - 1.5f);
                float sz = Random.Range(floor.min.z + 1.5f, floor.max.z - 1.5f);
                float sy = floor.max.y;
                if (Physics.Raycast(new Vector3(sx, floor.max.y + 5f, sz), Vector3.down,
                                    out RaycastHit hit, 20f) &&
                    hit.collider.name.StartsWith("Floor_Tile"))
                    sy = hit.point.y;
                Vector3 pos = new Vector3(sx, sy, sz);

                var npc = new GameObject(npcCount > 1 ? $"{NpcObjectPrefix}_{i}" : NpcObjectPrefix);
                npc.transform.position = pos;

                // Half the player's height. Scaling the root scales the visual,
                // the CharacterController, and the collider together.
                float scale = (playerHeight * sizeFractionOfPlayer) / SnoopyNativeHeight;
                npc.transform.localScale = Vector3.one * scale;

                var cc = npc.AddComponent<CharacterController>();
                cc.height = 1.6f;
                cc.radius = 0.45f;
                cc.center = new Vector3(0f, 0.85f, 0f);
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

                SceneManager.MoveGameObjectToScene(npc, scene);
            }

            Debug.Log($"[OfficeNpcSpawner] Spawned {Mathf.Max(1, npcCount)} Snoopy NPC(s) in '{scene.name}'.");
        }

        // Build a URP/Lit material with the baked texture so the plush survives
        // the URP shader pipeline (FBX-imported Standard materials go magenta).
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
