using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using Gree.UnityWebView;

namespace CathayCrossing.HD2D
{
    // Animal Crossing-style "computer desktop" floating window with an EMBEDDED
    // web page (gree/unity-webview).
    //
    // Flow (driven by OctopathPlayerController):
    //   O while seated  -> ComputerScreenUI.Show(animator) — desktop page with
    //                      a single app icon.
    //   click the icon  -> opens the browser page; a gree WebViewObject is
    //                      overlaid on the WebArea panel and loads LoginUrl.
    //   typing stops    -> watches the Animator's `Typing` bool and closes.
    //
    // On WebGL the webview is an HTML <iframe> drawn over the Unity canvas, so
    // the embedded page must allow framing / be same-origin (set the LoginUrl to
    // a page you control). SetMargins keeps the iframe aligned to the panel and
    // follows the window's zoom.
    [DisallowMultipleComponent]
    public class ComputerScreenUI : MonoBehaviour
    {
        const string LoginUrl = "https://artemis.cubeapps.work";

        static ComputerScreenUI _inst;
        static readonly int TypingHash = Animator.StringToHash("Typing");

        // ── Animal Crossing palette ──────────────────────────────────────────
        static readonly Color Cream      = new Color(1.000f, 0.965f, 0.882f);
        static readonly Color CreamDark  = new Color(0.949f, 0.898f, 0.769f);
        static readonly Color CreamLine  = new Color(0.886f, 0.820f, 0.667f);
        static readonly Color Brown      = new Color(0.475f, 0.404f, 0.302f);
        static readonly Color LeafGreen  = new Color(0.471f, 0.745f, 0.392f);
        static readonly Color LeafDark   = new Color(0.357f, 0.604f, 0.298f);
        static readonly Color White      = new Color(1f, 1f, 1f, 1f);

        Animator _watch;
        GameObject _desktopPage, _browserPage;
        bool _closing;

        // ── Window zoom / live resize ────────────────────────────────────────
        RectTransform _winRt;
        float _zoom = 1f;
        const float ZoomMin = 0.6f, ZoomMax = 1.7f, ZoomStep = 0.12f;
        const float BaseW = 1300f, BaseH = 780f;

        // ── Embedded web view (gree) ─────────────────────────────────────────
        WebViewObject _webView;
        RectTransform _webArea;
        Text _loading;

        static Sprite _rounded;
        static Font _font;

        // ── Public API ───────────────────────────────────────────────────────

        public static bool IsOpen => _inst != null;

        public static void Show(Animator watch)
        {
            if (_inst != null) { _inst._watch = watch; return; }
            var go = new GameObject("ComputerScreenUI");
            _inst = go.AddComponent<ComputerScreenUI>();
            _inst._watch = watch;
            _inst.Build();
        }

        public static void Hide()
        {
            if (_inst != null) _inst.Close();
        }

        // ── Lifecycle ────────────────────────────────────────────────────────

        void Update()
        {
            if (_watch == null || !_watch.GetBool(TypingHash)) { Close(); return; }

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { Close(); return; }

            // Keep the embedded page glued to the (possibly zoomed) panel.
            if (_webView != null && _browserPage != null && _browserPage.activeSelf)
                ApplyWebMargins();
        }

        void Close()
        {
            if (_closing) return;
            _closing = true;
            if (_webView != null) _webView.SetVisibility(false);
            _inst = null;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_inst == this) _inst = null;
        }

        // ── UI construction ──────────────────────────────────────────────────

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            EnsureEventSystem();

            var win = Panel(transform, "Window", new Color(Cream.r, Cream.g, Cream.b, 0.92f),
                            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), rounded: true);
            var winRt = (RectTransform)win.transform;
            winRt.sizeDelta = new Vector2(BaseW, BaseH);
            winRt.anchoredPosition = new Vector2(0f, 15f);
            _winRt = winRt;

            _desktopPage = BuildDesktopPage(win.transform);
            _browserPage = BuildBrowserPage(win.transform);
            _browserPage.SetActive(false);

            var close = Panel(win.transform, "CloseBtn", new Color(0.894f, 0.475f, 0.416f, 0.95f),
                              Vector2.one, Vector2.one, rounded: true);
            var closeRt = (RectTransform)close.transform;
            closeRt.anchoredPosition = new Vector2(-34f, -33f);
            closeRt.sizeDelta = new Vector2(36f, 36f);
            Label(close.transform, "X", "✕", 20, White, TextAnchor.MiddleCenter, FontStyle.Bold)
                .StretchFill();
            var closeBtn = close.AddComponent<Button>();
            closeBtn.targetGraphic = close.GetComponent<Image>();
            closeBtn.onClick.AddListener(Close);

            var zoomOut = MakeZoomBtn(win.transform, "ZoomOutBtn", "−", -75f);
            zoomOut.onClick.AddListener(() => SetZoom(_zoom - ZoomStep));
            var zoomIn = MakeZoomBtn(win.transform, "ZoomInBtn", "＋", -116f);
            zoomIn.onClick.AddListener(() => SetZoom(_zoom + ZoomStep));
        }

        GameObject BuildDesktopPage(Transform parent)
        {
            var page = Panel(parent, "DesktopPage", Color.clear, Vector2.zero, Vector2.one);
            var barColor = new Color(CreamDark.r, CreamDark.g, CreamDark.b, 0.9f);

            for (int i = 0; i < 6; i++)
            {
                var dot = Panel(page.transform, "Dot" + i, new Color(CreamDark.r, CreamDark.g, CreamDark.b, 0.4f),
                                Vector2.zero, Vector2.zero, rounded: true);
                var rt = (RectTransform)dot.transform;
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.anchoredPosition = new Vector2(110f + (i % 3) * 350f + (i / 3) * 90f, 140f + (i / 3) * 260f);
                rt.sizeDelta = new Vector2(80f + (i % 2) * 45f, 80f + (i % 2) * 45f);
            }

            var bar = Panel(page.transform, "TopBar", barColor, new Vector2(0f, 1f), Vector2.one, rounded: true);
            var barRt = (RectTransform)bar.transform;
            barRt.offsetMin = new Vector2(12f, -56f);
            barRt.offsetMax = new Vector2(-12f, -10f);
            Label(bar.transform, "Title", "Artemis OS", 24, Brown, TextAnchor.MiddleCenter, FontStyle.Bold)
                .StretchFill();

            var dock = Panel(page.transform, "Dock", barColor, Vector2.zero, new Vector2(1f, 0f), rounded: true);
            var dockRt = (RectTransform)dock.transform;
            dockRt.offsetMin = new Vector2(260f, 12f);
            dockRt.offsetMax = new Vector2(-260f, 56f);
            Label(dock.transform, "Hint", "點選圖示開啟應用程式", 16, Brown, TextAnchor.MiddleCenter)
                .StretchFill();

            var icon = Panel(page.transform, "ArtemisIcon", LeafGreen, Vector2.zero, Vector2.zero, rounded: true);
            var iconRt = (RectTransform)icon.transform;
            iconRt.anchorMin = iconRt.anchorMax = new Vector2(0f, 1f);
            iconRt.anchoredPosition = new Vector2(120f, -150f);
            iconRt.sizeDelta = new Vector2(100f, 100f);

            var ring = Panel(icon.transform, "Ring", LeafDark, Vector2.zero, Vector2.one, rounded: true);
            ((RectTransform)ring.transform).offsetMin = new Vector2(-4f, -4f);
            ((RectTransform)ring.transform).offsetMax = new Vector2(4f, 4f);
            ring.transform.SetAsFirstSibling();

            var circle = new GameObject("Circle", typeof(Image));
            circle.transform.SetParent(icon.transform, false);
            var cImg = circle.GetComponent<Image>();
            cImg.sprite = RoundedSprite();
            cImg.type = Image.Type.Sliced;
            cImg.color = White;
            var cRt = (RectTransform)circle.transform;
            cRt.anchorMin = cRt.anchorMax = new Vector2(0.5f, 0.5f);
            cRt.sizeDelta = new Vector2(62f, 62f);
            Label(circle.transform, "Glyph", "A", 36, LeafDark, TextAnchor.MiddleCenter, FontStyle.Bold)
                .StretchFill();

            var lbl = Label(page.transform, "IconLabel", "Artemis", 18, Brown, TextAnchor.MiddleCenter, FontStyle.Bold);
            var lblRt = lbl.rectTransform;
            lblRt.anchorMin = lblRt.anchorMax = new Vector2(0f, 1f);
            lblRt.anchoredPosition = new Vector2(120f, -218f);
            lblRt.sizeDelta = new Vector2(180f, 26f);

            var btn = icon.AddComponent<Button>();
            btn.targetGraphic = icon.GetComponent<Image>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.08f * LeafGreen.r, 1.08f * LeafGreen.g, 1.08f * LeafGreen.b);
            colors.pressedColor = LeafDark;
            btn.colors = colors;
            btn.onClick.AddListener(ShowBrowser);

            return page;
        }

        GameObject BuildBrowserPage(Transform parent)
        {
            var page = Panel(parent, "BrowserPage", Color.clear, Vector2.zero, Vector2.one);
            var barColor = new Color(CreamDark.r, CreamDark.g, CreamDark.b, 0.9f);

            var bar = Panel(page.transform, "TopBar", barColor, new Vector2(0f, 1f), Vector2.one, rounded: true);
            var barRt = (RectTransform)bar.transform;
            barRt.offsetMin = new Vector2(12f, -56f);
            barRt.offsetMax = new Vector2(-12f, -10f);
            Label(bar.transform, "Title", "Artemis — Pulse", 20, Brown, TextAnchor.MiddleCenter, FontStyle.Bold)
                .StretchFill();

            var back = Panel(bar.transform, "BackBtn", LeafGreen, Vector2.zero, Vector2.zero, rounded: true);
            var backRt = (RectTransform)back.transform;
            backRt.anchorMin = new Vector2(0f, 0.5f);
            backRt.anchorMax = new Vector2(0f, 0.5f);
            backRt.anchoredPosition = new Vector2(62f, 0f);
            backRt.sizeDelta = new Vector2(100f, 34f);
            Label(back.transform, "Txt", "← 返回", 17, White, TextAnchor.MiddleCenter, FontStyle.Bold)
                .StretchFill();
            var backBtn = back.AddComponent<Button>();
            backBtn.targetGraphic = back.GetComponent<Image>();
            backBtn.onClick.AddListener(ShowDesktop);

            // Always-available fallback (in the bar, NOT under the iframe).
            var ext = Panel(bar.transform, "OpenExtBtn", LeafDark, Vector2.one, Vector2.one, rounded: true);
            var extRt = (RectTransform)ext.transform;
            extRt.anchorMin = new Vector2(0f, 0.5f); extRt.anchorMax = new Vector2(0f, 0.5f);
            extRt.anchoredPosition = new Vector2(196f, 0f); extRt.sizeDelta = new Vector2(146f, 34f);
            Label(ext.transform, "Txt", "用瀏覽器開啟", 15, White, TextAnchor.MiddleCenter, FontStyle.Bold).StretchFill();
            var extBtn = ext.AddComponent<Button>(); extBtn.targetGraphic = ext.GetComponent<Image>();
            extBtn.onClick.AddListener(() => Application.OpenURL(LoginUrl));

            var frame = Panel(page.transform, "WebFrame", new Color(CreamLine.r, CreamLine.g, CreamLine.b, 0.9f),
                              Vector2.zero, Vector2.one, rounded: true);
            var frameRt = (RectTransform)frame.transform;
            frameRt.offsetMin = new Vector2(12f, 12f);
            frameRt.offsetMax = new Vector2(-12f, -64f);

            // WebArea: the rect the embedded web page (iframe) is overlaid onto.
            var inner = Panel(frame.transform, "WebArea", new Color(1f, 1f, 1f, 0.95f), Vector2.zero, Vector2.one, rounded: true);
            var innerRt = (RectTransform)inner.transform;
            innerRt.offsetMin = new Vector2(5f, 5f);
            innerRt.offsetMax = new Vector2(-5f, -5f);
            _webArea = innerRt;

            // Placeholder shown until the web page covers this area.
            _loading = Label(inner.transform, "Loading", "載入中…", 18, Brown, TextAnchor.MiddleCenter);
            _loading.StretchFill();

            return page;
        }

        // ── Page switching + embedded webview ────────────────────────────────

        void ShowBrowser()
        {
            _desktopPage.SetActive(false);
            _browserPage.SetActive(true);
            EnsureWebView();
            if (_webView != null) { _webView.SetVisibility(true); ApplyWebMargins(); }
        }

        void ShowDesktop()
        {
            _browserPage.SetActive(false);
            _desktopPage.SetActive(true);
            if (_webView != null) _webView.SetVisibility(false);
        }

        void EnsureWebView()
        {
            if (_webView != null) return;
            var go = new GameObject("EmbeddedWebView");
            go.transform.SetParent(transform, false);
            _webView = go.AddComponent<WebViewObject>();
            _webView.Init(
                ld: (msg) => { Debug.Log("[ComputerScreenUI] webview loaded: " + msg); if (_loading != null) _loading.gameObject.SetActive(false); },
                err: (msg) => Debug.LogWarning("[ComputerScreenUI] webview error: " + msg),
                httpErr: (msg) => Debug.LogWarning("[ComputerScreenUI] webview http error: " + msg),
                enableWKWebView: true);
            _webView.LoadURL(LoginUrl);
            _webView.SetVisibility(false);
        }

        // Position the native/iframe webview to overlay the WebArea panel.
        void ApplyWebMargins()
        {
            if (_webView == null || _webArea == null) return;
            var c = new Vector3[4];
            _webArea.GetWorldCorners(c); // overlay canvas -> screen pixels
            int left   = Mathf.RoundToInt(c[0].x);
            int bottom = Mathf.RoundToInt(c[0].y);
            int right  = Mathf.RoundToInt(Screen.width  - c[2].x);
            int top    = Mathf.RoundToInt(Screen.height - c[1].y);
            _webView.SetMargins(left, top, right, bottom);
        }

        // ── Zoom ─────────────────────────────────────────────────────────────

        Button MakeZoomBtn(Transform parent, string name, string glyph, float x)
        {
            var go = Panel(parent, name, new Color(LeafGreen.r, LeafGreen.g, LeafGreen.b, 0.95f),
                           Vector2.one, Vector2.one, rounded: true);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = new Vector2(x, -33f);
            rt.sizeDelta = new Vector2(36f, 36f);
            Label(go.transform, "T", glyph, 22, White, TextAnchor.MiddleCenter, FontStyle.Bold).StretchFill();
            var b = go.AddComponent<Button>();
            b.targetGraphic = go.GetComponent<Image>();
            return b;
        }

        void SetZoom(float z)
        {
            _zoom = Mathf.Clamp(z, ZoomMin, ZoomMax);
            if (_winRt != null) _winRt.localScale = new Vector3(_zoom, _zoom, 1f);
            Canvas.ForceUpdateCanvases();
            if (_webView != null && _browserPage != null && _browserPage.activeSelf) ApplyWebMargins();
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(es);
        }

        static GameObject Panel(Transform parent, string name, Color color,
                                Vector2 anchorMin, Vector2 anchorMax, bool rounded = false)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            if (rounded) { img.sprite = RoundedSprite(); img.type = Image.Type.Sliced; }
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return go;
        }

        static Text Label(Transform parent, string name, string text, int size,
                          Color color, TextAnchor anchor, FontStyle style = FontStyle.Normal)
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
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = Mathf.Max(r - x, x - (s - 1 - r), 0f);
                    float dy = Mathf.Max(r - y, y - (s - 1 - r), 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    px[y * s + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels32(px); tex.Apply();
            _rounded = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f),
                                     100f, 0, SpriteMeshType.FullRect, new Vector4(r + 4f, r + 4f, r + 4f, r + 4f));
            return _rounded;
        }
    }

    static class RectTransformExt
    {
        public static void StretchFill(this Text t)
        {
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }
    }
}
