using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace CathayCrossing.HD2D
{
    /// <summary>
    /// Office multifunction printer. Player near + O → Animal-Crossing-style
    /// window with 列印 (print) / 傳真 (fax). Clicking a button closes the window
    /// first, then runs the effect.
    ///
    /// 列印 animation: the scanner lid (root.1) rotates about its OWN lowest edge
    /// — the side that meets the body root.0 (that edge stays fixed) — folding the
    /// raised end down until the lid lies flat/parallel on root.0. The document
    /// feeder (root.3) is parented to root.1 so it rotates together as one unit.
    /// Then a white light flashes (scan light) and it re-opens.
    /// </summary>
    [DisallowMultipleComponent]
    public class PrinterController : MonoBehaviour
    {
        [Header("Interaction")]
        public Key openKey = Key.O;
        public float range = 2.2f;
        public string playerTag = "Player";

        [Header("Parts")]
        public string bodyPart = "root.0";
        [Tooltip("First entry = the hinged lid that rotates; the rest are parented to it and move together.")]
        public string[] lidParts = new[] { "root.1", "root.3" };

        [Header("Print animation")]
        [Tooltip("Degrees to fold the lid down around its rear hinge to lie flat on root.0. Tune in the inspector.")]
        public float closeAngle = 70f;
        public float closeDuration = 0.5f;
        public float flashDuration = 0.35f;
        public float flashIntensity = 12f;
        public float reopenDelay = 1.2f;

        // ── AC palette ──
        static readonly Color Cream     = new Color(1.000f, 0.965f, 0.882f);
        static readonly Color CreamDark = new Color(0.949f, 0.898f, 0.769f);
        static readonly Color Brown     = new Color(0.475f, 0.404f, 0.302f);
        static readonly Color Leaf      = new Color(0.471f, 0.745f, 0.392f);
        static readonly Color Sky       = new Color(0.404f, 0.624f, 0.831f);
        static readonly Color White     = new Color(1f, 1f, 1f, 1f);

        Transform _body;
        Transform _lid;          // the hinged lid (root.1); followers are parented to it
        Vector3 _hinge, _axis;
        float _angle;            // degrees to rotate to lie flat
        GameObject _ui;
        bool _open, _busy;
        Text _toast;

        static Sprite _rounded;
        static Font _font;

        void Start()
        {
            _body = FindPart(bodyPart);
            _lid = (lidParts != null && lidParts.Length > 0) ? FindPart(lidParts[0]) : null;
            // Parent the remaining lid parts (e.g. root.3 feeder) under the lid so they move together.
            if (_lid != null)
                for (int i = 1; i < lidParts.Length; i++)
                {
                    var c = FindPart(lidParts[i]);
                    if (c != null && c != _lid) c.SetParent(_lid, true);
                }

            _axis = transform.right; // hinge line direction (printer's left-right)
            if (_lid != null) ComputeHinge(_lid, out _hinge, out _angle);
        }

        Transform FindPart(string n)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == n) return t;
            return null;
        }

        // Hinge = the lid's lowest edge (the side joined to root.0). Angle = the
        // tilt that must be removed so the lid becomes horizontal (flat on root.0).
        void ComputeHinge(Transform lid, out Vector3 hinge, out float angle)
        {
            hinge = lid.position; angle = 0f;
            var mf = lid.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            var verts = mf.sharedMesh.vertices;
            var l2w = lid.localToWorldMatrix;
            float minY = float.MaxValue, maxY = float.MinValue;
            var ws = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                var w = l2w.MultiplyPoint3x4(verts[i]); ws[i] = w;
                if (w.y < minY) minY = w.y;
                if (w.y > maxY) maxY = w.y;
            }
            float eps = (maxY - minY) * 0.08f;
            Vector3 h = Vector3.zero; int c = 0; Vector3 far = lid.position; float fy = float.MinValue;
            for (int i = 0; i < ws.Length; i++)
            {
                if (ws[i].y < minY + eps) { h += ws[i]; c++; }
                if (ws[i].y > fy) { fy = ws[i].y; far = ws[i]; }
            }
            if (c == 0) return;
            hinge = h / c;

            Vector3 a = _axis.normalized;
            Vector3 v = far - hinge;
            Vector3 vPerp = v - Vector3.Dot(v, a) * a;          // tilt within the plane ⟂ hinge axis
            Vector3 horiz = new Vector3(vPerp.x, 0f, vPerp.z);  // same direction but level
            if (horiz.sqrMagnitude < 1e-6f) return;
            angle = Vector3.SignedAngle(vPerp, horiz, a);       // rotate this much to make the lid flat
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb[openKey].wasPressedThisFrame) return;

            var p = GameObject.FindWithTag(playerTag);
            if (p == null) return;
            Vector3 d = p.transform.position - transform.position; d.y = 0f;
            if (d.sqrMagnitude <= range * range) Toggle();
            else if (_open) CloseUI();
        }

        void Toggle() { if (_open) CloseUI(); else OpenUI(); }
        void OpenUI() { if (_ui == null) BuildUI(); _ui.SetActive(true); _open = true; }
        void CloseUI() { if (_ui != null) _ui.SetActive(false); _open = false; }

        // ── Buttons: close the window first, then run the effect ──

        void OnPrint() { CloseUI(); if (!_busy) StartCoroutine(PrintSequence()); }
        void OnFax()   { CloseUI(); }

        IEnumerator PrintSequence()
        {
            _busy = true;
            _axis = transform.right;
            if (_body != null) { var bb = _body.GetComponent<MeshFilter>().sharedMesh.bounds; _hinge = _body.transform.TransformPoint(new Vector3(bb.center.x, bb.max.y, bb.max.z)); } // hinge = body rear-top edge (the side root.1 joins); lid folds flat onto the platen
            yield return RotateLid(+1f, closeDuration);   // close: lay flat on root.0
            yield return Flash();
            yield return new WaitForSeconds(reopenDelay);
            yield return RotateLid(-1f, closeDuration);     // re-open
            _busy = false;
        }

        IEnumerator RotateLid(float dir, float dur)
        {
            if (_lid == null) yield break;
            float applied = 0f, t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float target = Mathf.Lerp(0f, dir * closeAngle, Mathf.Clamp01(t / dur));
                float step = target - applied; applied = target;
                _lid.RotateAround(_hinge, _axis, step);
                yield return null;
            }
            _lid.RotateAround(_hinge, _axis, dir * closeAngle - applied);
        }

        IEnumerator Flash()
        {
            Vector3 pos;
            if (_body != null) { var b = _body.GetComponent<Renderer>().bounds; pos = new Vector3(b.center.x, b.max.y + 0.05f, b.center.z); }
            else pos = transform.position + Vector3.up;
            var go = new GameObject("PrintFlash");
            go.transform.position = pos;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point; light.color = Color.white; light.range = 4f; light.intensity = 0f;
            float t = 0f;
            while (t < flashDuration)
            {
                t += Time.deltaTime;
                float k = 1f - Mathf.Abs(2f * (t / flashDuration) - 1f);
                light.intensity = k * flashIntensity;
                yield return null;
            }
            Destroy(go);
        }

        // ── AC-style window ──

        void BuildUI()
        {
            _ui = new GameObject("PrinterUI");
            var canvas = _ui.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 510;
            var scaler = _ui.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 0.5f;
            _ui.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();

            var win = Panel(_ui.transform, "Window", new Color(Cream.r, Cream.g, Cream.b, 0.97f),
                            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), true);
            ((RectTransform)win.transform).sizeDelta = new Vector2(560f, 340f);

            var bar = Panel(win.transform, "Bar", new Color(CreamDark.r, CreamDark.g, CreamDark.b, 0.95f),
                            new Vector2(0f, 1f), Vector2.one, true);
            var barRt = (RectTransform)bar.transform;
            barRt.offsetMin = new Vector2(10f, -54f); barRt.offsetMax = new Vector2(-10f, -10f);
            Label(bar.transform, "T", "事務機", 22, Brown, TextAnchor.MiddleCenter, FontStyle.Bold).StretchFill();

            var close = Panel(win.transform, "Close", new Color(0.894f, 0.475f, 0.416f, 0.95f), Vector2.one, Vector2.one, true);
            var cRt = (RectTransform)close.transform; cRt.anchoredPosition = new Vector2(-30f, -28f); cRt.sizeDelta = new Vector2(34f, 34f);
            Label(close.transform, "X", "✕", 18, White, TextAnchor.MiddleCenter, FontStyle.Bold).StretchFill();
            var cb = close.AddComponent<Button>(); cb.targetGraphic = close.GetComponent<Image>(); cb.onClick.AddListener(CloseUI);

            MakeBigButton(win.transform, "列印", Leaf, new Vector2(-130f, 10f), OnPrint);
            MakeBigButton(win.transform, "傳真", Sky,  new Vector2(130f, 10f), OnFax);

            _toast = Label(win.transform, "Toast", "請選擇功能", 16, Brown, TextAnchor.MiddleCenter);
            var tRt = _toast.rectTransform;
            tRt.anchorMin = new Vector2(0.5f, 0f); tRt.anchorMax = new Vector2(0.5f, 0f);
            tRt.anchoredPosition = new Vector2(0f, 34f); tRt.sizeDelta = new Vector2(500f, 30f);
        }

        void MakeBigButton(Transform parent, string text, Color color, Vector2 pos, UnityEngine.Events.UnityAction cb)
        {
            var go = Panel(parent, text + "Btn", color, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), true);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(210f, 150f);
            Label(go.transform, "T", text, 30, White, TextAnchor.MiddleCenter, FontStyle.Bold).StretchFill();
            var b = go.AddComponent<Button>(); b.targetGraphic = go.GetComponent<Image>();
            b.onClick.AddListener(cb);
        }

        void ShowToast(string msg) { if (_toast != null) _toast.text = msg; }

        // ── helpers ──

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(es);
        }

        static GameObject Panel(Transform parent, string name, Color color, Vector2 aMin, Vector2 aMax, bool rounded)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            if (rounded) { img.sprite = RoundedSprite(); img.type = Image.Type.Sliced; }
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return go;
        }

        static Text Label(Transform parent, string name, string text, int size, Color color, TextAnchor anchor, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.text = text; t.font = UiFont(); t.fontSize = size; t.fontStyle = style;
            t.color = color; t.alignment = anchor; t.raycastTarget = false;
            return t;
        }

        static Font UiFont()
        {
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return _font;
        }

        static Sprite RoundedSprite()
        {
            if (_rounded != null) return _rounded;
            const int s = 64; const float r = 22f;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false); tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = Mathf.Max(r - x, x - (s - 1 - r), 0f);
                    float dy = Mathf.Max(r - y, y - (s - 1 - r), 0f);
                    float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    px[y * s + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels32(px); tex.Apply();
            _rounded = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r + 4, r + 4, r + 4, r + 4));
            return _rounded;
        }
    }
}
