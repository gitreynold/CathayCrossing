using CathayCrossing.Characters;
using CathayCrossing.HD2D;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CathayCrossing.Bootstrap
{
    /// <summary>
    /// Spawns a controllable Player at runtime when the office scene loads.
    /// Position is the geometric center of every Desk_*/Chair_* renderer
    /// already placed in the scene, projected onto the floor (y = 0).
    ///
    /// Visual loading order (first hit wins):
    ///   1. <c>Resources/Characters/men_3D_color</c> — Tencent Hunyuan3D
    ///      rigged + textured chibi. When found, an <see cref="Animator"/>
    ///      is attached using <c>Resources/Characters/PlayerAnimator</c>
    ///      (built by <c>Tools › CathayCrossing › Setup Character Animator</c>).
    ///   2. <c>Resources/Characters/tripo_man_1</c> — older static Tripo
    ///      mesh (kept as fallback so existing scenes don't break).
    ///   3. <see cref="ProceduralCharacter"/> primitives.
    ///
    /// Materials cloned from an existing scene material so URP/Lit survives
    /// WebGL shader stripping (only relevant for the procedural path).
    /// </summary>
    public class OfficePlayerSpawner : MonoBehaviour
    {
        [Tooltip("Office scene this spawner injects into.")]
        public string officeSceneName = "OfficeScene";

        [Tooltip("Vertical offset added on top of the computed desk-chair center.")]
        public float spawnY = 0f;

        const string PlayerObjectName = "__OfficePlayer";

        // Ordered list of character model paths to try, newest first. Stop at
        // the first one Resources.Load actually returns.
        static readonly string[] CharacterModelResourcePaths =
        {
            "Characters/men_3D_color",
            "Characters/tripo_man_1",
        };
        const string AnimatorControllerResourcePath = "Characters/PlayerAnimator";

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;

            var existing = SceneManager.GetSceneByName(officeSceneName);
            if (existing.IsValid() && existing.isLoaded) SpawnInto(existing);
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != officeSceneName) return;
            SpawnInto(scene);
        }

        void SpawnInto(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == PlayerObjectName) return;
                if (root.CompareTag("Player")) return;
            }

            Vector3 center = ComputeDeskChairCenter(scene, out bool found);
            if (!found)
            {
                Debug.LogWarning($"[OfficePlayerSpawner] No Desk_*/Chair_* objects found in '{scene.name}'. Spawning at origin.");
            }

            // Flip the whole map 180° around the desk-chair center so the back
            // wall (windows) ends up at the top of the camera view. The pivot
            // is the spawn point itself, so it stays in place across the flip.
            var pivot = new Vector3(center.x, 0f, center.z);
            Rotate180Around(scene, pivot);

            var spawnPos = new Vector3(center.x, spawnY, center.z);

            var player = new GameObject(PlayerObjectName);
            player.tag = "Player";
            player.transform.position = spawnPos;

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.72f;
            cc.radius = 0.30f;
            cc.center = new Vector3(0f, 0.86f, 0f);
            cc.skinWidth = 0.04f;
            cc.minMoveDistance = 0f;
            cc.stepOffset = 0.2f;

            // SpriteRoot rotates to face movement; Body bobs vertically while walking.
            var spriteRoot = new GameObject("SpriteRoot");
            spriteRoot.transform.SetParent(player.transform, false);

            var body = new GameObject("Body");
            body.transform.SetParent(spriteRoot.transform, false);

            GameObject fbxVisual = TryInstantiateCharacterModel(body.transform, out string loadedPath);
            Animator visualAnimator = null;
            if (fbxVisual == null)
            {
                Material baseMat = FindLoadedMaterial(scene);
                var procedural = body.AddComponent<ProceduralCharacter>();
                procedural.Build(baseMat);
            }
            else
            {
                visualAnimator = AttachAnimator(fbxVisual);
            }

            var ctrl = player.AddComponent<OctopathPlayerController>();
            ctrl.spriteRoot = spriteRoot.transform;
            // Bob is a fake walk-cue for the primitive procedural body; the
            // real skinned mesh is driven by the Animator below instead.
            ctrl.spriteVisual = fbxVisual != null ? null : body.transform;
            ctrl.animator     = visualAnimator;

            SceneManager.MoveGameObjectToScene(player, scene);

            OfficeRoomSetup.Apply(scene);

            ConfigureCamera(scene, player.transform);

            string visualKind = fbxVisual != null ? loadedPath : "ProceduralCharacter";
            string animKind   = visualAnimator != null && visualAnimator.runtimeAnimatorController != null
                ? $" + Animator '{visualAnimator.runtimeAnimatorController.name}'"
                : "";
            Debug.Log($"[OfficePlayerSpawner] Player ({visualKind}{animKind}) spawned at {spawnPos} in '{scene.name}'. WASD/arrows to move, Shift to run, H to wave.");
        }

        // ─── Helpers ────────────────────────────────────────────────────────
        static GameObject TryInstantiateCharacterModel(Transform parent, out string loadedPath)
        {
            foreach (var path in CharacterModelResourcePaths)
            {
                var prefab = Resources.Load<GameObject>(path);
                if (prefab == null) continue;

                var visual = Instantiate(prefab, parent);
                visual.name = "Visual";
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                loadedPath = path;
                return visual;
            }
            Debug.LogWarning($"[OfficePlayerSpawner] No character model found in Resources at any of: {string.Join(", ", CharacterModelResourcePaths)}. Falling back to ProceduralCharacter.");
            loadedPath = null;
            return null;
        }

        // Wires the player Animator. The Tencent FBX already ships with an
        // Animator component (Humanoid avatar baked at import time); we only
        // assign the runtime controller and disable root motion so movement
        // stays driven by CharacterController, not by the Walking clip.
        static Animator AttachAnimator(GameObject visual)
        {
            var anim = visual.GetComponentInChildren<Animator>();
            if (anim == null)
            {
                anim = visual.AddComponent<Animator>();
            }
            var controller = Resources.Load<RuntimeAnimatorController>(AnimatorControllerResourcePath);
            if (controller != null)
            {
                anim.runtimeAnimatorController = controller;
            }
            else
            {
                Debug.LogWarning($"[OfficePlayerSpawner] AnimatorController not found at Resources/{AnimatorControllerResourcePath}. " +
                                 "Run Tools › CathayCrossing › Setup Character Animator to generate it.");
            }
            anim.applyRootMotion = false;
            return anim;
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

        static Vector3 ComputeDeskChairCenter(Scene scene, out bool found)
        {
            Bounds combined = default;
            found = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive: false))
                {
                    if (!IsDeskOrChair(t.name)) continue;
                    foreach (var r in t.GetComponentsInChildren<Renderer>(includeInactive: false))
                    {
                        if (!found) { combined = r.bounds; found = true; }
                        else combined.Encapsulate(r.bounds);
                    }
                }
            }
            return found ? combined.center : Vector3.zero;
        }

        static bool IsDeskOrChair(string n)
        {
            return n.StartsWith("Desk_") || n.StartsWith("Chair_");
        }

        // Rotate every root in the scene 180° around the world Y axis through
        // `pivot`. The scene is reloaded fresh from disk on every load, so we
        // don't need an idempotency guard — the player-existence check at the
        // top of SpawnInto already prevents a second pass within one load.
        static void Rotate180Around(Scene scene, Vector3 pivot)
        {
            var rot = Quaternion.AngleAxis(180f, Vector3.up);
            foreach (var root in scene.GetRootGameObjects())
            {
                var t = root.transform;
                Vector3 offset = t.position - pivot;
                t.position = pivot + new Vector3(-offset.x, offset.y, -offset.z);
                t.rotation = rot * t.rotation;
            }
        }

        static void ConfigureCamera(Scene scene, Transform follow)
        {
            Camera target = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var c in root.GetComponentsInChildren<Camera>(includeInactive: false))
                {
                    if (c.gameObject.CompareTag("MainCamera")) { target = c; break; }
                }
                if (target != null) break;
            }
            if (target == null) target = Camera.main;
            if (target == null) return;

            var oc = target.GetComponent<OctopathCamera>();
            bool fresh = oc == null;
            if (fresh) oc = target.gameObject.AddComponent<OctopathCamera>();
            oc.target = follow;
            if (fresh)
            {
                oc.targetOffset = new Vector3(0f, 1.0f, 0f);
                oc.pitch = 28f;
                oc.yaw = 0f;
                oc.distance = 9f;
                oc.fov = 30f;
                oc.allowMouseOrbit = false;
                oc.allowScrollZoom = true;
                oc.SyncTargetsFromFields();
            }
        }
    }
}
