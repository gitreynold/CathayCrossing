using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CathayCrossing.HD2D
{
    // 聊天輸入列：
    //   Enter        → 開啟畫面下方輸入框（公開發言）
    //   V（靠近某人）→ 開啟私訊模式，只有對方看得到
    //   再按 Enter   → 送出；Esc → 取消
    //
    // 送出走 ChatManager → NetworkManager → 後端廣播，泡泡等 server echo
    // 回來才顯示（跟其他玩家看到的完全一致）。
    //
    // 其他吃鍵盤的腳本（OctopathPlayerController、AfaRideHorseSummon、
    // DoorOpener）都要檢查 ChatInputUI.IsTyping，打字時不能觸發
    // 移動或 H/F/G/O/R 動作 —— 跟 ComputerScreenUI.IsOpen 同一套模式。
    [DisallowMultipleComponent]
    public class ChatInputUI : MonoBehaviour
    {
        public static ChatInputUI Instance { get; private set; }

        // 全域打字狀態。所有輪詢 Keyboard.current 的腳本都以這個為準。
        public static bool IsTyping => Instance != null && Instance._open;

        // 與後端 CONFIG.chatMaxLength 一致
        const int MaxLength = 200;
        // V 鍵私訊：對方要在這個距離內（公尺）
        const float WhisperRange = 3f;

        static readonly Color PublicBg  = new Color(0f, 0f, 0f, 0.65f);
        static readonly Color WhisperBg = new Color(0.30f, 0.12f, 0.45f, 0.80f);

        bool _open;
        string _whisperTarget; // null = 公開發言
        Canvas _canvas;
        GameObject _panel;
        TMP_InputField _field;
        TextMeshProUGUI _placeholder;
        Image _panelBg;
        TextMeshProUGUI _errorLabel;
        Coroutine _errorRoutine;

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

            bool enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;

            if (!_open)
            {
                // 電腦螢幕 UI 開著時鍵盤屬於內嵌網頁，不搶按鍵
                if (ComputerScreenUI.IsOpen) return;

                if (enter) Open(null);
                else if (kb.vKey.wasPressedThisFrame) TryOpenWhisper();
                return;
            }

            if (kb.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }
            if (enter) Submit();
        }

        // V：找最近的遠端玩家（WhisperRange 內）開私訊；沒人就提示。
        void TryOpenWhisper()
        {
            var net = CathayCrossing.Network.NetworkManager.Instance;
            var me = GameObject.FindGameObjectWithTag("Player");
            if (net == null || !net.IsConnected || me == null)
            {
                ShowError("私訊需要連線，且附近要有其他玩家");
                return;
            }

            string target = net.GetNearestOtherPlayerId(me.transform.position, WhisperRange);
            if (string.IsNullOrEmpty(target))
            {
                ShowError($"附近 {WhisperRange:F0}m 內沒有其他玩家，走近一點再按 V");
                return;
            }
            Open(target);
        }

        void Open(string whisperTarget)
        {
            EnsureUI();
            _open = true;
            _whisperTarget = whisperTarget;

            bool whisper = !string.IsNullOrEmpty(whisperTarget);
            _panelBg.color = whisper ? WhisperBg : PublicBg;
            _placeholder.text = whisper
                ? $"私訊 @{whisperTarget}…（Enter 送出、Esc 取消）"
                : "輸入訊息…（Enter 送出、Esc 取消）";

            _panel.SetActive(true);
            _field.text = "";
            _field.Select();
            _field.ActivateInputField();
        }

        void Submit()
        {
            string text = _field.text.Trim();
            string target = _whisperTarget;
            Close();
            if (text.Length == 0) return;

            if (!string.IsNullOrEmpty(target)) ChatManager.Instance?.SendLocalWhisper(target, text);
            else ChatManager.Instance?.SendLocalChat(text);
        }

        void Close()
        {
            _open = false;
            _whisperTarget = null;
            if (_field != null) _field.DeactivateInputField();
            if (_panel != null) _panel.SetActive(false);
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }

        // ── 錯誤提示（rate limit、訊息過長…）───────────────────────
        // 顯示在輸入列上方的紅字，3 秒自動消失。輸入列關著也會顯示。
        public static void ShowErrorToast(string message)
        {
            Instance?.ShowError(message);
        }

        void ShowError(string message)
        {
            EnsureUI();
            _errorLabel.text = message;
            _errorLabel.gameObject.SetActive(true);
            if (_errorRoutine != null) StopCoroutine(_errorRoutine);
            _errorRoutine = StartCoroutine(HideErrorAfter(3f));
        }

        IEnumerator HideErrorAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            _errorLabel.gameObject.SetActive(false);
            _errorRoutine = null;
        }

        // ── UI 組裝（全程式生成，無 prefab）────────────────────────
        void EnsureUI()
        {
            if (_panel != null) return;
            EnsureEventSystem();

            var canvasGo = new GameObject("ChatInput_Canvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 5000;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            // 底部置中的輸入列
            _panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(canvasGo.transform, false);
            var prt = (RectTransform)_panel.transform;
            prt.anchorMin = new Vector2(0.5f, 0f);
            prt.anchorMax = new Vector2(0.5f, 0f);
            prt.pivot = new Vector2(0.5f, 0f);
            prt.anchoredPosition = new Vector2(0f, 36f);
            prt.sizeDelta = new Vector2(720f, 56f);
            _panelBg = _panel.GetComponent<Image>();
            _panelBg.sprite = ChatBubbleUI.RoundedSprite();
            _panelBg.type = Image.Type.Sliced;
            _panelBg.color = PublicBg;

            // TMP_InputField：viewport + text + placeholder 手動接線
            var fieldGo = new GameObject("Input", typeof(RectTransform));
            fieldGo.transform.SetParent(_panel.transform, false);
            var frt = (RectTransform)fieldGo.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = new Vector2(16f, 8f);
            frt.offsetMax = new Vector2(-16f, -8f);
            _field = fieldGo.AddComponent<TMP_InputField>();

            var areaGo = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            areaGo.transform.SetParent(fieldGo.transform, false);
            var art = (RectTransform)areaGo.transform;
            art.anchorMin = Vector2.zero;
            art.anchorMax = Vector2.one;
            art.offsetMin = Vector2.zero;
            art.offsetMax = Vector2.zero;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(areaGo.transform, false);
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.fontSize = 28f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            var phGo = new GameObject("Placeholder", typeof(RectTransform));
            phGo.transform.SetParent(areaGo.transform, false);
            _placeholder = phGo.AddComponent<TextMeshProUGUI>();
            _placeholder.fontSize = 28f;
            _placeholder.fontStyle = FontStyles.Italic;
            _placeholder.color = new Color(1f, 1f, 1f, 0.4f);
            _placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            _placeholder.textWrappingMode = TextWrappingModes.NoWrap;
            _placeholder.text = "輸入訊息…（Enter 送出、Esc 取消）";
            var phrt = (RectTransform)phGo.transform;
            phrt.anchorMin = Vector2.zero;
            phrt.anchorMax = Vector2.one;
            phrt.offsetMin = Vector2.zero;
            phrt.offsetMax = Vector2.zero;

            _field.textViewport = art;
            _field.textComponent = text;
            _field.placeholder = _placeholder;
            _field.characterLimit = MaxLength;
            _field.lineType = TMP_InputField.LineType.SingleLine;
            _field.onFocusSelectAll = false;

            _panel.SetActive(false);

            // 錯誤提示：輸入列上方的紅字（獨立於 _panel，關著也能顯示）
            var errGo = new GameObject("ErrorToast", typeof(RectTransform));
            errGo.transform.SetParent(canvasGo.transform, false);
            _errorLabel = errGo.AddComponent<TextMeshProUGUI>();
            _errorLabel.fontSize = 24f;
            _errorLabel.color = new Color(1f, 0.35f, 0.3f);
            _errorLabel.alignment = TextAlignmentOptions.Center;
            _errorLabel.textWrappingMode = TextWrappingModes.NoWrap;
            var ert = (RectTransform)errGo.transform;
            ert.anchorMin = new Vector2(0.5f, 0f);
            ert.anchorMax = new Vector2(0.5f, 0f);
            ert.pivot = new Vector2(0.5f, 0f);
            ert.anchoredPosition = new Vector2(0f, 100f);
            ert.sizeDelta = new Vector2(900f, 32f);
            errGo.SetActive(false);
        }

        // 場景可能沒有 EventSystem（OfficeScene 是 runtime 組出來的），
        // 沒有它 TMP_InputField 收不到輸入。同 ComputerScreenUI 的作法。
        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
    }
}
