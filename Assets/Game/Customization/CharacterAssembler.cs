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
        // bone. We observed Style3 ship with Hips rotated 90° and the
        // left leg chain flipped 170°; Jay flips the entire right shin
        // 176°; both flip the Spine chain ~178°.
        //
        // Solution: apply the rotation-correction bindpose so the
        // mesh appears at the variant's authored rest pose on the
        // rig. The correction collapses to identity when variantRot ≈
        // rigRot, so aligned bones cost nothing.
        //
        // Three policies depending on the bone region:
        //   • Arm chain (shoulder → finger): ALWAYS skip correction.
        //     Variant arm rest poses (Style3 elbows out, Jay hands
        //     behind back) shouldn't override the rig's natural
        //     hands-at-sides stance — bodies should share a silhouette.
        //
        //   • Head / neck: gated by FlipDetectionThresholdDegrees.
        //     Small deltas (< 60°) are per-variant tilt offsets we
        //     skip so the head sits upright on the rig; large deltas
        //     (≥ 60°) are Hunyuan3D's random axis flips and DO get
        //     un-flipped (e.g. Jay's head-axis 180° flip — without
        //     correction the head pointed backwards).
        //
        //   • Trunk + legs (spine, hips, legs, feet): ALWAYS correct.
        //     Hunyuan3D ships these with 90°-178° flips; correction
        //     keeps the torso / legs the right way up.

        // Bone-name patterns. Substring match, case-insensitive.
        // Hunyuan3D / Mixamo / Unity Humanoid all use at least one of
        // these tokens for the named region.
        static readonly string[] HeadNeckBoneTokens =
        {
            "head", "neck",
        };
        static readonly string[] ArmChainBoneTokens =
        {
            "shoulder", "clavicle", "arm",      // shoulder + upper arm + forearm
            "hand", "wrist",                     // wrist + palm
            "finger", "thumb", "index", "middle", "ring", "pinky",
        };

        // Anything above this is treated as a Hunyuan3D random axis
        // flip and MUST be un-flipped by the correction bindpose.
        // Below this it's a per-variant pose offset we'd rather not
        // preserve (so the rig drives the visible rest pose).
        // 60° is well above typical artistic pose differences (≤30°)
        // and well below 90° / 180° axis flips.
        const float FlipDetectionThresholdDegrees = 60f;

        static bool MatchesAnyToken(string boneName, string[] tokens)
        {
            if (string.IsNullOrEmpty(boneName)) return false;
            string n = boneName.ToLowerInvariant();
            foreach (var tok in tokens)
            {
                if (n.Contains(tok)) return true;
            }
            return false;
        }

        // For head/neck: snap an arbitrary rotation to the nearest
        // 90°-multiple rotation. Discards any residual artistic tilt
        // offset that's bundled with a Hunyuan3D axis flip, so a
        // "180° flip + 10° tilt" composite collapses to a clean 180°
        // flip and the head ends up upright instead of tilted.
        static Quaternion SnapTo90Multiple(Quaternion q)
        {
            Vector3 e = q.eulerAngles;
            // eulerAngles returns [0, 360); normalise to (-180, 180]
            // so 350° rounds to 0 (i.e. -10°), not 360°.
            float nx = NormalizeAngle(e.x);
            float ny = NormalizeAngle(e.y);
            float nz = NormalizeAngle(e.z);
            float sx = Mathf.Round(nx / 90f) * 90f;
            float sy = Mathf.Round(ny / 90f) * 90f;
            float sz = Mathf.Round(nz / 90f) * 90f;
            return Quaternion.Euler(sx, sy, sz);
        }

        static float NormalizeAngle(float deg)
        {
            deg = deg % 360f;
            if (deg > 180f) deg -= 360f;
            if (deg < -180f) deg += 360f;
            return deg;
        }

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
                        // Decide per-bone whether to apply rotation
                        // correction. Three regions, three policies:
                        //
                        // 1. Arm chain (shoulder → finger): ALWAYS skip.
                        //    Variant arm rest poses (Style3 elbows out,
                        //    Jay hands behind) shouldn't override the
                        //    rig's natural hands-at-sides stance.
                        //
                        // 2. Head + neck: skip if the rotation delta
                        //    looks like a pose offset (< 60°), correct
                        //    if it looks like a Hunyuan3D random axis
                        //    flip (≥ 60°). Without this gate, Jay's
                        //    head-bone tilt got preserved and the head
                        //    looked askew; with a blanket skip, Jay's
                        //    180° head-axis flip wasn't un-flipped and
                        //    the head pointed backwards.
                        //
                        // 3. Trunk + legs (spine, hips, legs, feet):
                        //    ALWAYS correct. Hunyuan3D ships variants
                        //    with 90°-178° flips on these bones; without
                        //    correction the torso / legs render
                        //    upside-down or twisted.
                        Quaternion variantRot = variantBoneWorld[i].rotation;
                        Quaternion rigRot     = rigBone.rotation;
                        bool isArm  = MatchesAnyToken(rigBone.name, ArmChainBoneTokens);
                        bool isHead = MatchesAnyToken(rigBone.name, HeadNeckBoneTokens);

                        // Decide the correction quaternion per region:
                        //   • Arm chain → identity (no correction)
                        //   • Head/neck →
                        //        small delta (< 60°): identity
                        //        large delta (≥ 60°): snap to nearest
                        //          90°-multiple rotation. This keeps the
                        //          axis-flip (e.g. 180° around Y) but
                        //          discards the residual artistic tilt
                        //          (e.g. +10° around X) that Hunyuan3D
                        //          bundles in. Without snap, Jay's head
                        //          came out upright-but-tilted; with
                        //          snap it lands square upright.
                        //   • Trunk + legs → full correction
                        Quaternion correction;
                        if (isArm)
                        {
                            correction = Quaternion.identity;
                        }
                        else if (isHead)
                        {
                            float deltaDeg = Quaternion.Angle(variantRot, rigRot);
                            if (deltaDeg >= FlipDetectionThresholdDegrees)
                            {
                                Quaternion full = variantRot * Quaternion.Inverse(rigRot);
                                correction = SnapTo90Multiple(full);
                            }
                            else
                            {
                                correction = Quaternion.identity;
                            }
                        }
                        else
                        {
                            correction = variantRot * Quaternion.Inverse(rigRot);
                        }

                        // Identity correction → degenerates to original
                        // bindpose; we skip the Matrix4x4.Rotate work
                        // for that case but the result is the same.
                        if (correction == Quaternion.identity)
                        {
                            newBindposes[i] = originalBindposes[i];
                        }
                        else
                        {
                            newBindposes[i] = Matrix4x4.Rotate(correction)
                                              * originalBindposes[i];
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
