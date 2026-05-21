using System.Collections.Generic;
using CathayCrossing.Characters;
using UnityEngine;

namespace CathayCrossing.Customization
{
    /// <summary>
    /// Builds a customised character by spawning the catalog's base rig and
    /// hot-swapping individual mesh parts per slot.
    ///
    /// How it works
    /// ------------
    /// 1. Instantiate the base character FBX (Default3D) — gets us a full,
    ///    self-consistent skeleton + Animator + every default mesh part.
    /// 2. For every slot whose selection differs from the base:
    ///    a. Disable the base's existing parts for that slot.
    ///    b. Instantiate the chosen variant's FBX off-screen.
    ///    c. Re-parent the variant's SkinnedMeshRenderer GameObjects for
    ///       that slot under the base rig.
    ///    d. Remap each transplanted SMR's bones array to point at the
    ///       base rig's same-named transforms.
    ///    e. Destroy the variant's leftover armature / unused parts.
    ///
    /// What this trade-off buys us
    /// ---------------------------
    /// Single-bone-dominant slots (Hair, Head, Shoes) transplant cleanly —
    /// the part's bindPoses describe its rest position relative to the
    /// single bone it deforms with, so the result looks correct.
    ///
    /// Multi-bone slots (Body, Pants) will show some skin distortion
    /// because the variant's bindPoses were authored against the variant's
    /// own bone positions, which differ slightly from Default3D's. That's
    /// the known limit of the approach — fully fixing it requires either
    /// re-skinning the variant meshes in Blender or having the source
    /// pipeline lock to one consistent rig from the start.
    /// </summary>
    public static class CharacterAssembler
    {
        // Bindpose retargeting policy
        // ----------------------------
        // Hunyuan3D's auto-rigger doesn't lock an axis convention per
        // bone, so different generations randomly flip individual bones.
        // We observed Style3 ship with Hips rotated 90° and the left leg
        // chain flipped 170°; Jay flips the entire right shin 176°; both
        // flip the Spine chain ~178°. Maintaining a hand-written list of
        // "bones to retarget" therefore loses every time a fresh variant
        // arrives with a different flip.
        //
        // Solution: dynamically decide per-bone at transplant time.
        // Whenever a variant bone's world rotation differs from the rig
        // bone's by more than this threshold, we apply the rotation-
        // correction bindpose to compensate (un-flip the mesh). For bones
        // whose rotations already agree (within the threshold), we use
        // the original bindpose so the mesh follows the rig's bone
        // position directly with no extra math.
        const float RetargetThresholdDegrees = 45f;

        /// <summary>
        /// Build the assembled character under <paramref name="parent"/> and
        /// return its root GameObject. <paramref name="selection"/> maps
        /// each slot to a source character id; slots whose value equals the
        /// catalog's <c>baseCharacterId</c> are no-ops (the base already
        /// shows its own mesh for that slot).
        /// </summary>
        public static GameObject Assemble(
            CharacterPartCatalog catalog,
            Dictionary<CharacterPartSlot, string> selection,
            Dictionary<string, GameObject> bodyByCharacterId,
            Transform parent)
        {
            if (catalog == null)
            {
                Debug.LogError("[CharacterAssembler] No catalog supplied.");
                return null;
            }
            if (bodyByCharacterId == null || !bodyByCharacterId.TryGetValue(catalog.baseCharacterId, out var baseFbx) || baseFbx == null)
            {
                Debug.LogError($"[CharacterAssembler] Base character '{catalog.baseCharacterId}' not found in bodyByCharacterId map.");
                return null;
            }

            // 1. Instantiate base rig.
            var root = Object.Instantiate(baseFbx, parent);
            root.name = "Assembled";

            // 2. Catalogue every transform on the base for bone re-binding.
            var bonesByName = new Dictionary<string, Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (t == null || string.IsNullOrEmpty(t.name)) continue;
                if (!bonesByName.ContainsKey(t.name)) bonesByName[t.name] = t;
            }

            // 3. Index base SMRs by GameObject name so we can disable the
            //    correct parts when a slot gets overridden.
            var baseSMRsByName = new Dictionary<string, SkinnedMeshRenderer>();
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
            {
                if (smr != null && !baseSMRsByName.ContainsKey(smr.gameObject.name))
                {
                    baseSMRsByName[smr.gameObject.name] = smr;
                }
            }

            // 4. For each slot the user picked something other than the
            //    base, transplant the variant's parts in. We track
            //    already-transplanted (characterId, partName) pairs so a
            //    combined body+pants mesh — picked for both slots — isn't
            //    cloned and drawn twice.
            var transplantedAlready = new HashSet<string>();
            foreach (var kvp in selection)
            {
                var slot = kvp.Key;
                var chosen = kvp.Value;
                if (string.IsNullOrEmpty(chosen) || chosen == catalog.baseCharacterId) continue;

                // Disable the base's same-slot parts so we don't draw both.
                var baseEntry = catalog.Find(catalog.baseCharacterId, slot);
                if (baseEntry != null && baseEntry.partNames != null)
                {
                    foreach (var pname in baseEntry.partNames)
                    {
                        if (string.IsNullOrEmpty(pname)) continue;
                        if (baseSMRsByName.TryGetValue(pname, out var smr) && smr != null) smr.enabled = false;
                    }
                }

                // Find the variant's source FBX and entry for this slot.
                var variantEntry = catalog.Find(chosen, slot);
                if (variantEntry == null || variantEntry.partNames == null) continue;
                if (!bodyByCharacterId.TryGetValue(chosen, out var variantFbx) || variantFbx == null) continue;

                // Instantiate the variant in the SAME world transform as the
                // assembled root. We snapshot variantBone.localToWorld below
                // and feed it into the retargeted bindpose formula; if the
                // variant sits at world origin while the assembled root is
                // somewhere else (e.g. the office player parked at x=10),
                // the bindpose pulls Spine vertices back toward variant's
                // origin-frame coordinates → the upper body stretches
                // out as a long horizontal line from the player to the
                // world origin (visible as a long shadow). Matching world
                // transforms collapses that mismatch.
                var variantInst = Object.Instantiate(variantFbx);
                variantInst.hideFlags = HideFlags.HideAndDontSave;
                variantInst.transform.position   = root.transform.position;
                variantInst.transform.rotation   = root.transform.rotation;
                variantInst.transform.localScale = root.transform.lossyScale;
                variantInst.SetActive(false);

                var variantSMRsByName = new Dictionary<string, SkinnedMeshRenderer>();
                foreach (var smr in variantInst.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
                {
                    if (smr != null && !variantSMRsByName.ContainsKey(smr.gameObject.name))
                    {
                        variantSMRsByName[smr.gameObject.name] = smr;
                    }
                }

                foreach (var pname in variantEntry.partNames)
                {
                    if (string.IsNullOrEmpty(pname)) continue;
                    string dedupKey = chosen + "::" + pname;
                    if (transplantedAlready.Contains(dedupKey)) continue;
                    if (!variantSMRsByName.TryGetValue(pname, out var srcSMR) || srcSMR == null) continue;
                    TransplantSMR(srcSMR, root.transform, bonesByName, slot, chosen);
                    transplantedAlready.Add(dedupKey);
                }

                // Throw away the variant's leftover armature + unused parts.
                if (Application.isPlaying) Object.Destroy(variantInst);
                else Object.DestroyImmediate(variantInst);
            }

            return root;
        }

        // Re-parents one SkinnedMeshRenderer's GameObject under the assembled
        // root and re-binds its bones array onto the rig's transforms.
        //
        // The clever bit: we don't just swap bone Transform references —
        // we ALSO rebake the mesh's bindPoses so the rest-pose orientation
        // differences between the source rig and the destination rig
        // cancel out. Without this, variants whose Spine bone axes are
        // flipped 180° (Jay / Style3 are exported that way by Hunyuan3D)
        // end up with their bodies facing backwards on Default3D's rig.
        //
        // Math (per bone):
        //   newBindpose = rigBone.worldToLocal_atRest *
        //                 variantBone.localToWorld_atRest *
        //                 originalBindpose
        //
        // Why this works: at rest with the rig, the chain collapses to
        // (variantBone.localToWorld_atRest * originalBindpose * vertex),
        // which is exactly where the vertex sat when the artist baked the
        // mesh on the variant's rig. When the rig animates, the extra
        // (rigBone.localToWorld_animated * rigBone.worldToLocal_atRest)
        // factor applies the rig's motion relative to its own rest pose,
        // so the mesh follows along — but starting from the variant's
        // authored rest pose, not the rig's.
        //
        // We need to capture variantBone.localToWorldMatrix BEFORE we
        // overwrite srcSMR.bones (which destroys our access to the
        // variant's original bone transforms), so we do that as the first
        // step.
        static void TransplantSMR(SkinnedMeshRenderer srcSMR,
                                  Transform assembledRoot,
                                  Dictionary<string, Transform> bonesByName,
                                  CharacterPartSlot slot,
                                  string sourceCharacterId)
        {
            // Snapshot the variant's bone rest poses + original bindposes
            // before we touch anything.
            var originalBones = srcSMR.bones;
            var originalMesh = srcSMR.sharedMesh;
            var originalBindposes = originalMesh != null ? originalMesh.bindposes : null;

            Matrix4x4[] variantBoneWorld = null;
            if (originalBones != null)
            {
                variantBoneWorld = new Matrix4x4[originalBones.Length];
                for (int i = 0; i < originalBones.Length; i++)
                {
                    if (originalBones[i] != null) variantBoneWorld[i] = originalBones[i].localToWorldMatrix;
                }
            }

            // Re-parent the SMR's GameObject under the assembled root.
            var go = srcSMR.gameObject;
            go.SetActive(true);
            go.transform.SetParent(assembledRoot, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.name = $"{slot}_{sourceCharacterId}_{go.name}";

            // Build the new bones[] array + adjusted bindposes in lockstep
            // so the indices stay aligned.
            Transform[] newBones = originalBones != null ? new Transform[originalBones.Length] : null;
            Matrix4x4[] newBindposes = (originalBindposes != null && originalBones != null)
                ? new Matrix4x4[originalBindposes.Length]
                : null;

            if (originalBones != null)
            {
                for (int i = 0; i < originalBones.Length; i++)
                {
                    var ob = originalBones[i];
                    if (ob == null)
                    {
                        // Vertices weighted to a missing bone slot render
                        // at world origin in Unity — fatal when the player
                        // isn't at the origin (the office mesh would
                        // stretch from the player back to (0,0,0)). Park
                        // the slot on the assembled root so dead weights
                        // contribute zero offset instead.
                        if (newBones != null) newBones[i] = assembledRoot;
                        if (newBindposes != null && i < originalBindposes.Length)
                            newBindposes[i] = originalBindposes[i];
                        continue;
                    }
                    if (!bonesByName.TryGetValue(ob.name, out var rigBone))
                    {
                        // Same case as above — variant ships a bone the
                        // rig doesn't have. Fallback prevents the long
                        // origin-tether stretch.
                        if (newBones != null) newBones[i] = assembledRoot;
                        if (newBindposes != null && i < originalBindposes.Length)
                            newBindposes[i] = originalBindposes[i];
                        continue;
                    }
                    newBones[i] = rigBone;
                    if (newBindposes != null && i < originalBindposes.Length)
                    {
                        // Decide per-bone: if the variant's bone rotation
                        // differs from the rig's by more than the
                        // threshold, this bone is one of Hunyuan3D's
                        // randomly-flipped ones — apply the rotation-
                        // correction bindpose to un-flip it. Otherwise
                        // the bone's axes already agree well enough that
                        // the original bindpose places the mesh correctly
                        // at the rig's bone position.
                        Quaternion variantRot = variantBoneWorld[i].rotation;
                        Quaternion rigRot = rigBone.rotation;
                        float deltaDeg = Quaternion.Angle(variantRot, rigRot);

                        if (deltaDeg > RetargetThresholdDegrees)
                        {
                            // Rotation-correction bindpose:
                            //   newBindpose = R(variant.rot × rig.rot⁻¹) × originalBindpose
                            //   ⇒ worldPos = rig.pos + (vertex − variant.pos)
                            // This is consistent with the no-correction
                            // branch for non-flipped bones (whose rig.rot ≈
                            // variant.rot makes that R(...) ≈ I), so
                            // vertices weighted across flipped + unflipped
                            // bones blend smoothly with no inter-bone
                            // stretch.
                            Quaternion correction = variantRot * Quaternion.Inverse(rigRot);
                            newBindposes[i] = Matrix4x4.Rotate(correction)
                                              * originalBindposes[i];
                        }
                        else
                        {
                            // Axes match — original bindpose is enough.
                            newBindposes[i] = originalBindposes[i];
                        }
                    }
                }
                srcSMR.bones = newBones;
            }

            if (srcSMR.rootBone != null && bonesByName.TryGetValue(srcSMR.rootBone.name, out var rb))
            {
                srcSMR.rootBone = rb;
            }

            if (newBindposes != null && originalMesh != null)
            {
                // Clone the mesh so we don't mutate the shared asset on
                // disk. The clone is only referenced by this assembled
                // instance — when the assembly is destroyed, the clone is
                // garbage-collected along with it.
                var meshCopy = Object.Instantiate(originalMesh);
                meshCopy.name = originalMesh.name + "_Retargeted";
                meshCopy.bindposes = newBindposes;
                srcSMR.sharedMesh = meshCopy;
            }

            // Force per-frame bounds recalculation so the transplanted mesh
            // doesn't get culled mid-animation. Without this, running /
            // walking poses occasionally push a vertex outside the SMR's
            // static localBounds (which was baked at the variant's rest
            // pose) and Unity renders a thin smear from the culled region
            // back to the visible mesh — the "line trailing off the foot"
            // we saw on Style3 while moving.
            srcSMR.updateWhenOffscreen = true;
            srcSMR.enabled = true;
        }
    }
}
