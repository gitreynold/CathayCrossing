using System;
using UnityEngine;

namespace CathayCrossing.Characters
{
    /// <summary>
    /// Face customization data. BlendShape names refer to the head mesh on the
    /// base body — whatever the artist exposed (e.g. "EyeSize", "NoseHeight",
    /// "JawWidth"). Values are 0–100, mirroring Unity's BlendShape weight.
    /// Names that don't exist on the head mesh are silently skipped, so this
    /// stays robust as the artist adds/removes shapes over time.
    /// </summary>
    [Serializable]
    public struct CharacterFaceData
    {
        public Color skinColor;
        public Color eyeColor;
        public BlendShapeValue[] blendShapes;

        public static CharacterFaceData Default => new CharacterFaceData
        {
            skinColor = new Color(0.94f, 0.78f, 0.65f),
            eyeColor = new Color(0.30f, 0.20f, 0.12f),
            blendShapes = Array.Empty<BlendShapeValue>(),
        };
    }

    [Serializable]
    public struct BlendShapeValue
    {
        public string name;
        [Range(0f, 100f)] public float weight;
    }
}
