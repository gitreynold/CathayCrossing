using UnityEngine;

namespace CathayCrossing.HD2D
{
    // 聊天訊息的路由中心：
    //   送出：ChatInputUI → SendLocalChat()/SendLocalWhisper() → NetworkManager
    //   接收：NetworkManager.HandleMessage("CHAT") → OnChatReceived()
    //         → 頭頂 ChatBubbleUI 泡泡 + ChatHistoryUI 歷史面板
    //
    // 後端廣播含發話者自己（server echo），所以本地泡泡也走接收路徑 ——
    // 你看到自己的泡泡，就代表訊息真的送達伺服器了。
    public class ChatManager : MonoBehaviour
    {
        public static ChatManager Instance { get; private set; }

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

        // 由 ChatInputUI 呼叫：送出本地玩家打的訊息。
        public void SendLocalChat(string text)
        {
            var net = CathayCrossing.Network.NetworkManager.Instance;
            if (net != null && net.IsConnected)
            {
                net.SendChat(text); // 泡泡等 server echo 回來再顯示
            }
            else
            {
                // 離線（後端沒開）也能看到自己的泡泡，方便單機測 UI
                ShowBubbleOn(GameObject.FindGameObjectWithTag("Player"), text, false);
                ChatHistoryUI.Instance?.AddLine("我(離線)", text, false, 0);
            }
        }

        // 由 ChatInputUI 呼叫（V 鍵私訊模式）：送私訊給指定玩家。
        public void SendLocalWhisper(string targetId, string text)
        {
            var net = CathayCrossing.Network.NetworkManager.Instance;
            if (net != null && net.IsConnected) net.SendWhisper(targetId, text);
        }

        // 由 NetworkManager 呼叫：收到伺服器廣播的聊天訊息（含私訊）。
        public void OnChatReceived(string playerId, string message, bool whisper, long timestamp)
        {
            if (string.IsNullOrEmpty(message)) return;

            ChatHistoryUI.Instance?.AddLine(playerId, message, whisper, timestamp);

            var net = CathayCrossing.Network.NetworkManager.Instance;
            GameObject target = net != null ? net.GetPlayerObject(playerId) : null;
            if (target == null)
            {
                // 找不到對應角色（例如對方還沒動過、替身尚未生成）——
                // 歷史面板已記錄，泡泡就略過。
                Debug.Log($"[Chat] {playerId}: {message}（場上找不到此玩家的角色）");
                return;
            }
            ShowBubbleOn(target, message, whisper);
        }

        // 由 NetworkManager 呼叫：INIT / ROOM_JOINED 附帶的歷史訊息，只進面板不冒泡泡。
        public void OnHistoryEntry(string playerId, string message, bool whisper, long timestamp)
        {
            if (string.IsNullOrEmpty(message)) return;
            ChatHistoryUI.Instance?.AddLine(playerId, message, whisper, timestamp);
        }

        // 換房間時清空歷史面板（新房的歷史隨 ROOM_JOINED 重灌）。
        public void ClearHistory()
        {
            ChatHistoryUI.Instance?.Clear();
        }

        static void ShowBubbleOn(GameObject playerObject, string message, bool whisper)
        {
            if (playerObject == null) return;

            // 泡泡跟 CharacterIdDisplay 掛同一個物件，確保跟著角色移動
            var anchor = playerObject.GetComponentInChildren<CharacterIdDisplay>();
            var host = anchor != null ? anchor.gameObject : playerObject;

            var bubble = host.GetComponent<ChatBubbleUI>();
            if (bubble == null) bubble = host.AddComponent<ChatBubbleUI>();
            bubble.ShowMessage(message, whisper);
        }
    }
}
