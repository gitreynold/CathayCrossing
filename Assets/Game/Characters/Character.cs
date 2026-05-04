using System.Collections.Generic;
using UnityEngine;

namespace CathayCrossing.Characters
{
    /// <summary>
    /// Runtime character: a base body (SkinnedMeshRenderer rigged on a Humanoid
    /// skeleton) plus zero or more swappable parts (hair, top, bottom...) that
    /// share the same skeleton via bone re-binding.
    ///
    /// Re-binding works by name: when a part prefab is instantiated, its
    /// SkinnedMeshRenderer.bones array is rewritten to point at this
    /// character's actual bone Transforms (matched by Transform.name). The
    /// part's own bone GameObjects under its root are then discarded — they
    /// were just authoring-time placeholders so the artist could rig in
    /// isolation.
    ///
    /// Skinned meshes from different DCC tools tend to use different bone
    /// naming conventions (Mixamo's "mixamorig:Hips" vs Unity Humanoid's
    /// "Hips"). Pick ONE convention for the project and enforce it on every
    /// imported part — the re-bind will silently leave bones unmapped and the
    /// mesh will collapse to the origin if names don't match.
    /// </summary>
    public class Character : MonoBehaviour
    {
        [Header("Skeleton")]
        [Tooltip("Root of the base humanoid skeleton (typically 'Hips' or 'Armature').")]
        public Transform skeletonRoot;

        [Header("Base body")]
        [Tooltip("Renderer for the base body mesh (skin + base clothes underlayer).")]
        public SkinnedMeshRenderer baseBodyRenderer;

        [Tooltip("Renderer holding face BlendShapes. Leave empty to fall back to baseBodyRenderer.")]
        public SkinnedMeshRenderer headRenderer;

        [Tooltip("Material index on baseBodyRenderer tinted by face.skinColor. -1 = skip.")]
        public int skinMaterialIndex = 0;

        [Tooltip("Material index on headRenderer tinted by face.eyeColor. -1 = skip.")]
        public int eyeMaterialIndex = -1;

        [Header("Parts")]
        [Tooltip("Parent for instantiated part GameObjects. Auto-created if null.")]
        public Transform slotsContainer;

        readonly Dictionary<CharacterPartSlot, GameObject> _equipped = new();
        Dictionary<string, Transform> _boneMap;

        public void ApplyAppearance(CharacterAppearance appearance)
        {
            if (appearance == null) return;

            foreach (CharacterPartSlot slot in System.Enum.GetValues(typeof(CharacterPartSlot)))
            {
                Equip(slot, appearance.Get(slot), appearance, applyColorsNow: false);
            }

            ApplyFace(appearance.face);
            ApplyAllColors(appearance);
        }

        public void Equip(CharacterPartSlot slot, CharacterPartDefinition part) =>
            Equip(slot, part, appearance: null, applyColorsNow: true);

        void Equip(CharacterPartSlot slot, CharacterPartDefinition part,
                   CharacterAppearance appearance, bool applyColorsNow)
        {
            if (_equipped.TryGetValue(slot, out var existing) && existing != null)
            {
                if (Application.isPlaying) Destroy(existing); else DestroyImmediate(existing);
                _equipped.Remove(slot);
            }

            if (part == null || part.prefab == null) return;

            var container = EnsureSlotsContainer();
            var instance = Instantiate(part.prefab, container);
            instance.name = $"Slot_{slot}_{part.partId}";

            foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
            {
                RebindToSkeleton(smr);
                if (applyColorsNow && appearance != null)
                {
                    ApplyPartColors(smr, part, appearance);
                }
            }

            _equipped[slot] = instance;
        }

        public void ApplyFace(CharacterFaceData face)
        {
            var head = headRenderer != null ? headRenderer : baseBodyRenderer;
            if (head != null && head.sharedMesh != null && face.blendShapes != null)
            {
                var mesh = head.sharedMesh;
                foreach (var bs in face.blendShapes)
                {
                    if (string.IsNullOrEmpty(bs.name)) continue;
                    int idx = mesh.GetBlendShapeIndex(bs.name);
                    if (idx >= 0) head.SetBlendShapeWeight(idx, bs.weight);
                }
            }

            if (baseBodyRenderer != null && skinMaterialIndex >= 0)
            {
                SetMaterialColor(baseBodyRenderer, skinMaterialIndex, face.skinColor);
            }
            if (head != null && eyeMaterialIndex >= 0)
            {
                SetMaterialColor(head, eyeMaterialIndex, face.eyeColor);
            }
        }

        void ApplyAllColors(CharacterAppearance appearance)
        {
            foreach (var kv in _equipped)
            {
                var part = appearance.Get(kv.Key);
                if (part == null || kv.Value == null) continue;
                foreach (var smr in kv.Value.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
                {
                    ApplyPartColors(smr, part, appearance);
                }
            }
        }

        static void ApplyPartColors(SkinnedMeshRenderer smr, CharacterPartDefinition part,
                                    CharacterAppearance appearance)
        {
            if (part.colorRoles == null) return;
            foreach (var binding in part.colorRoles)
            {
                if (binding.role == CharacterColorRole.None) continue;
                SetMaterialColor(smr, binding.materialIndex, appearance.GetRoleColor(binding.role));
            }
        }

        // ─── Bone re-binding ────────────────────────────────────────────────
        void RebindToSkeleton(SkinnedMeshRenderer smr)
        {
            if (skeletonRoot == null || smr == null) return;
            EnsureBoneMap();

            var srcBones = smr.bones;
            var newBones = new Transform[srcBones.Length];
            for (int i = 0; i < srcBones.Length; i++)
            {
                var src = srcBones[i];
                if (src != null && _boneMap.TryGetValue(src.name, out var mapped))
                {
                    newBones[i] = mapped;
                }
            }
            smr.bones = newBones;

            if (smr.rootBone != null && _boneMap.TryGetValue(smr.rootBone.name, out var newRoot))
            {
                smr.rootBone = newRoot;
            }
            else
            {
                smr.rootBone = skeletonRoot;
            }
        }

        void EnsureBoneMap()
        {
            if (_boneMap != null) return;
            _boneMap = new Dictionary<string, Transform>();
            foreach (var t in skeletonRoot.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                _boneMap[t.name] = t;
            }
        }

        Transform EnsureSlotsContainer()
        {
            if (slotsContainer != null) return slotsContainer;
            var go = new GameObject("Slots");
            go.transform.SetParent(transform, false);
            slotsContainer = go.transform;
            return slotsContainer;
        }

        // ─── Material helpers (MaterialPropertyBlock — no material instances) ─
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int LegacyColorId = Shader.PropertyToID("_Color");
        static MaterialPropertyBlock _mpb;

        static void SetMaterialColor(Renderer renderer, int materialIndex, Color color)
        {
            if (renderer == null) return;
            if (materialIndex < 0 || materialIndex >= renderer.sharedMaterials.Length) return;

            _mpb ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_mpb, materialIndex);
            _mpb.SetColor(BaseColorId, color);
            _mpb.SetColor(LegacyColorId, color);
            renderer.SetPropertyBlock(_mpb, materialIndex);
        }

        void OnDestroy()
        {
            _equipped.Clear();
            _boneMap = null;
        }
    }
}
