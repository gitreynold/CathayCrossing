using System;
using System.Collections.Generic;
using UnityEngine;

namespace CathayCrossing.Characters
{
    /// <summary>
    /// Lookup table that tells the assembler which mesh part(s) inside each
    /// source FBX should be activated for a given slot. Built once in the
    /// editor (CustomizeSceneSetup populates it from the analysis baked
    /// into Partials[]) and then loaded at runtime by
    /// <c>Resources.Load&lt;CharacterPartCatalog&gt;("CharacterPartCatalog")</c>
    /// — both the customise scene and the office spawner share the same
    /// asset so a part mapping change ripples through both flows.
    ///
    /// Part names are the GameObject names visible after FBX import — e.g.
    /// <c>part_0.001</c>. Each character's parts use the same naming scheme
    /// but the same numeric suffix means different body parts across
    /// characters (Jay's part_0 is hair, Style3's part_0 is also hair, but
    /// Default3D's part_0 is also hair — and beyond that the numbers diverge),
    /// which is exactly why we need this mapping table.
    /// </summary>
    [CreateAssetMenu(menuName = "CathayCrossing/Character Part Catalog", fileName = "CharacterPartCatalog")]
    public class CharacterPartCatalog : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("Stable id of the source character (Default3D / Jay / Style3).")]
            public string sourceCharacterId;

            [Tooltip("Friendly label shown in the slot picker.")]
            public string displayName;

            [Tooltip("Which slot this part collection fills.")]
            public CharacterPartSlot slot;

            [Tooltip("GameObject names of the mesh parts inside the source FBX " +
                     "that make up this slot. Multiple entries when the slot " +
                     "is composed of several sub-parts (e.g. Head = face main + " +
                     "ears + nose + eye details).")]
            public string[] partNames;

            [Tooltip("True for variants whose body and pants are baked into a " +
                     "single mesh (Style3 part_5). When the user picks such a " +
                     "variant in Body or Pants, the assembler force-locks the " +
                     "other slot to the same source so the mesh isn't drawn " +
                     "twice and isn't half-replaced.")]
            public bool combinesBodyAndPants;
        }

        [Tooltip("Character id used as the assembly base. Always spawned " +
                 "first; only the slots the player overrides are swapped " +
                 "out.")]
        public string baseCharacterId = "Default3D";

        public Entry[] entries;

        // ─── Lookups ────────────────────────────────────────────────────

        public Entry Find(string characterId, CharacterPartSlot slot)
        {
            if (entries == null) return null;
            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                if (e != null && e.sourceCharacterId == characterId && e.slot == slot) return e;
            }
            return null;
        }

        /// <summary>
        /// Every entry that fills <paramref name="slot"/>, in the order
        /// they appear in the catalog. Useful for populating the slot
        /// picker — each entry contributes one thumbnail button.
        /// </summary>
        public IEnumerable<Entry> Options(CharacterPartSlot slot)
        {
            if (entries == null) yield break;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null && entries[i].slot == slot) yield return entries[i];
            }
        }

        /// <summary>
        /// Legacy accessor kept for source compatibility with older
        /// customise-scene code. In the two-slot world there is no
        /// separate pants slot to lock, so this is always false.
        /// </summary>
        public bool BodyPantsLocked(string characterId) => false;
    }
}
