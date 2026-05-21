"""
normalize_partial_rigs.py
=========================

Re-bakes the rest pose of every "partial" FBX in patials_source/ so its
bones share the same world-space orientations as the master FBX
(3d_men_default.fbx). The mesh skin is preserved by going through Blender's
"Apply Pose as Rest Pose" path: we rotate each bone in pose mode until its
world matrix matches the master, then `pose.armature_apply` bakes that pose
as the new rest pose while updating the skinned mesh's inverse bind-pose
matrices so the visible deformation is unchanged.

Why we need this
----------------
Hunyuan3D's per-generation FBX export doesn't lock a bone-axis convention.
Default3D's Spine bone has its local X axis aligned with world +X, but
Jay's Spine has local X flipped to -X (180° around Y) and Style3's is
rotated yet another way. When Unity's Mecanim Humanoid retargets the
shared Idle clip, those axis differences read as "lean forward" on one
variant and "lean back" on another. Normalising rest-pose orientations
sidesteps the whole retargeting mismatch class.

We do NOT touch bone head/tail positions or scale — only bone orientations
(roll + local axes). Bone lengths therefore stay per-variant, so the mesh
proportions remain faithful to the original generation.

Usage
-----
From a terminal:

    /Applications/Blender.app/Contents/MacOS/Blender \\
        --background \\
        --python "/Users/twinb00598897/Desktop/程式專案/OKR/CathayCrossing/Tools/normalize_partial_rigs.py"

The script reads from:
    /Users/twinb00598897/Desktop/patials_source/3d_men_default.fbx          (master)
    /Users/twinb00598897/Desktop/patials_source/3d_men_jay_partial.fbx      (follower)
    /Users/twinb00598897/Desktop/patials_source/3d_men_style3_partial.fbx   (follower)

…and writes normalised followers next to them with a `_normalized` suffix:
    3d_men_jay_partial_normalized.fbx
    3d_men_style3_partial_normalized.fbx

After it finishes:
  1. In Finder, delete the two original `_partial.fbx` files.
  2. Rename the `_normalized.fbx` files back to the original names
     (so re-running Tools › CathayCrossing › Import Character Partials in
     Unity still picks them up — that menu only looks at the original
     filenames).
  3. Switch to Unity and run Tools › CathayCrossing › Import Character
     Partials. Default3D should now share its rest-pose convention with
     every variant.
"""

import bpy
import os
import sys
import math
from mathutils import Matrix, Quaternion, Vector

# ─── Configuration ──────────────────────────────────────────────────────

SOURCE_DIR = "/Users/twinb00598897/Desktop/patials_source"
MASTER_FILENAME = "3d_men_default.fbx"
FOLLOWER_FILENAMES = [
    "3d_men_jay_partial.fbx",
    "3d_men_style3_partial.fbx",
]
OUTPUT_SUFFIX = "_normalized"

# ─── Blender helpers ────────────────────────────────────────────────────

def clear_scene():
    """Empty the scene + every data block so consecutive imports don't
    pile up materials/meshes from the previous file."""
    if bpy.context.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for collection in (
        bpy.data.armatures,
        bpy.data.meshes,
        bpy.data.materials,
        bpy.data.images,
        bpy.data.actions,
    ):
        for block in list(collection):
            collection.remove(block)


def import_fbx(filepath):
    """Import an FBX and return its armature object (None if the file
    contains no armature). `automatic_bone_orientation=False` keeps the
    bone axes exactly as the source file ships them — Blender's auto-orient
    helper would re-derive them and defeat the whole exercise."""
    bpy.ops.import_scene.fbx(
        filepath=filepath,
        automatic_bone_orientation=False,
        use_anim=False,
    )
    for obj in bpy.context.selected_objects:
        if obj.type == "ARMATURE":
            return obj
    for obj in bpy.data.objects:
        if obj.type == "ARMATURE":
            return obj
    return None


def collect_world_rest_rotations(armature):
    """Walk each bone in `armature` and return a dict mapping bone name to
    the bone's world-space rest rotation (Quaternion). Bones with no
    parent are taken relative to the armature itself; the calculation
    matches what Blender shows when you select the bone in pose mode at
    its rest pose."""
    rotations = {}
    for pbone in armature.pose.bones:
        world_mat = armature.matrix_world @ pbone.bone.matrix_local
        rotations[pbone.name] = world_mat.to_quaternion()
    return rotations


def align_bones_to_reference(armature, ref_rotations):
    """For each pose bone whose name appears in ref_rotations, set its
    world matrix so the world rotation matches the reference while keeping
    the bone's current world translation (so head/tail stay put — only the
    bone-local axes spin). Returns the count of bones touched."""
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.mode_set(mode="POSE")

    touched = 0
    for pbone in armature.pose.bones:
        if pbone.name not in ref_rotations:
            continue
        current_world_mat = armature.matrix_world @ pbone.bone.matrix_local
        target_world_mat = Matrix.LocRotScale(
            current_world_mat.translation,
            ref_rotations[pbone.name],
            current_world_mat.to_scale(),
        )
        pbone.matrix = target_world_mat
        touched += 1

    bpy.context.view_layer.update()
    bpy.ops.object.mode_set(mode="OBJECT")
    return touched


def apply_pose_as_rest(armature):
    """Bake the current pose as the new rest pose. This is the magic step
    — Blender re-computes every bone's matrix_local AND adjusts each
    skinned mesh's inverse-bind matrices so the visible deformation stays
    the same. Skin weights are untouched."""
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.mode_set(mode="POSE")
    bpy.ops.pose.select_all(action="SELECT")
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode="OBJECT")


def export_fbx(filepath):
    """Export everything in the scene as a single FBX, embedding textures
    so the result is self-contained (matches the input file's footprint)."""
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=filepath,
        use_selection=False,
        object_types={"ARMATURE", "MESH"},
        add_leaf_bones=False,
        bake_anim=False,
        embed_textures=True,
        path_mode="COPY",
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_NONE",
    )


# ─── Pipeline ───────────────────────────────────────────────────────────

def main():
    master_path = os.path.join(SOURCE_DIR, MASTER_FILENAME)
    if not os.path.isfile(master_path):
        print(f"[normalize] master FBX missing: {master_path}", file=sys.stderr)
        sys.exit(1)

    # Pass 1 — read the master's rest rotations.
    clear_scene()
    master_arm = import_fbx(master_path)
    if master_arm is None:
        print(f"[normalize] no armature in master {master_path}", file=sys.stderr)
        sys.exit(1)
    ref_rotations = collect_world_rest_rotations(master_arm)
    print(f"[normalize] master '{MASTER_FILENAME}' contributes "
          f"{len(ref_rotations)} bone orientation(s) to the reference set.")

    # Pass 2 — align each follower and export.
    for follower_name in FOLLOWER_FILENAMES:
        follower_path = os.path.join(SOURCE_DIR, follower_name)
        if not os.path.isfile(follower_path):
            print(f"[normalize] follower missing, skipping: {follower_path}")
            continue

        clear_scene()
        arm = import_fbx(follower_path)
        if arm is None:
            print(f"[normalize] no armature in {follower_path}, skipping")
            continue

        touched = align_bones_to_reference(arm, ref_rotations)
        print(f"[normalize] {follower_name}: aligned {touched} bone(s) to "
              f"master orientation.")

        apply_pose_as_rest(arm)
        print(f"[normalize] {follower_name}: baked pose as rest pose.")

        stem, ext = os.path.splitext(follower_path)
        out_path = stem + OUTPUT_SUFFIX + ext
        export_fbx(out_path)
        print(f"[normalize] wrote → {out_path}")

    print("[normalize] done.")


if __name__ == "__main__":
    main()
