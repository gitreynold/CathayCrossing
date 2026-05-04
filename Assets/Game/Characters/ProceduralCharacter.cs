using System.Collections.Generic;
using UnityEngine;

namespace CathayCrossing.Characters
{
    /// <summary>
    /// Placeholder humanoid built from Unity primitives. Exists so the
    /// facial-customization flow can be validated before any real art assets
    /// arrive. Every parameter on this component is intended to map 1:1 onto a
    /// future BlendShape on the real head mesh — when art arrives, the same
    /// slider UI keeps working, only the binding changes from "scale a sphere"
    /// to "set BlendShape weight".
    ///
    /// Anatomical layout (~40 primitives, 1.72 m tall):
    ///   Lower body  : Foot / Ankle / Calf / Knee / Thigh / HipBand
    ///   Torso       : Waist / Chest / Shoulder
    ///   Arms        : Upper / Elbow / Forearm / Wrist / Hand
    ///   Head        : Skull / Ears / Hair (crown + 4 strands)
    ///   Face        : EyeWhite + Iris + Pupil / Brow / NoseBridge + Tip /
    ///                 LipUpper + LipLower (3-segment for curve)
    ///
    /// Built on demand via <see cref="Build"/> so callers can choose when the
    /// scene is ready (e.g. after locating a working URP material to clone).
    /// In Play mode, Inspector edits trigger an automatic <see cref="Refresh"/>.
    /// </summary>
    public class ProceduralCharacter : MonoBehaviour
    {
        // ─── Colours ────────────────────────────────────────────────────────
        [Header("Skin / clothes / hair")]
        public Color skinColor   = new Color(0.94f, 0.78f, 0.65f);
        public Color shirtColor  = new Color(0.22f, 0.38f, 0.62f);
        public Color pantsColor  = new Color(0.20f, 0.22f, 0.28f);
        public Color shoeColor   = new Color(0.08f, 0.08f, 0.10f);
        public Color hairColor   = new Color(0.18f, 0.12f, 0.08f);
        public Color irisColor   = new Color(0.30f, 0.20f, 0.12f);
        public Color lipColor    = new Color(0.62f, 0.36f, 0.36f);

        // ─── Head ───────────────────────────────────────────────────────────
        [Header("Head")]
        [Range(0.85f, 1.15f)] public float headSize = 1f;
        [Range(0.85f, 1.15f)] public float jawWidth = 1f;

        // ─── Eyes ───────────────────────────────────────────────────────────
        [Header("Eyes")]
        [Range(0.6f, 1.6f)]   public float eyeSize    = 1f;
        [Range(0.7f, 1.4f)]   public float eyeSpacing = 1f;
        [Range(-0.02f, 0.02f)] public float eyeY      = 0f;

        // ─── Brows ──────────────────────────────────────────────────────────
        [Header("Brows")]
        [Range(0f, 1.5f)]     public float browThickness = 1f;
        [Range(-30f, 30f)]    public float browAngle     = 0f;   // +ve = inner end higher (angry)
        [Range(-0.01f, 0.02f)] public float browY        = 0f;

        // ─── Nose ───────────────────────────────────────────────────────────
        [Header("Nose")]
        [Range(0.6f, 1.5f)]   public float noseLength = 1f;
        [Range(0.6f, 1.5f)]   public float noseWidth  = 1f;

        // ─── Mouth ──────────────────────────────────────────────────────────
        [Header("Mouth")]
        [Range(0.6f, 1.5f)]   public float mouthWidth = 1f;
        [Range(-1f, 1f)]      public float mouthCurve = 0f;      // -1 frown, +1 smile

        // ─── Material clone source ──────────────────────────────────────────
        [Header("Shader source")]
        [Tooltip("URP material whose shader will be cloned for all parts. " +
                 "Set automatically by the spawner; leave empty otherwise.")]
        public Material baseMaterial;

        // ─── Internal references ────────────────────────────────────────────
        readonly List<(Renderer r, ColorRole role)> _roleRenderers = new();

        // Head + face transforms cached for parameter-driven refresh
        Transform _skull;
        Transform _hairCrown;
        Transform _eyeWhiteL, _eyeWhiteR, _irisL, _irisR, _pupilL, _pupilR;
        Transform _browL, _browR;
        Transform _noseBridge, _noseTip;
        Transform _lipUpperL, _lipUpperC, _lipUpperR, _lipLowerL, _lipLowerC, _lipLowerR;

        bool _built;

        enum ColorRole { Skin, Shirt, Pants, Shoe, Hair, Iris, Lip, EyeWhite, Pupil }

        // Head reference height — every face primitive positions itself relative.
        const float HEAD_Y = 1.62f;
        const float HEAD_FRONT_Z = 0.100f;        // surface of head sphere along +Z

        // ─── Public API ─────────────────────────────────────────────────────
        public void Build(Material baseMat)
        {
            if (_built) return;
            if (baseMat != null) baseMaterial = baseMat;

            BuildLegs();
            BuildHips();
            BuildTorso();
            BuildArms();
            BuildNeckAndHead();
            BuildEars();
            BuildHair();
            BuildFace();

            _built = true;
            Refresh();
        }

        public void Refresh()
        {
            if (!_built) return;

            // Head proportions — width tapers slightly with jawWidth (jaw lower half).
            _skull.localScale = new Vector3(0.21f * headSize,
                                            0.24f * headSize,
                                            0.22f * headSize);
            _hairCrown.localScale = new Vector3(0.225f * headSize,
                                                0.13f  * headSize,
                                                0.225f * headSize);

            // Eyes — three-layer (white / iris / pupil), recessed slightly into head.
            float ex = 0.04f * eyeSpacing;
            float ey = HEAD_Y + 0.035f + eyeY;
            float ez = HEAD_FRONT_Z;
            float whiteS = 0.032f * eyeSize;
            float irisS  = 0.020f * eyeSize;
            float pupilS = 0.010f * eyeSize;
            SetT(_eyeWhiteL, new Vector3(-ex, ey, ez),         whiteS, whiteS, whiteS);
            SetT(_eyeWhiteR, new Vector3( ex, ey, ez),         whiteS, whiteS, whiteS);
            SetT(_irisL,     new Vector3(-ex, ey, ez + 0.010f), irisS,  irisS,  irisS);
            SetT(_irisR,     new Vector3( ex, ey, ez + 0.010f), irisS,  irisS,  irisS);
            SetT(_pupilL,    new Vector3(-ex, ey, ez + 0.014f), pupilS, pupilS, pupilS);
            SetT(_pupilR,    new Vector3( ex, ey, ez + 0.014f), pupilS, pupilS, pupilS);

            // Brows — outer pivot, rotate so the inner end follows browAngle.
            float browBaseY = HEAD_Y + 0.075f + browY;
            float browLen   = 0.05f;
            float browH     = 0.010f * browThickness;
            float browZ     = HEAD_FRONT_Z + 0.010f;
            SetT(_browL, new Vector3(-ex, browBaseY, browZ), browLen, browH, 0.012f);
            SetT(_browR, new Vector3( ex, browBaseY, browZ), browLen, browH, 0.012f);
            _browL.localRotation = Quaternion.Euler(0f, 0f,  browAngle);
            _browR.localRotation = Quaternion.Euler(0f, 0f, -browAngle);

            // Nose — bridge capsule angled forward, tip sphere at the bottom.
            float bridgeY = HEAD_Y + 0.005f;
            float bridgeLen = 0.040f * noseLength;
            float bridgeW   = 0.018f * noseWidth;
            SetT(_noseBridge, new Vector3(0f, bridgeY, HEAD_FRONT_Z + 0.013f),
                 bridgeW, bridgeLen, bridgeW);
            // Tip sits at the bottom of the bridge, slightly more forward.
            SetT(_noseTip, new Vector3(0f, bridgeY - bridgeLen * 0.95f,
                                       HEAD_FRONT_Z + 0.018f),
                 0.022f * noseWidth, 0.018f * noseWidth, 0.022f * noseLength);

            // Lips — upper and lower, each split into 3 segments so the corners
            // can lift / drop with mouthCurve to form a smile / frown.
            float mouthY    = HEAD_Y - 0.072f;
            float mouthZ    = HEAD_FRONT_Z + 0.005f;
            float halfW     = 0.025f * mouthWidth;
            float corner    = mouthCurve * 0.012f;
            float segW      = 0.020f * mouthWidth;
            // Upper lip — thinner.
            SetT(_lipUpperL, new Vector3(-halfW, mouthY + corner + 0.005f, mouthZ),
                 segW, 0.0070f, 0.012f);
            SetT(_lipUpperC, new Vector3(   0f, mouthY            + 0.005f, mouthZ),
                 segW, 0.0080f, 0.012f);
            SetT(_lipUpperR, new Vector3( halfW, mouthY + corner + 0.005f, mouthZ),
                 segW, 0.0070f, 0.012f);
            // Lower lip — thicker.
            SetT(_lipLowerL, new Vector3(-halfW, mouthY + corner - 0.005f, mouthZ),
                 segW, 0.0095f, 0.014f);
            SetT(_lipLowerC, new Vector3(   0f, mouthY            - 0.005f, mouthZ),
                 segW, 0.0110f, 0.014f);
            SetT(_lipLowerR, new Vector3( halfW, mouthY + corner - 0.005f, mouthZ),
                 segW, 0.0095f, 0.014f);

            // Colours
            ApplyAllColors();
        }

        void OnValidate()
        {
            if (_built && Application.isPlaying) Refresh();
        }

        [ContextMenu("Refresh")]
        void RefreshFromMenu() => Refresh();

        // ─── Body builders ─────────────────────────────────────────────────
        void BuildLegs()
        {
            // Foot — flat cube longer in Z, slightly forward.
            MakePart("Foot_L",  PrimitiveType.Cube,
                new Vector3(-0.085f, 0.025f, 0.04f), new Vector3(0.10f, 0.05f, 0.26f), ColorRole.Shoe);
            MakePart("Foot_R",  PrimitiveType.Cube,
                new Vector3( 0.085f, 0.025f, 0.04f), new Vector3(0.10f, 0.05f, 0.26f), ColorRole.Shoe);

            // Ankle joint
            MakePart("Ankle_L", PrimitiveType.Sphere,
                new Vector3(-0.085f, 0.07f, 0f), new Vector3(0.085f, 0.085f, 0.085f), ColorRole.Pants);
            MakePart("Ankle_R", PrimitiveType.Sphere,
                new Vector3( 0.085f, 0.07f, 0f), new Vector3(0.085f, 0.085f, 0.085f), ColorRole.Pants);

            // Calf — capsule
            MakePart("Calf_L",  PrimitiveType.Capsule,
                new Vector3(-0.085f, 0.27f, 0f), new Vector3(0.105f, 0.20f, 0.105f), ColorRole.Pants);
            MakePart("Calf_R",  PrimitiveType.Capsule,
                new Vector3( 0.085f, 0.27f, 0f), new Vector3(0.105f, 0.20f, 0.105f), ColorRole.Pants);

            // Knee
            MakePart("Knee_L",  PrimitiveType.Sphere,
                new Vector3(-0.090f, 0.47f, 0.005f), new Vector3(0.10f, 0.10f, 0.10f), ColorRole.Pants);
            MakePart("Knee_R",  PrimitiveType.Sphere,
                new Vector3( 0.090f, 0.47f, 0.005f), new Vector3(0.10f, 0.10f, 0.10f), ColorRole.Pants);

            // Thigh — slightly thicker than calf
            MakePart("Thigh_L", PrimitiveType.Capsule,
                new Vector3(-0.090f, 0.66f, 0f), new Vector3(0.115f, 0.18f, 0.115f), ColorRole.Pants);
            MakePart("Thigh_R", PrimitiveType.Capsule,
                new Vector3( 0.090f, 0.66f, 0f), new Vector3(0.115f, 0.18f, 0.115f), ColorRole.Pants);
        }

        void BuildHips()
        {
            // Wider hip band joining thighs to waist.
            MakePart("Hips", PrimitiveType.Cylinder,
                new Vector3(0f, 0.92f, 0f), new Vector3(0.32f, 0.06f, 0.22f), ColorRole.Pants);
        }

        void BuildTorso()
        {
            // Three-segment torso: waist (narrow) → chest (wide). Cylinders give
            // a smoother silhouette than the previous monolithic cube.
            MakePart("Waist", PrimitiveType.Cylinder,
                new Vector3(0f, 1.04f, 0f), new Vector3(0.28f, 0.06f, 0.20f), ColorRole.Shirt);
            MakePart("Chest", PrimitiveType.Cylinder,
                new Vector3(0f, 1.22f, 0f), new Vector3(0.36f, 0.12f, 0.24f), ColorRole.Shirt);
            // Pectoral block fills the gap between Chest cylinder top and shoulders.
            MakePart("ChestTop", PrimitiveType.Cylinder,
                new Vector3(0f, 1.36f, 0f), new Vector3(0.40f, 0.04f, 0.24f), ColorRole.Shirt);
        }

        void BuildArms()
        {
            // Shoulder caps — spheres at the deltoid.
            MakePart("Shoulder_L", PrimitiveType.Sphere,
                new Vector3(-0.215f, 1.39f, 0f), new Vector3(0.130f, 0.130f, 0.130f), ColorRole.Shirt);
            MakePart("Shoulder_R", PrimitiveType.Sphere,
                new Vector3( 0.215f, 1.39f, 0f), new Vector3(0.130f, 0.130f, 0.130f), ColorRole.Shirt);

            // Upper arm
            MakePart("UpperArm_L", PrimitiveType.Capsule,
                new Vector3(-0.230f, 1.21f, 0f), new Vector3(0.085f, 0.140f, 0.085f), ColorRole.Shirt);
            MakePart("UpperArm_R", PrimitiveType.Capsule,
                new Vector3( 0.230f, 1.21f, 0f), new Vector3(0.085f, 0.140f, 0.085f), ColorRole.Shirt);

            // Elbow
            MakePart("Elbow_L", PrimitiveType.Sphere,
                new Vector3(-0.235f, 1.04f, 0f), new Vector3(0.080f, 0.080f, 0.080f), ColorRole.Shirt);
            MakePart("Elbow_R", PrimitiveType.Sphere,
                new Vector3( 0.235f, 1.04f, 0f), new Vector3(0.080f, 0.080f, 0.080f), ColorRole.Shirt);

            // Forearm
            MakePart("Forearm_L", PrimitiveType.Capsule,
                new Vector3(-0.240f, 0.91f, 0f), new Vector3(0.075f, 0.115f, 0.075f), ColorRole.Shirt);
            MakePart("Forearm_R", PrimitiveType.Capsule,
                new Vector3( 0.240f, 0.91f, 0f), new Vector3(0.075f, 0.115f, 0.075f), ColorRole.Shirt);

            // Wrist
            MakePart("Wrist_L", PrimitiveType.Sphere,
                new Vector3(-0.245f, 0.78f, 0f), new Vector3(0.060f, 0.060f, 0.060f), ColorRole.Skin);
            MakePart("Wrist_R", PrimitiveType.Sphere,
                new Vector3( 0.245f, 0.78f, 0f), new Vector3(0.060f, 0.060f, 0.060f), ColorRole.Skin);

            // Hand — small flattened capsule, hanging straight down.
            MakePart("Hand_L", PrimitiveType.Capsule,
                new Vector3(-0.245f, 0.71f, 0.01f), new Vector3(0.075f, 0.075f, 0.045f), ColorRole.Skin);
            MakePart("Hand_R", PrimitiveType.Capsule,
                new Vector3( 0.245f, 0.71f, 0.01f), new Vector3(0.075f, 0.075f, 0.045f), ColorRole.Skin);
        }

        void BuildNeckAndHead()
        {
            MakePart("Neck", PrimitiveType.Cylinder,
                new Vector3(0f, 1.46f, 0f), new Vector3(0.090f, 0.05f, 0.090f), ColorRole.Skin);

            _skull = MakePart("Skull", PrimitiveType.Sphere,
                new Vector3(0f, HEAD_Y, 0.005f), new Vector3(0.21f, 0.24f, 0.22f),
                ColorRole.Skin).transform;
        }

        void BuildEars()
        {
            // Flattened ellipsoids on the side of the head.
            MakePart("Ear_L", PrimitiveType.Sphere,
                new Vector3(-0.110f, HEAD_Y, 0.005f), new Vector3(0.024f, 0.052f, 0.018f), ColorRole.Skin);
            MakePart("Ear_R", PrimitiveType.Sphere,
                new Vector3( 0.110f, HEAD_Y, 0.005f), new Vector3(0.024f, 0.052f, 0.018f), ColorRole.Skin);
        }

        void BuildHair()
        {
            // Crown cap — slightly flattened sphere covering the top half of the skull.
            _hairCrown = MakePart("Hair_Crown", PrimitiveType.Sphere,
                new Vector3(0f, HEAD_Y + 0.045f, -0.010f), new Vector3(0.225f, 0.13f, 0.225f),
                ColorRole.Hair).transform;

            // Bangs — three small capsules forming a fringe.
            MakePart("Hair_BangL", PrimitiveType.Capsule,
                new Vector3(-0.060f, HEAD_Y + 0.075f, HEAD_FRONT_Z - 0.005f),
                new Vector3(0.045f, 0.040f, 0.045f), ColorRole.Hair);
            MakePart("Hair_BangC", PrimitiveType.Capsule,
                new Vector3(0f, HEAD_Y + 0.080f, HEAD_FRONT_Z - 0.002f),
                new Vector3(0.050f, 0.045f, 0.050f), ColorRole.Hair);
            MakePart("Hair_BangR", PrimitiveType.Capsule,
                new Vector3( 0.060f, HEAD_Y + 0.075f, HEAD_FRONT_Z - 0.005f),
                new Vector3(0.045f, 0.040f, 0.045f), ColorRole.Hair);

            // Side hair / temple
            MakePart("Hair_SideL", PrimitiveType.Capsule,
                new Vector3(-0.105f, HEAD_Y + 0.030f, -0.005f),
                new Vector3(0.048f, 0.075f, 0.060f), ColorRole.Hair);
            MakePart("Hair_SideR", PrimitiveType.Capsule,
                new Vector3( 0.105f, HEAD_Y + 0.030f, -0.005f),
                new Vector3(0.048f, 0.075f, 0.060f), ColorRole.Hair);

            // Back of head
            MakePart("Hair_Back", PrimitiveType.Sphere,
                new Vector3(0f, HEAD_Y + 0.020f, -0.085f),
                new Vector3(0.215f, 0.150f, 0.110f), ColorRole.Hair);
        }

        void BuildFace()
        {
            _eyeWhiteL = MakeFace("EyeWhite_L", PrimitiveType.Sphere, ColorRole.EyeWhite).transform;
            _eyeWhiteR = MakeFace("EyeWhite_R", PrimitiveType.Sphere, ColorRole.EyeWhite).transform;
            _irisL     = MakeFace("Iris_L",     PrimitiveType.Sphere, ColorRole.Iris).transform;
            _irisR     = MakeFace("Iris_R",     PrimitiveType.Sphere, ColorRole.Iris).transform;
            _pupilL    = MakeFace("Pupil_L",    PrimitiveType.Sphere, ColorRole.Pupil).transform;
            _pupilR    = MakeFace("Pupil_R",    PrimitiveType.Sphere, ColorRole.Pupil).transform;
            _browL     = MakeFace("Brow_L",     PrimitiveType.Cube,   ColorRole.Hair).transform;
            _browR     = MakeFace("Brow_R",     PrimitiveType.Cube,   ColorRole.Hair).transform;
            _noseBridge = MakeFace("NoseBridge", PrimitiveType.Capsule, ColorRole.Skin).transform;
            _noseTip    = MakeFace("NoseTip",    PrimitiveType.Sphere,  ColorRole.Skin).transform;
            _lipUpperL  = MakeFace("LipUpperL",  PrimitiveType.Capsule, ColorRole.Lip).transform;
            _lipUpperC  = MakeFace("LipUpperC",  PrimitiveType.Capsule, ColorRole.Lip).transform;
            _lipUpperR  = MakeFace("LipUpperR",  PrimitiveType.Capsule, ColorRole.Lip).transform;
            _lipLowerL  = MakeFace("LipLowerL",  PrimitiveType.Capsule, ColorRole.Lip).transform;
            _lipLowerC  = MakeFace("LipLowerC",  PrimitiveType.Capsule, ColorRole.Lip).transform;
            _lipLowerR  = MakeFace("LipLowerR",  PrimitiveType.Capsule, ColorRole.Lip).transform;
        }

        // ─── Helpers ────────────────────────────────────────────────────────
        GameObject MakePart(string name, PrimitiveType prim, Vector3 localPos,
                            Vector3 localScale, ColorRole role)
        {
            var go = GameObject.CreatePrimitive(prim);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                col.enabled = false;
                if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
            }
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale    = localScale;
            var r = go.GetComponent<MeshRenderer>();
            ApplyMaterial(r, role);
            _roleRenderers.Add((r, role));
            return go;
        }

        GameObject MakeFace(string name, PrimitiveType prim, ColorRole role)
        {
            // Position/scale set in Refresh — face primitives are parameter-driven.
            return MakePart(name, prim, Vector3.zero, Vector3.one, role);
        }

        static void SetT(Transform t, Vector3 pos, float sx, float sy, float sz)
        {
            if (t == null) return;
            t.localPosition = pos;
            t.localScale    = new Vector3(sx, sy, sz);
        }

        void ApplyMaterial(Renderer renderer, ColorRole role)
        {
            if (renderer == null) return;
            Material mat = baseMaterial != null
                ? new Material(baseMaterial)
                : MakeFallbackMaterial();
            ApplyColor(mat, ColorFor(role));
            ApplySurface(mat, role);
            ZeroTextures(mat);
            renderer.sharedMaterial = mat;
        }

        void ApplyAllColors()
        {
            foreach (var (r, role) in _roleRenderers)
            {
                if (r == null || r.sharedMaterial == null) continue;
                ApplyColor(r.sharedMaterial, ColorFor(role));
                ApplySurface(r.sharedMaterial, role);
            }
        }

        Color ColorFor(ColorRole role)
        {
            return role switch
            {
                ColorRole.Skin     => skinColor,
                ColorRole.Shirt    => shirtColor,
                ColorRole.Pants    => pantsColor,
                ColorRole.Shoe     => shoeColor,
                ColorRole.Hair     => hairColor,
                ColorRole.Iris     => irisColor,
                ColorRole.Lip      => lipColor,
                ColorRole.EyeWhite => Color.white,
                ColorRole.Pupil    => Color.black,
                _ => Color.magenta,
            };
        }

        // Per-role surface tuning (smoothness / metallic) so skin and fabric
        // are matte but eyes stay glossy. URP/Lit: _Smoothness, _Metallic.
        static void ApplySurface(Material mat, ColorRole role)
        {
            float smoothness = role switch
            {
                ColorRole.EyeWhite => 0.85f,
                ColorRole.Iris     => 0.85f,
                ColorRole.Pupil    => 0.85f,
                ColorRole.Lip      => 0.55f,
                ColorRole.Shoe     => 0.35f,
                ColorRole.Skin     => 0.18f,
                ColorRole.Hair     => 0.30f,
                ColorRole.Shirt    => 0.05f,
                ColorRole.Pants    => 0.05f,
                _                  => 0.20f,
            };
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic",   0f);
        }

        static void ApplyColor(Material mat, Color color)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     color);
            mat.color = color;
        }

        static void ZeroTextures(Material mat)
        {
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", null);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", null);
        }

        static Material MakeFallbackMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Standard");
            return shader != null ? new Material(shader) : null;
        }
    }
}
