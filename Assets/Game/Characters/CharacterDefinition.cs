using UnityEngine;

namespace CathayCrossing.Characters
{
    /// <summary>
    /// One asset per playable character. Stored alongside the character's mesh
    /// at <c>Resources/Characters/&lt;Name&gt;/&lt;Name&gt;.asset</c> so the
    /// player-spawner can find every available character with a single
    /// <see cref="Resources.LoadAll{CharacterDefinition}"/> call.
    ///
    /// The <see cref="id"/> string is the stable lookup key — it must match the
    /// containing folder name (<c>Default</c>, <c>Jay</c>, …) so the
    /// CharacterSelect scene can store the chosen id in PlayerPrefs and the
    /// spawner can resolve it back to a definition.
    /// </summary>
    [CreateAssetMenu(menuName = "CathayCrossing/Character Definition", fileName = "Character")]
    public class CharacterDefinition : ScriptableObject
    {
        [Tooltip("Stable lookup key. Must match the containing folder name " +
                 "under Resources/Characters/.")]
        public string id = "Default";

        [Tooltip("Friendly label shown in the character-select UI.")]
        public string displayName = "Default";

        [Tooltip("The rigged FBX (with Humanoid avatar) supplying the visible " +
                 "mesh + materials. For the master character this is also the " +
                 "rig source; for swappable variants this is the mesh donor " +
                 "that gets re-bound onto rigSource's bones at spawn time.")]
        public GameObject body;

        [Tooltip("Optional shared-rig source. When set, the spawner " +
                 "instantiates this FBX (its skeleton + Animator + Avatar) " +
                 "and re-binds the variant's SkinnedMeshRenderers from " +
                 "'body' onto rigSource's bones by name. Leave null when " +
                 "this character ships its own rig.")]
        public GameObject rigSource;

        [Tooltip("Per-character AnimatorController built by " +
                 "Tools › CathayCrossing › Setup <Name> Character.")]
        public RuntimeAnimatorController controller;

        [Tooltip("Optional spine tilt applied AFTER the Animator updates " +
                 "(LateUpdate). Lets us straighten variants whose FBX rest " +
                 "pose ships with a baked-in lean — Hunyuan3D's exporter " +
                 "doesn't lock a bone-axis convention, so each generation " +
                 "of Jay/Style3 can land a few degrees off vertical. Zero " +
                 "= no correction. Positive X pitches the upper body " +
                 "forward (counter a backward lean).")]
        public Vector3 spineCorrectionEuler;
    }
}
