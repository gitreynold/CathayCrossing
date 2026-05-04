using UnityEngine;

namespace CathayCrossing.Characters
{
    /// <summary>
    /// One swappable cosmetic part (a hair, a shirt, a pair of pants...).
    /// The prefab MUST contain a SkinnedMeshRenderer rigged against the
    /// project's shared Humanoid skeleton template — at runtime
    /// <see cref="Character"/> re-binds its bones onto the actual character's
    /// skeleton by matching bone names. Bone names that exist in the part
    /// but not on the target character will be silently skipped.
    /// </summary>
    [CreateAssetMenu(menuName = "CathayCrossing/Character/Part Definition", fileName = "Part_")]
    public class CharacterPartDefinition : ScriptableObject
    {
        [Tooltip("Stable id used in saves and lookup. Don't rename after shipping.")]
        public string partId;

        public CharacterPartSlot slot;

        [Tooltip("Shown in character-editor UI.")]
        public string displayName;

        [Tooltip("Prefab whose root has a SkinnedMeshRenderer. Bones will be re-bound.")]
        public GameObject prefab;

        [Tooltip("Optional thumbnail for character-editor UI.")]
        public Sprite preview;

        [Tooltip("Material slot index → tintable color role. Empty = no tinting. " +
                 "Used by Character to apply hair/cloth colors from the appearance.")]
        public ColorRoleBinding[] colorRoles;
    }

    public enum CharacterColorRole
    {
        None = 0,
        Hair = 1,
        Primary = 2,
        Secondary = 3,
    }

    [System.Serializable]
    public struct ColorRoleBinding
    {
        [Tooltip("Index into SkinnedMeshRenderer.materials.")]
        public int materialIndex;
        public CharacterColorRole role;
    }
}
