namespace CathayCrossing.Characters
{
    /// <summary>
    /// Two-slot LEGO breakdown used by the customise scene + spawner.
    /// Matches the tab order in the right rail of CustomizeScene.unity.
    ///
    /// We collapsed down from five slots to two because every fine-grain
    /// split (body / pants / shoes / hands) exposed at least one variant
    /// where the source FBX baked those regions into a single mesh
    /// (Style3 has body + pants in one mesh) or the inter-region skinning
    /// stretched across the rig (Jay's pants drifted off Default3D's
    /// hips). Picking whole-body identities sidesteps every one of those
    /// cases — within a single variant the mesh stays internally
    /// consistent and we only need to worry about the head/body
    /// interface at the neck.
    /// </summary>
    public enum CharacterPartSlot
    {
        Head = 0,   // hair + face + ears + nose + eye details
        Body = 1,   // everything from the neck down — torso, arms, hands,
                    // hips, legs, feet, shoes — in one swappable group.
    }
}
