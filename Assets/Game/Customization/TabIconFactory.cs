using UnityEngine;

namespace CathayCrossing.Customization
{
    /// <summary>
    /// Builds simple flat silhouette icons (no external art assets) used by the
    /// customise scene's category tabs: a person/head silhouette for the Head
    /// tab and a t-shirt silhouette for the Body tab. Textures are generated
    /// once and cached. White on transparent so they read on both the active
    /// (accent) and inactive (dark) tab backgrounds.
    /// </summary>
    public static class TabIconFactory
    {
        const int S = 128;
        static Texture2D _head, _shirt;

        public static Texture2D Head()  { if (_head  == null) _head  = BuildHead();  return _head; }
        public static Texture2D Shirt() { if (_shirt == null) _shirt = BuildShirt(); return _shirt; }

        static Texture2D NewTex()
        {
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            var px = new Color[S * S];
            var clear = new Color(0f, 0f, 0f, 0f);
            for (int i = 0; i < px.Length; i++) px[i] = clear;
            t.SetPixels(px);
            return t;
        }

        // Filled ellipse in pixel space (cx,cy centre; rx,ry radii), as a
        // fraction of S. Pass a fully transparent colour to carve a hole.
        static void Ellipse(Texture2D t, float cx, float cy, float rx, float ry, Color c)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt((cx - rx) * S));
            int x1 = Mathf.Min(S - 1, Mathf.CeilToInt((cx + rx) * S));
            int y0 = Mathf.Max(0, Mathf.FloorToInt((cy - ry) * S));
            int y1 = Mathf.Min(S - 1, Mathf.CeilToInt((cy + ry) * S));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = ((x + 0.5f) / S - cx) / rx;
                    float dy = ((y + 0.5f) / S - cy) / ry;
                    if (dx * dx + dy * dy <= 1f) t.SetPixel(x, y, c);
                }
        }

        static void Rect(Texture2D t, float xa, float ya, float xb, float yb, Color c)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(xa * S));
            int x1 = Mathf.Min(S - 1, Mathf.CeilToInt(xb * S));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(ya * S));
            int y1 = Mathf.Min(S - 1, Mathf.CeilToInt(yb * S));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    t.SetPixel(x, y, c);
        }

        // A clear, unambiguous HEAD: a face oval with ears and a short neck,
        // with eyes and a mouth carved out so it reads as a head/face rather
        // than an upper body. y = 0 is the bottom of the texture.
        static Texture2D BuildHead()
        {
            var t = NewTex();
            var c = Color.white;
            var hole = new Color(0f, 0f, 0f, 0f);

            // Neck stub at the bottom.
            Rect(t, 0.44f, 0.06f, 0.56f, 0.30f, c);
            // Ears.
            Ellipse(t, 0.215f, 0.56f, 0.07f, 0.11f, c);
            Ellipse(t, 0.785f, 0.56f, 0.07f, 0.11f, c);
            // Head / face oval.
            Ellipse(t, 0.50f, 0.58f, 0.27f, 0.33f, c);

            // Carve facial features so it clearly reads as a head.
            Ellipse(t, 0.40f, 0.62f, 0.045f, 0.075f, hole); // left eye
            Ellipse(t, 0.60f, 0.62f, 0.045f, 0.075f, hole); // right eye
            Rect(t, 0.42f, 0.44f, 0.58f, 0.475f, hole);      // mouth

            t.Apply();
            return t;
        }

        // T-shirt silhouette: wide shoulder/sleeve band over a narrower torso,
        // with a neckline notch carved out of the top centre.
        static Texture2D BuildShirt()
        {
            var t = NewTex();
            var c = Color.white;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float fx = (x + 0.5f) / S;
                    float fy = (y + 0.5f) / S;
                    bool on = false;
                    if (fy >= 0.56f && fy <= 0.74f && fx >= 0.15f && fx <= 0.85f) on = true; // shoulders + sleeves
                    if (fy >= 0.14f && fy < 0.56f && fx >= 0.34f && fx <= 0.66f) on = true;  // torso
                    if (fy >= 0.66f && fx >= 0.43f && fx <= 0.57f) on = false;                // neckline notch
                    if (on) t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }
    }
}
