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

        [Tooltip("The rigged FBX (with Humanoid avatar) to instantiate as the " +
                 "player's visual.")]
        public GameObject body;

        [Tooltip("Per-character AnimatorController built by " +
                 "Tools › CathayCrossing › Setup <Name> Character.")]
        public RuntimeAnimatorController controller;
    }
}
