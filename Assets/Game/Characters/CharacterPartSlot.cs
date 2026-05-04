namespace CathayCrossing.Characters
{
    /// <summary>
    /// Customizable slots on a character. The base body (skinned skeleton +
    /// head/hands/skin) is always present and is NOT a slot — it's the
    /// foundation that all slot meshes re-bind onto.
    ///
    /// Adding a new slot here is a breaking change: every CharacterAppearance
    /// asset will need a new field. Prefer Accessory* slots for additive items.
    /// </summary>
    public enum CharacterPartSlot
    {
        Hair = 0,
        Top = 1,
        Bottom = 2,
        Shoes = 3,
        AccessoryHead = 10,
        AccessoryFace = 11,
        AccessoryBack = 12,
    }
}
