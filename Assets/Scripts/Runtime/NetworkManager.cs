using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CathayCrossing.Network
{
    [Serializable]
    public class Vec3 { public float x; public float y; public float z; }

    [Serializable]
    public class NetworkData
    {
        public string type;
        public string id;
        public Vec3 position;
        public float rotation;
        public string action; // 新增：動作名稱 (如 "WAVE", "DANCE")
        public string message; // CHAT 訊息內容 / ERROR 的人話說明
        public string code;    // ERROR 錯誤碼 (如 "RATE_LIMIT")
        public bool whisper;   // CHAT：是否為私訊
        public long timestamp; // CHAT：伺服器蓋章的時間 (ms)
        public string room;    // INIT / ROOM_JOINED：所在房間
        public List<NetworkData> others;
        public List<NetworkData> chatHistory; // INIT / ROOM_JOINED：最近的聊天訊息
    }

    public class NetworkManager : MonoBehaviour
    {
        public static NetworkManager Instance;

        // 遠端玩家的正式外觀工廠。由 NetworkChatBootstrap 注入
        // （RemoteAvatarBuilder.Build，住在 Bootstrap 組件，這裡不能直接
        // 引用）。null 或回傳 null 時退回膠囊替身。
        public static Func<Vector3, GameObject> RemoteAvatarBuilder;

        [Header("設定")]
        public string serverUrl = "ws://localhost:3000";
        public GameObject playerPrefab;
        
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private string _myPlayerId = "連線中...";
        public string MyPlayerId => _myPlayerId;
        public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;
        private Dictionary<string, GameObject> _otherPlayers = new Dictionary<string, GameObject>();
        private Queue<Action> _mainThreadActions = new Queue<Action>();
        private string _lastLog = "等待連線...";
        private string _currentRoom = "OfficeScene"; // 後端的預設房間

        private void Awake()
        {
            if (Instance == null) {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                // 換場景（辦公室 ↔ 遊戲房）時自動切後端聊天房間
                SceneManager.sceneLoaded += OnSceneLoaded;
            } else {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        // 場景載入 = 換房間。舊場景的遠端玩家物件已隨場景銷毀，
        // 後端回 ROOM_JOINED 時會重建新房間的名單。
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            if (!IsConnected || scene.name == _currentRoom) return;
            SendJoinRoom(scene.name);
        }

        private async void Start()
        {
            if (playerPrefab == null) _lastLog = "未指派 Player Prefab，遠端玩家以膠囊替身顯示";
            await Connect();
        }

        private void Update()
        {
            lock (_mainThreadActions)
            {
                while (_mainThreadActions.Count > 0) _mainThreadActions.Dequeue()?.Invoke();
            }
        }

        public async Task Connect()
        {
            _ws = new ClientWebSocket();
            _cts = new CancellationTokenSource();
            try {
                await _ws.ConnectAsync(new Uri(serverUrl), _cts.Token);
                _lastLog = "連線成功！";
                _ = ReceiveLoop();
            } catch (Exception e) { _lastLog = "連線失敗: " + e.Message; }
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[1024 * 8];
            while (_ws != null && _ws.State == WebSocketState.Open)
            {
                try {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    
                    string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    NetworkData data = JsonUtility.FromJson<NetworkData>(json);
                    EnqueueAction(() => HandleMessage(data));
                } catch { 
                    break;
                }
            }
            _lastLog = "連線已中斷";
        }

        private void HandleMessage(NetworkData data)
        {
            if (data.type == "INIT") {
                _myPlayerId = data.id;
                _lastLog = "我的 ID: " + data.id;
                
                // Set ID for local player
                var localPlayer = GameObject.FindGameObjectWithTag("Player");
                if (localPlayer != null)
                {
                    // For local player, finding the ID display anywhere in children is fine
                    var idDisplay = localPlayer.GetComponentInChildren<CathayCrossing.HD2D.CharacterIdDisplay>();
                    if (idDisplay == null) idDisplay = localPlayer.AddComponent<CathayCrossing.HD2D.CharacterIdDisplay>();
                    idDisplay.SetId(_myPlayerId);
                    Debug.Log($"[Network] Applied ID {_myPlayerId} to local player.");
                }
                else
                {
                    Debug.LogWarning("[Network] Local player not found by tag 'Player' during INIT.");
                }

                if (!string.IsNullOrEmpty(data.room)) _currentRoom = data.room;
                if (data.others != null) foreach (var o in data.others) SpawnOtherPlayer(o.id, o.position);
                ReplayChatHistory(data.chatHistory);
            }
            else if (data.type == "ENTER") {
                SpawnOtherPlayer(data.id, data.position);
            }
            else if (data.type == "UPDATE") {
                if (_otherPlayers.ContainsKey(data.id)) {
                    var go = _otherPlayers[data.id];
                    if (go == null) { _otherPlayers.Remove(data.id); return; } // 隨場景銷毀的殘留
                    var controller = go.GetComponentInChildren<CathayCrossing.HD2D.OctopathPlayerController>();
                    if (controller != null) {
                        controller.targetPosition = new Vector3(data.position.x, data.position.y, data.position.z);
                        controller.targetRotationY = data.rotation;

                        // 處理動作觸發
                        if (!string.IsNullOrEmpty(data.action)) {
                            if (data.action == "WAVE") controller.Wave();
                            else if (data.action == "DANCE") controller.Dance();
                            else if (data.action == "SIT") controller.Sit();
                            else if (data.action == "TYPE") controller.StartTyping();
                            else if (data.action == "STAND") controller.StandUp();
                        }
                    } else {
                        // 如果沒找到腳本，直接更新位置當作備案
                        go.transform.position = Vector3.Lerp(go.transform.position, new Vector3(data.position.x, data.position.y, data.position.z), 0.5f);
                    }
                } else if (data.id != _myPlayerId) {
                    SpawnOtherPlayer(data.id, data.position);
                }
            }
            else if (data.type == "LEAVE" && _otherPlayers.ContainsKey(data.id)) {
                Destroy(_otherPlayers[data.id]);
                _otherPlayers.Remove(data.id);
            }
            else if (data.type == "CHAT") {
                // 交給 ChatManager 找到對應角色、顯示頭頂泡泡＋寫進歷史。
                // 廣播含發話者自己（server echo），本地泡泡也走這條路。
                CathayCrossing.HD2D.ChatManager.Instance?.OnChatReceived(
                    data.id, data.message, data.whisper, data.timestamp);
            }
            else if (data.type == "ROOM_JOINED") {
                // 換房成功：清掉舊房的遠端玩家、重建新房名單與聊天歷史
                if (!string.IsNullOrEmpty(data.room)) _currentRoom = data.room;
                foreach (var kv in _otherPlayers) if (kv.Value != null) Destroy(kv.Value);
                _otherPlayers.Clear();
                if (data.others != null) foreach (var o in data.others) SpawnOtherPlayer(o.id, o.position);
                CathayCrossing.HD2D.ChatManager.Instance?.ClearHistory();
                ReplayChatHistory(data.chatHistory);
                _lastLog = "已進入房間: " + _currentRoom;
            }
            else if (data.type == "ERROR") {
                // 伺服器擋下的訊息（RATE_LIMIT / MESSAGE_TOO_LONG…）
                Debug.LogWarning($"[Network] 伺服器回報錯誤 {data.code}: {data.message}");
                _lastLog = $"<color=orange>{data.message}</color>";
                CathayCrossing.HD2D.ChatInputUI.ShowErrorToast(data.message);
            }
        }

        // 把 INIT / ROOM_JOINED 附帶的最近訊息灌進聊天歷史面板
        private void ReplayChatHistory(List<NetworkData> history)
        {
            if (history == null) return;
            var chat = CathayCrossing.HD2D.ChatManager.Instance;
            if (chat == null) return;
            foreach (var entry in history)
                chat.OnHistoryEntry(entry.id, entry.message, entry.whisper, entry.timestamp);
        }

        /// <summary>依玩家 ID 找到場上的角色物件（自己或遠端玩家）。</summary>
        public GameObject GetPlayerObject(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (id == _myPlayerId) return GameObject.FindGameObjectWithTag("Player");
            return _otherPlayers.TryGetValue(id, out var go) ? go : null;
        }

        private void SpawnOtherPlayer(string id, Vec3 pos)
        {
            if (id == _myPlayerId || _otherPlayers.ContainsKey(id)) return;

            Vector3 startPos = (pos != null) ? new Vector3(pos.x, pos.y, pos.z) : new Vector3(0, 0, 0);
            // 外觀優先序：指派的 prefab → RemoteAvatarBuilder（正式角色，
            // 由 NetworkChatBootstrap 注入）→ 膠囊替身（最後保底）。
            GameObject go = null;
            if (playerPrefab != null) go = Instantiate(playerPrefab, startPos, Quaternion.identity);
            else if (RemoteAvatarBuilder != null) go = RemoteAvatarBuilder(startPos);
            if (go == null) go = CreatePlaceholderAvatar(startPos);
            go.name = "RemotePlayer_" + id;
            
            // IMPORTANT: Ensure remote players don't have the Player tag to avoid logic conflicts
            if (go.CompareTag("Player")) go.tag = "Untagged";

            // 嘗試從根目錄或子目錄找腳本
            var script = go.GetComponentInChildren<CathayCrossing.HD2D.OctopathPlayerController>();
            if (script != null) {
                script.isLocalPlayer = false;
                script.targetPosition = startPos;
                script.enabled = true;
                
                // Add ID display to the SAME object as the controller so it moves with it
                var idDisplay = script.gameObject.GetComponent<CathayCrossing.HD2D.CharacterIdDisplay>();
                if (idDisplay == null) idDisplay = script.gameObject.AddComponent<CathayCrossing.HD2D.CharacterIdDisplay>();
                idDisplay.SetId(id);
                
                Debug.Log($"[Network] 成功生成玩家 {id}，位置: {startPos}");
            } else {
                if (playerPrefab != null)
                    Debug.LogWarning($"[Network] 警告：生成的 Prefab '{go.name}' 身上找不到 OctopathPlayerController 腳本！");

                // Fallback: Add to root（膠囊替身也走這裡，位置更新走
                // HandleMessage 的 Lerp 備案路徑）
                var idDisplay = go.AddComponent<CathayCrossing.HD2D.CharacterIdDisplay>();
                idDisplay.SetId(id);
            }
            
            var cc = go.GetComponentInChildren<CharacterController>();
            if (cc != null) cc.enabled = false;

            _otherPlayers.Add(id, go);
            _lastLog = "已生成玩家: " + id;
        }

        // 藍色膠囊 + ID 標籤的簡易替身（開發期用，例如用 wscat 假扮第二
        // 位玩家）。正式的遠端角色外觀之後再接 playerPrefab。
        private GameObject CreatePlaceholderAvatar(Vector3 pos)
        {
            var root = new GameObject();
            root.transform.position = pos;

            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(visual.GetComponent<Collider>()); // 不擋路，位置純顯示
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            visual.transform.localScale = new Vector3(0.42f, 0.9f, 0.42f);

            var renderer = visual.GetComponent<Renderer>();
            if (renderer != null) renderer.material.color = new Color(0.55f, 0.75f, 1f);

            return root;
        }

        public async void SendMove(Vector3 pos, float rot)
        {
            if (_ws?.State != WebSocketState.Open) return;
            try {
                string json = $"{{\"type\":\"MOVE\",\"position\":{{\"x\":{pos.x.ToString("F3")},\"y\":{pos.y.ToString("F3")},\"z\":{pos.z.ToString("F3")}}},\"rotation\":{rot.ToString("F3")}}}";
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
            } catch { }
        }

        public async void SendAction(string actionName)
        {
            if (_ws?.State != WebSocketState.Open) return;
            
            var localPlayer = GameObject.FindGameObjectWithTag("Player");
            Vector3 pos = localPlayer != null ? localPlayer.transform.position : transform.position;
            float rot = localPlayer != null ? localPlayer.transform.eulerAngles.y : transform.eulerAngles.y;

            try {
                string json = $"{{\"type\":\"MOVE\",\"action\":\"{actionName}\",\"position\":{{\"x\":{pos.x.ToString("F3")},\"y\":{pos.y.ToString("F3")},\"z\":{pos.z.ToString("F3")}}},\"rotation\":{rot.ToString("F3")}}}";
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
            } catch { }
        }

        /// <summary>送出聊天訊息。後端會驗證、遮蔽關鍵字後廣播給同房間所有人（含自己）。</summary>
        public async void SendChat(string message)
        {
            if (_ws?.State != WebSocketState.Open) return;
            if (string.IsNullOrEmpty(message)) return;

            try {
                string json = $"{{\"type\":\"CHAT\",\"message\":\"{EscapeJson(message)}\"}}";
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
            } catch { }
        }

        /// <summary>私訊：只有 toId 那位玩家（和自己的 echo）收得到。</summary>
        public async void SendWhisper(string toId, string message)
        {
            if (_ws?.State != WebSocketState.Open) return;
            if (string.IsNullOrEmpty(toId) || string.IsNullOrEmpty(message)) return;

            try {
                string json = $"{{\"type\":\"CHAT\",\"to\":\"{EscapeJson(toId)}\",\"message\":\"{EscapeJson(message)}\"}}";
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
            } catch { }
        }

        /// <summary>切換後端聊天房間（換場景時由 OnSceneLoaded 自動呼叫）。</summary>
        public async void SendJoinRoom(string room)
        {
            if (_ws?.State != WebSocketState.Open) return;
            if (string.IsNullOrEmpty(room)) return;

            try {
                string json = $"{{\"type\":\"JOIN_ROOM\",\"room\":\"{EscapeJson(room)}\"}}";
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
            } catch { }
        }

        /// <summary>回傳距離 from 最近、且在 maxDistance 內的遠端玩家 ID（沒有則 null）。私訊選人用。</summary>
        public string GetNearestOtherPlayerId(Vector3 from, float maxDistance)
        {
            string best = null;
            float bestSqr = maxDistance * maxDistance;
            foreach (var kv in _otherPlayers)
            {
                if (kv.Value == null) continue;
                Vector3 d = kv.Value.transform.position - from;
                d.y = 0f;
                float s = d.sqrMagnitude;
                if (s <= bestSqr) { bestSqr = s; best = kv.Key; }
            }
            return best;
        }

        // 聊天內容是使用者輸入，含引號/反斜線/換行都要跳脫，
        // 不像 SendMove 只送數字可以直接串字串。
        private static string EscapeJson(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private void EnqueueAction(Action a) { lock (_mainThreadActions) _mainThreadActions.Enqueue(a); }

        private void OnGUI()
        {
            GUI.Box(new Rect(10, 10, 250, 100), "網路狀態監測");
            GUI.Label(new Rect(20, 30, 230, 20), "我的 ID: " + _myPlayerId);
            GUI.Label(new Rect(20, 50, 230, 20), "在線人數: " + (_otherPlayers.Count + 1));
            GUI.Label(new Rect(20, 70, 230, 30), "日誌: " + _lastLog);
        }

        private async void OnApplicationQuit()
        {
            if (_ws != null) {
                _cts.Cancel();
                if (_ws.State == WebSocketState.Open) await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye", CancellationToken.None);
            }
        }
    }
}
