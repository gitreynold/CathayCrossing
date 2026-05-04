using UnityEngine;

namespace CathayCrossing.Characters
{
    /// <summary>
    /// Snapshot of one character's full visual configuration: which part is
    /// equipped in each slot, plus face/color data. Stored as a SO so designers
    /// can hand-author NPCs in the editor; at runtime player customization is
    /// expected to allocate transient instances (CreateInstance) and mutate.
    ///
    /// Null part references are valid and mean "slot is empty". A character
    /// with no Top equipped will show whatever the base body renders there
    /// (typically an underlayer or skin).
    /// </summary>
    [CreateAssetMenu(menuName = "CathayCrossing/Character/Appearance", fileName = "Appearance_")]
    public class CharacterAppearance : ScriptableObject
    {
        public CharacterPartDefinition hair;
        public CharacterPartDefinition top;
        public CharacterPartDefinition bottom;
        public CharacterPartDefinition shoes;
        public CharacterPartDefinition accessoryHead;
        public CharacterPartDefinition accessoryFace;
        public CharacterPartDefinition accessoryBack;

        public Color hairColor = new Color(0.18f, 0.12f, 0.08f);
        public Color primaryColor = new Color(0.22f, 0.38f, 0.62f);
        public Color secondaryColor = new Color(0.20f, 0.22f, 0.28f);

        public CharacterFaceData face = CharacterFaceData.Default;

        public CharacterPartDefinition Get(CharacterPartSlot slot)
        {
            return slot switch
            {
                CharacterPartSlot.Hair => hair,
                CharacterPartSlot.Top => top,
                CharacterPartSlot.Bottom => bottom,
                CharacterPartSlot.Shoes => shoes,
                CharacterPartSlot.AccessoryHead => accessoryHead,
                CharacterPartSlot.AccessoryFace => accessoryFace,
                CharacterPartSlot.AccessoryBack => accessoryBack,
                _ => null,
            };
        }

        public void Set(CharacterPartSlot slot, CharacterPartDefinition part)
        {
            switch (slot)
            {
                case CharacterPartSlot.Hair: hair = part; break;
                case CharacterPartSlot.Top: top = part; break;
                case CharacterPartSlot.Bottom: bottom = part; break;
                case CharacterPartSlot.Shoes: shoes = part; break;
                case CharacterPartSlot.AccessoryHead: accessoryHead = part; break;
                case CharacterPartSlot.AccessoryFace: accessoryFace = part; break;
                case CharacterPartSlot.AccessoryBack: accessoryBack = part; break;
            }
        }

        public Color GetRoleColor(CharacterColorRole role)
        {
            return role switch
            {
                CharacterColorRole.Hair => hairColor,
                CharacterColorRole.Primary => primaryColor,
                CharacterColorRole.Secondary => secondaryColor,
                _ => Color.white,
            };
        }
    }
}
