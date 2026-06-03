using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace CathayCrossing.HD2D
{
    // A small world-space "code generation" screen that mounts on a desk's
    // monitor and types out scrolling, syntax-coloured code lines (mimics the
    // reference gif). Driven by the player pressing T while seated at a desk.
    //
    // Usage (from OctopathPlayerController):
    //   CodeGenScreen.Toggle(deskTransform, playerTransform);
    [DisallowMultipleComponent]
    public class CodeGenScreen : MonoBehaviour
    {
        // ── Placement (relative to the desk's monitor) ───────────────────────
        // Height factor between the desk bounds' centre (0) and top (1) where
        // the screen centre sits, plus a manual world offset for fine-tuning.
        public float screenHeightFactor = 0.78f;
        public float towardPlayer = 0.12f;   // (legacy, unused)
        // Fraction of the desk's half-depth used to push the panel back to the
        // monitor (1 = the far edge). Then nudge slightly toward the player so
        // it floats just in front of the screen face.
        public float monitorBackFactor = 0.40f;
        public float surfaceNudge = 0.001f;
    public float heightScale = 1.35f;
        public Vector3 worldOffset = Vector3.zero;

        // ── Look ─────────────────────────────────────────────────────────────
        public Vector2 panelSizeMeters = new Vector2(0.46f, 0.30f);
        public float sizeScale = 0.96f;           // inset slightly inside the glass
        public int fontSize = 22;                 // canvas-pixel font size
        public float charsPerSecond = 45f;
        public int maxVisibleLines = 8;
        public Color bgColor   = new Color(0.06f, 0.07f, 0.10f, 0.96f);
        public Color frameColor = new Color(0.02f, 0.02f, 0.03f, 1f);

        Transform _faceTarget;
        Text _text;
        readonly List<string> _shown = new List<string>();
        string _current = "";
        int _srcLine, _srcChar;
        float _acc;

        // Reference-style code lines. Each entry is rich-text coloured.
        static readonly string[] Src =
        {
            "<color=#C586C0>function</color> <color=#DCDCAA>addProp</color>(response) {",
            "  <color=#569CD6>for</color> (<color=#569CD6>var</color> i = <color=#B5CEA8>0</color>; i < response.length; i++) {",
            "    <color=#569CD6>var</color> layer = i % <color=#B5CEA8>2</color> === <color=#B5CEA8>0</color>",
            "      ? <color=#9CDCFE>response</color>[i].latitude",
            "      : <color=#9CDCFE>response</color>[i].longitude;",
            "    <color=#9CDCFE>layer</color>.<color=#DCDCAA>addProp</color>(prop);",
            "  }",
            "}",
            "",
            "$(<color=#CE9178>'.select'</color>).<color=#DCDCAA>change</color>(<color=#C586C0>function</color>() {",
            "  species = <color=#569CD6>this</color>.value;",
            "});",
            "",
            "$.<color=#DCDCAA>ajax</color>({",
            "  url: queryURL,",
            "  method: <color=#CE9178>\"GET\"</color>,",
            "  success: <color=#C586C0>function</color>(response) {",
            "    <color=#DCDCAA>renderMap</color>(response);",
            "  }",
            "});",
        };

        public static CodeGenScreen Toggle(Transform desk, Transform faceTarget)
        {
            if (desk == null) return null;
            var existing = desk.GetComponentInChildren<CodeGenScreen>(true);
            if (existing != null)
            {
                bool on = !existing.gameObject.activeSelf;
                existing.gameObject.SetActive(on);
                if (on) { existing._faceTarget = faceTarget; existing.Place(desk, faceTarget); existing.ResetStream(); }
                return existing;
            }
            var go = new GameObject("CodeGenScreen");
            go.transform.SetParent(desk, false);
            var s = go.AddComponent<CodeGenScreen>();
            s._faceTarget = faceTarget;
            s.Build();
            s.Place(desk, faceTarget);
            return s;
        }

        public static void Hide(Transform desk)
        {
            if (desk == null) return;
            var s = desk.GetComponentInChildren<CodeGenScreen>(true);
            if (s != null) s.gameObject.SetActive(false);
        }

        void Build()
        {
            var canvasGO = gameObject;
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            var crt = (RectTransform)canvasGO.transform;
            ApplySize(panelSizeMeters.x, panelSizeMeters.y);

            // Outer frame.
            var frame = NewImage("Frame", crt, frameColor);
            Stretch(frame, -10, -10, 10, 10);

            // Background panel.
            var bg = NewImage("BG", crt, bgColor);
            Stretch(bg, 0, 0, 0, 0);

            // Text.
            var txtGO = new GameObject("Code");
            var trt = txtGO.AddComponent<RectTransform>();
            trt.SetParent(crt, false);
            Stretch(trt, 14, 12, 14, 12);
            _text = txtGO.AddComponent<Text>();
            _text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                         ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            _text.fontSize = fontSize;
            _text.lineSpacing = 1.05f;
            _text.alignment = TextAnchor.UpperLeft;
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.verticalOverflow = VerticalWrapMode.Truncate;
            _text.supportRichText = true;
            _text.color = new Color(0.85f, 0.88f, 0.92f);
            _text.text = "";

            ResetStream();
        }

        // Resize the world-space canvas to the given physical width/height (m),
        // keeping a fixed pixel width so font/layout stay crisp.
        void ApplySize(float widthMeters, float heightMeters)
        {
            var crt = (RectTransform)transform;
            const float pxW = 512f;
            float aspect = (widthMeters > 1e-4f) ? heightMeters / widthMeters : 0.6f;
            float pxH = pxW * aspect;
            crt.sizeDelta = new Vector2(pxW, pxH);
            float scale = widthMeters / pxW;
            crt.localScale = new Vector3(scale, scale, scale);
        }


        Image NewImage(string name, Transform parent, Color c)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = c;
            return img;
        }

        static void Stretch(Graphic g, float l, float b, float r, float t) => Stretch(g.rectTransform, l, b, r, t);
        static void Stretch(RectTransform rt, float l, float b, float r, float t)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
        }

        void ResetStream()
        {
            _shown.Clear(); _current = ""; _srcLine = 0; _srcChar = 0; _acc = 0f;
            if (_text != null) _text.text = "";
        }

        // Position + face the seated player.
public void Place(Transform desk, Transform faceTarget)
        {
            var mf = desk.GetComponentInChildren<MeshFilter>();
            var rend = desk.GetComponentInChildren<Renderer>();
            if (mf == null || mf.sharedMesh == null || rend == null)
            {
                transform.position = desk.position + Vector3.up;
                return;
            }

            Bounds full = rend.bounds;
            float thr = full.center.y + full.size.y * 0.27f; // monitor screen sits up high

            // World-space bounds of the screen panel (top slice of the mesh).
            var verts = mf.sharedMesh.vertices;
            var l2w = mf.transform.localToWorldMatrix;
            Bounds scr = default; bool init = false;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 w = l2w.MultiplyPoint3x4(verts[i]);
                if (w.y <= thr) continue;
                if (!init) { scr = new Bounds(w, Vector3.zero); init = true; }
                else scr.Encapsulate(w);
            }
            if (!init) { transform.position = full.center + Vector3.up * full.size.y; return; }

            Vector3 center = scr.center;
            Vector3 ext = scr.extents;

            // The panel is the thin axis (x or z). The screen faces the player
            // along that thin axis; the other horizontal axis is its width.
            Vector3 normalAxis, widthAxis;
            if (ext.x <= ext.z) { normalAxis = Vector3.right;   widthAxis = Vector3.forward; }
            else                { normalAxis = Vector3.forward; widthAxis = Vector3.right;   }

            // Point the normal toward the player.
            Vector3 toPlayer = (faceTarget != null ? faceTarget.position - center : normalAxis);
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude < 1e-4f) toPlayer = normalAxis;
            toPlayer.Normalize();
            Vector3 normal = normalAxis * Mathf.Sign(Vector3.Dot(normalAxis, toPlayer));
            if (normal.sqrMagnitude < 1e-4f) normal = toPlayer;

            float halfThin  = Mathf.Abs(Vector3.Dot(ext, normalAxis));
            float screenW   = Mathf.Abs(Vector3.Dot(ext, widthAxis)) * 2f * sizeScale;
            float screenH   = ext.y * 2f * heightScale * sizeScale;
            float topY = center.y + ext.y;
            Vector3 anchorPos = center; anchorPos.y = topY - screenH * 0.5f;
            transform.position = anchorPos + normal * (halfThin + surfaceNudge) + worldOffset;
            transform.rotation = Quaternion.LookRotation(normal, Vector3.up);
            ApplySize(Mathf.Max(screenW, 0.05f), Mathf.Max(screenH, 0.04f));
        }

        void Update()
        {
            // Panel stays glued to the monitor (orientation set in Place()).
            if (_text == null) return;
            _acc += charsPerSecond * Time.deltaTime;
            int add = Mathf.FloorToInt(_acc);
            if (add <= 0) return;
            _acc -= add;

            for (int n = 0; n < add; n++) Step();
            Render();
        }

        void Step()
        {
            string line = Src[_srcLine];
            if (_srcChar < line.Length)
            {
                // Advance past a whole <...> tag in one step so partial tags
                // never show.
                if (line[_srcChar] == '<')
                {
                    int close = line.IndexOf('>', _srcChar);
                    _srcChar = (close < 0) ? line.Length : close + 1;
                }
                else _srcChar++;
                _current = line.Substring(0, _srcChar);
            }
            else
            {
                _shown.Add(line);
                while (_shown.Count > maxVisibleLines) _shown.RemoveAt(0);
                _srcLine = (_srcLine + 1) % Src.Length;
                _srcChar = 0;
                _current = "";
                if (_srcLine == 0) _shown.Clear(); // loop: clear and restart
            }
        }

        void Render()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _shown.Count; i++) sb.Append(_shown[i]).Append('\n');
            sb.Append(_current).Append("<color=#5AC8FA>█</color>");
            _text.text = sb.ToString();
        }
    }
}
