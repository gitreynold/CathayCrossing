using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CathayCrossing.HD2D
{
    // 左下角聊天歷史面板（可捲動、Tab 開關、預設顯示）。
    // 資料來源：
    //   • 連線時 INIT / 換房時 ROOM_JOINED 附帶的最近 20 則（後端 SQLite 回填）
    //   • 之後每一則即時 CHAT（含私訊，紫字標示）
    public class ChatHistoryUI : MonoBehaviour
    {
        public static ChatHistoryUI Instance { get; private set; }

        [Tooltip("面板顯示/隱藏的切換鍵。")]
        public Key toggleKey = Key.Tab;
        [Tooltip("最多保留幾行，超過丟掉最舊的。")]
        public int maxLines = 60;

        readonly System.Collections.Generic.List<string> _lines = new System.Collections.Generic.List<string>();

        GameObject _panel;
        TextMeshProUGUI _text;
        ScrollRect _scroll;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (ChatInputUI.IsTyping || ComputerScreenUI.IsOpen) return;

            if (kb[toggleKey].wasPressedThisFrame)
            {
                EnsureUI();
                _panel.SetActive(!_panel.activeSelf);
            }
        }

        // 由 ChatManager 呼叫：新的一行（即時訊息或歷史回填共用）。
        public void AddLine(string playerId, string message, bool whisper, long timestampMs)
        {
            EnsureUI();

            string time = timestampMs > 0
                ? System.DateTimeOffset.FromUnixTimeMilliseconds(timestampMs).ToLocalTime().ToString("HH:mm")
                : System.DateTime.Now.ToString("HH:mm");

            string myId = CathayCrossing.Network.NetworkManager.Instance != null
                ? CathayCrossing.Network.NetworkManager.Instance.MyPlayerId
                : "";
            string nameColor = playerId == myId ? "#FFE08A" : "#9AD1FF";

            string line = whisper
                ? $"<color=#77777788>{time}</color> <color=#C9A0FF>(私訊) {playerId}: {message}</color>"
                : $"<color=#77777788>{time}</color> <color={nameColor}>{playerId}</color>: {message}";

            _lines.Add(line);
            if (_lines.Count > maxLines) _lines.RemoveAt(0);
            _text.text = string.Join("\n", _lines);

            if (_panel.activeInHierarchy) StartCoroutine(ScrollToBottom());
        }

        public void Clear()
        {
            _lines.Clear();
            if (_text != null) _text.text = "";
        }

        IEnumerator ScrollToBottom()
        {
            yield return null; // 等 ContentSizeFitter 算完新高度
            if (_scroll != null) _scroll.verticalNormalizedPosition = 0f;
        }

        // ── UI 組裝（全程式生成，無 prefab）────────────────────────
        void EnsureUI()
        {
            if (_panel != null) return;

            var canvasGo = new GameObject("ChatHistory_Canvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 4999; // 壓在輸入列(5000)下面
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            // 左下角面板（避開輸入列的高度）
            _panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(canvasGo.transform, false);
            var prt = (RectTransform)_panel.transform;
            prt.anchorMin = new Vector2(0f, 0f);
            prt.anchorMax = new Vector2(0f, 0f);
            prt.pivot = new Vector2(0f, 0f);
            prt.anchoredPosition = new Vector2(16f, 150f);
            prt.sizeDelta = new Vector2(460f, 220f);
            var bg = _panel.GetComponent<Image>();
            bg.sprite = ChatBubbleUI.RoundedSprite();
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0f, 0f, 0f, 0.45f);

            // ScrollRect + viewport + content(TMP)
            _scroll = _panel.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 24f;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            viewportGo.transform.SetParent(_panel.transform, false);
            var vrt = (RectTransform)viewportGo.transform;
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = new Vector2(12f, 8f);
            vrt.offsetMax = new Vector2(-12f, -8f);
            // 透明 Image 讓滾輪事件有受體
            viewportGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var crt = (RectTransform)contentGo.transform;
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;

            _text = contentGo.AddComponent<TextMeshProUGUI>();
            _text.fontSize = 22f;
            _text.color = Color.white;
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.textWrappingMode = TextWrappingModes.Normal;
            _text.richText = true;

            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll.viewport = vrt;
            _scroll.content = crt;

            _text.text = string.Join("\n", _lines);
        }
    }
}
