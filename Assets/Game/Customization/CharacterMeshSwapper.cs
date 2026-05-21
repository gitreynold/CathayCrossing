using System.Collections.Generic;
using UnityEngine;

namespace CathayCrossing.Customization
{
    /// <summary>
    /// Swaps the visible meshes of a rigged base character with meshes
    /// pulled from a variant FBX. The rig (skeleton + Animator + Avatar)
    /// belongs to the base; the variant only contributes mesh + material
    /// data, then gets discarded.
    ///
    /// Why we do this instead of letting every variant carry its own rig:
    /// Hunyuan3D generations don't lock a bone-axis convention, so variant
    /// FBXs ship with their Spine local axes flipped or rotated relative
    /// to the master. Mecanim's Humanoid retarget then turns "lean forward"
    /// into "lean backward" on those variants. Driving every variant's
    /// mesh from a single, consistent skeleton sidesteps the whole class
    /// of axis-mismatch bugs — the variant mesh's bindPoses encode its
    /// own rest pose, the base's bones drive the animation, and the GPU
    /// composes the two automatically.
    ///
    /// Bone matching is by name. Default3D / Style3 / JayPartial all use
    /// the same naming scheme (root → Hips → Spine{1,2} → Neck → Head plus
    /// the four limbs), so the rebind is unambiguous.
    /// </summary>
    public static class CharacterMeshSwapper
    {
        /// <summary>
        /// Replaces every SkinnedMeshRenderer mesh on <paramref name="rig"/>
        /// with the corresponding mesh from <paramref name="meshSource"/>
        /// (matched by GameObject name). For variant meshes that have no
        /// same-named slot on the base, a new SMR is spawned under the base
        /// root. Base SMRs that the variant doesn't cover are hidden.
        ///
        /// <paramref name="meshSource"/> is consumed: it is destroyed after
        /// the swap so its orphan armature / leftover GameObjects don't
        /// pollute the scene.
        /// </summary>
        public static void SwapMeshes(GameObject rig, GameObject meshSource)
        {
            if (rig == null || meshSource == null) return;

            // Build base bone lookup so each variant SMR can have its
            // .bones array remapped to the rig's transforms.
            var rigBoneByName = new Dictionary<string, Transform>();
            foreach (var t in rig.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (t == null || string.IsNullOrEmpty(t.name)) continue;
                if (!rigBoneByName.ContainsKey(t.name)) rigBoneByName[t.name] = t;
            }

            // Same lookup for base SMRs so we can reuse them in place
            // (preserves whatever the base prefab set up — lighting probes,
            // motion vectors, blend shape weights, etc.).
            var rigSMRs = rig.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);
            var rigSMRByName = new Dictionary<string, SkinnedMeshRenderer>();
            foreach (var s in rigSMRs)
            {
                if (s == null) continue;
                if (!rigSMRByName.ContainsKey(s.gameObject.name)) rigSMRByName[s.gameObject.name] = s;
            }

            var processed = new HashSet<string>();
            var sourceSMRs = meshSource.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);
            foreach (var src in sourceSMRs)
            {
                if (src == null || src.sharedMesh == null) continue;
                SkinnedMeshRenderer dst;
                if (!rigSMRByName.TryGetValue(src.gameObject.name, out dst))
                {
                    // Variant has a part the base doesn't ship — spawn a
                    // sibling SMR under the rig root.
                    var go = new GameObject(src.gameObject.name);
                    go.transform.SetParent(rig.transform, false);
                    dst = go.AddComponent<SkinnedMeshRenderer>();
                }
                ApplySwap(src, dst, rigBoneByName);
                processed.Add(dst.gameObject.name);
            }

            // Hide any base parts the variant doesn't cover. They're still
            // bound to the rig's bones (so don't error in builds), just not
            // rendered.
            foreach (var s in rigSMRs)
            {
                if (s == null) continue;
                if (!processed.Contains(s.gameObject.name)) s.enabled = false;
            }

            // Tear down the variant's leftover hierarchy — we only needed
            // its mesh data. Use DestroyImmediate so the spawner can keep
            // a single-frame reference to the result.
            if (Application.isPlaying) Object.Destroy(meshSource);
            else Object.DestroyImmediate(meshSource);
        }

        static void ApplySwap(SkinnedMeshRenderer src, SkinnedMeshRenderer dst,
                              Dictionary<string, Transform> rigBoneByName)
        {
            dst.sharedMesh = src.sharedMesh;
            dst.sharedMaterials = src.sharedMaterials;
            dst.localBounds = src.localBounds;
            dst.updateWhenOffscreen = src.updateWhenOffscreen;
            dst.skinnedMotionVectors = src.skinnedMotionVectors;
            dst.quality = src.quality;
            dst.enabled = true;

            // Remap variant's bone array to the rig's Transforms — same
            // order, same names, but the actual Transforms are the rig's
            // animated bones.
            var srcBones = src.bones;
            var newBones = new Transform[srcBones != null ? srcBones.Length : 0];
            for (int i = 0; i < newBones.Length; i++)
            {
                var b = srcBones[i];
                if (b == null) continue;
                if (rigBoneByName.TryGetValue(b.name, out var rigBone)) newBones[i] = rigBone;
            }
            dst.bones = newBones;

            if (src.rootBone != null && rigBoneByName.TryGetValue(src.rootBone.name, out var rigRoot))
            {
                dst.rootBone = rigRoot;
            }
        }
    }
}
