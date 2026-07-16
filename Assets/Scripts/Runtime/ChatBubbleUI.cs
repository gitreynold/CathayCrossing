using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CathayCrossing.HD2D
{
    // 頭頂聊天泡泡。掛在角色（與 CharacterIdDisplay 同一個物件）上，
    // ChatManager 呼叫 ShowMessage() 顯示文字/emoji，停留數秒後淡出。
    //
    // 跟 CharacterIdDisplay 一樣用 world-space Canvas + 每幀面向鏡頭；
    // 泡泡疊在 ID 標籤（offset y=2.0）上方。UI 全部在程式裡組出來，
    // 不需要 prefab —— 跟本專案其他 runtime UI 的做法一致。
    public class ChatBubbleUI : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("泡泡相對角色原點的位移。ID 標籤在 y=2.0，泡泡再高一點。")]
        public Vector3 offset = new Vector3(0f, 2.45f, 0f);
        [Tooltip("泡泡最大寬度（canvas px，×0.01 世界尺度）。超過會自動換行。")]
        public float maxWidth = 240f;
        public float fontSize = 26f;

        [Header("Timing")]
        [Tooltip("訊息最短停留秒數。")]
        public float minSeconds = 3f;
        [Tooltip("訊息最長停留秒數（長訊息會依字數延長，封頂在這裡）。")]
        public float maxSeconds = 8f;
        public float fadeSeconds = 0.25f;

        [Header("Distance fade")]
        [Tooltip("與本地玩家距離超過這裡開始變淡（公尺）。")]
        public float fadeStartDistance = 10f;
        [Tooltip("超過這個距離完全看不到（SPEC 3.6 的 15m）。")]
        public float hideDistance = 15f;

        const float PadX = 14f;
        const float PadY = 9f;

        static readonly Color PublicBg  = new Color(0f, 0f, 0f, 0.72f);
        static readonly Color WhisperBg = new Color(0.30f, 0.12f, 0.45f, 0.85f); // 私訊紫

        Canvas _canvas;
        RectTransform _canvasRect;
        CanvasGroup _group;
        TextMeshProUGUI _text;
        Image _bg;
        Camera _cam;
        Coroutine _routine;
        float _baseAlpha;          // 淡入淡出主值，距離衰減再乘上去
        Transform _localPlayer;    // 距離衰減的基準點

        static Sprite _rounded;

        // 由 ChatManager 呼叫。新訊息進來時直接取代舊泡泡（最新訊息優先）。
        public void ShowMessage(string message, bool whisper = false)
        {
            if (string.IsNullOrEmpty(message)) return;
            EnsureUI();

            _bg.color = whisper ? WhisperBg : PublicBg;
            if (whisper) message = "(悄悄話) " + message;
            _text.text = message;

            // 依內容計算泡泡尺寸：寬度封頂後自動換行
            float textMax = maxWidth - PadX * 2f;
            Vector2 pref = _text.GetPreferredValues(message, textMax, 0f);
            float w = Mathf.Clamp(pref.x, 40f, textMax) + PadX * 2f;
            float h = pref.y + PadY * 2f;
            _canvasRect.sizeDelta = new Vector2(w, h);

            _canvas.gameObject.SetActive(true);
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(FadeRoutine(message.Length));
        }

        IEnumerator FadeRoutine(int messageLength)
        {
            float hold = Mathf.Clamp(2.5f + messageLength * 0.08f, minSeconds, maxSeconds);

            // 淡入淡出只寫 _baseAlpha；實際 alpha 由 LateUpdate 乘上距離
            // 衰減後套用，兩個效果互不打架。
            for (float t = 0f; t < fadeSeconds; t += Time.deltaTime)
            {
                _baseAlpha = t / fadeSeconds;
                yield return null;
            }
            _baseAlpha = 1f;

            yield return new WaitForSeconds(hold);

            for (float t = 0f; t < fadeSeconds; t += Time.deltaTime)
            {
                _baseAlpha = 1f - t / fadeSeconds;
                yield return null;
            }
            _baseAlpha = 0f;
            _canvas.gameObject.SetActive(false);
            _routine = null;
        }

        // 距離衰減：fadeStartDistance 內全亮，到 hideDistance 線性降到 0。
        float DistanceFactor()
        {
            if (hideDistance <= 0f) return 1f;
            if (_localPlayer == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p == null) return 1f;
                _localPlayer = p.transform;
            }
            Vector3 d = transform.position - _localPlayer.position;
            d.y = 0f;
            float dist = d.magnitude;
            if (dist <= fadeStartDistance) return 1f;
            if (dist >= hideDistance) return 0f;
            return 1f - (dist - fadeStartDistance) / (hideDistance - fadeStartDistance);
        }

        void EnsureUI()
        {
            if (_canvas != null) return;

            var canvasGo = new GameObject("ChatBubble_Canvas");
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            // ID 標籤是 1000，泡泡壓在它上面
            _canvas.sortingOrder = 1001;

            _group = canvasGo.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;

            _canvasRect = canvasGo.GetComponent<RectTransform>();
            _canvasRect.localScale = new Vector3(0.01f, 0.01f, 0.01f);
            _canvasRect.localPosition = offset;
            _canvasRect.sizeDelta = new Vector2(maxWidth, 50f);

            var bgGo = new GameObject("BG", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(canvasGo.transform, false);
            _bg = bgGo.GetComponent<Image>();
            _bg.sprite = RoundedSprite();
            _bg.type = Image.Type.Sliced;
            _bg.color = PublicBg;
            var bgRt = (RectTransform)bgGo.transform;
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(bgGo.transform, false);
            _text = textGo.AddComponent<TextMeshProUGUI>();
            _text.alignment = TextAlignmentOptions.Center;
            _text.fontSize = fontSize;
            _text.color = Color.white;
            _text.textWrappingMode = TextWrappingModes.Normal;
            // 中文靠 TMP Settings 的 fallback（CJKFont）、emoji 靠預設
            // sprite asset（EmojiOne），這裡用預設字型即可。
            var textRt = (RectTransform)textGo.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(PadX, PadY);
            textRt.offsetMax = new Vector2(-PadX, -PadY);

            _canvas.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (_canvas == null || !_canvas.gameObject.activeSelf) return;

            // 泡泡跟著角色、永遠面向鏡頭（同 CharacterIdDisplay 的作法）
            _canvasRect.position = transform.position + offset;

            if (_cam == null) _cam = Camera.main;
            if (_cam != null) _canvasRect.rotation = _cam.transform.rotation;

            // 淡入淡出 × 距離衰減
            _group.alpha = _baseAlpha * DistanceFactor();
        }

        // 程式生成的圓角白色 sprite（9-slice），泡泡背景與輸入列共用。
        // 白色 + Image.color 染色，所以一張就夠。
        public static Sprite RoundedSprite()
        {
            if (_rounded != null) return _rounded;

            const int size = 64;
            const float r = 20f;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(Mathf.Max(r - x, x - (size - 1 - r)), 0f);
                    float dy = Mathf.Max(Mathf.Max(r - y, y - (size - 1 - r)), 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(r - d + 0.5f); // 邊緣 1px 抗鋸齒
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();

            _rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                                     100f, 0, SpriteMeshType.FullRect, new Vector4(24, 24, 24, 24));
            return _rounded;
        }
    }
}
