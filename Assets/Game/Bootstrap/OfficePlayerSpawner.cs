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
    /// Character pick order (first hit wins):
    ///   1. Player's last selection — <c>PlayerPrefs["ActiveCharacterId"]</c>
    ///      set by the CharacterSelect UI scene.
    ///   2. The first <see cref="CharacterDefinition"/> we find in
    ///      <c>Resources/Characters/</c> (alphabetical by asset name).
    ///   3. <see cref="ProceduralCharacter"/> primitives (when no
    ///      CharacterDefinitions exist at all).
    ///
    /// Each <see cref="CharacterDefinition"/> bundles a rigged FBX + an
    /// AnimatorController, so the spawner doesn't care about per-character
    /// asset paths — it just instantiates definition.body and assigns
    /// definition.controller. New characters slot in by dropping another
    /// .asset under Resources/Characters/&lt;Name&gt;/.
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

        // Where the CharacterSelect scene stashes the player's pick. Read at
        // SpawnInto time so a fresh selection takes effect on the very next
        // scene load (without a restart).
        public const string ActiveCharacterPrefsKey = "ActiveCharacterId";

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

            CharacterDefinition activeDef = ResolveActiveCharacter();
            GameObject fbxVisual = activeDef != null ? InstantiateCharacterDefinition(activeDef, body.transform) : null;
            Animator visualAnimator = null;
            if (fbxVisual == null)
            {
                Material baseMat = FindLoadedMaterial(scene);
                var procedural = body.AddComponent<ProceduralCharacter>();
                procedural.Build(baseMat);
            }
            else
            {
                visualAnimator = AttachAnimator(fbxVisual, activeDef);
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

            string visualKind = fbxVisual != null && activeDef != null
                ? $"'{activeDef.displayName}' ({activeDef.body.name})"
                : "ProceduralCharacter";
            string animKind   = visualAnimator != null && visualAnimator.runtimeAnimatorController != null
                ? $" + Animator '{visualAnimator.runtimeAnimatorController.name}'"
                : "";
            Debug.Log($"[OfficePlayerSpawner] Player {visualKind}{animKind} spawned at {spawnPos} in '{scene.name}'. WASD/arrows to move, Shift to run, H to wave, F to dance.");
        }

        // ─── Helpers ────────────────────────────────────────────────────────

        // Resolve the active character via PlayerPrefs first, falling back to
        // the first definition we find in Resources/Characters/ so the game
        // still boots in the very first run before the player has picked.
        static CharacterDefinition ResolveActiveCharacter()
        {
            var all = Resources.LoadAll<CharacterDefinition>("Characters");
            if (all == null || all.Length == 0)
            {
                Debug.LogWarning("[OfficePlayerSpawner] No CharacterDefinition assets found under Resources/Characters/. " +
                                 "Falling back to ProceduralCharacter.");
                return null;
            }

            string wanted = PlayerPrefs.GetString(ActiveCharacterPrefsKey, "");
            if (!string.IsNullOrEmpty(wanted))
            {
                foreach (var def in all)
                {
                    if (def != null && def.id == wanted) return def;
                }
                Debug.LogWarning($"[OfficePlayerSpawner] PlayerPrefs['{ActiveCharacterPrefsKey}']='{wanted}' " +
                                 "but no matching CharacterDefinition. Using first available.");
            }

            // Deterministic fallback so multiple runs land on the same default.
            System.Array.Sort(all, (a, b) => string.CompareOrdinal(a.name, b.name));
            return all[0];
        }

        static GameObject InstantiateCharacterDefinition(CharacterDefinition def, Transform parent)
        {
            if (def.body == null)
            {
                Debug.LogWarning($"[OfficePlayerSpawner] CharacterDefinition '{def.id}' has no body. Falling back to ProceduralCharacter.");
                return null;
            }
            var visual = Instantiate(def.body, parent);
            visual.name = "Visual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            return visual;
        }

        // Wires the player Animator. The rigged FBX already ships with an
        // Animator component (Humanoid avatar baked at import time); we only
        // assign the runtime controller and disable root motion so movement
        // stays driven by CharacterController, not by the Walking clip.
        static Animator AttachAnimator(GameObject visual, CharacterDefinition def)
        {
            var anim = visual.GetComponentInChildren<Animator>();
            if (anim == null) anim = visual.AddComponent<Animator>();
            if (def != null && def.controller != null)
            {
                anim.runtimeAnimatorController = def.controller;
            }
            else
            {
                Debug.LogWarning($"[OfficePlayerSpawner] CharacterDefinition '{def?.id ?? "(null)"}' has no AnimatorController. " +
                                 "Run Tools › CathayCrossing › Setup <Name> Character to generate it.");
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
