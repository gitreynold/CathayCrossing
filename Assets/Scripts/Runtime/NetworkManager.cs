using UnityEngine;
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
        public List<NetworkData> others;
    }

    public class NetworkManager : MonoBehaviour
    {
        public static NetworkManager Instance;

        [Header("設定")]
        public string serverUrl = "ws://localhost:3000";
        public GameObject playerPrefab; 
        
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private string _myPlayerId = "連線中...";
        private Dictionary<string, GameObject> _otherPlayers = new Dictionary<string, GameObject>();
        private Queue<Action> _mainThreadActions = new Queue<Action>();
        private string _lastLog = "等待連線...";

        private void Awake()
        {
            if (Instance == null) {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            } else {
                Destroy(gameObject);
            }
        }

        private async void Start()
        {
            if (playerPrefab == null) _lastLog = "<color=red>錯誤：未指派 Player Prefab！</color>";
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
                if (data.others != null) foreach (var o in data.others) SpawnOtherPlayer(o.id, o.position);
            }
            else if (data.type == "ENTER") {
                SpawnOtherPlayer(data.id, data.position);
            }
            else if (data.type == "UPDATE") {
                if (_otherPlayers.ContainsKey(data.id)) {
                    var go = _otherPlayers[data.id];
                    var controller = go.GetComponentInChildren<CathayCrossing.HD2D.OctopathPlayerController>();
                    if (controller != null) {
                        controller.targetPosition = new Vector3(data.position.x, data.position.y, data.position.z);
                        controller.targetRotationY = data.rotation;

                        // 處理動作觸發
                        if (!string.IsNullOrEmpty(data.action)) {
                            if (data.action == "WAVE") controller.Wave();
                            else if (data.action == "DANCE") controller.Dance();
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
        }

        private void SpawnOtherPlayer(string id, Vec3 pos)
        {
            if (id == _myPlayerId || _otherPlayers.ContainsKey(id)) return;
            if (playerPrefab == null) { _lastLog = "<color=red>生成失敗：未指派 Prefab</color>"; return; }

            Vector3 startPos = (pos != null) ? new Vector3(pos.x, pos.y, pos.z) : new Vector3(0, 0, 0);
            GameObject go = Instantiate(playerPrefab, startPos, Quaternion.identity);
            go.name = "RemotePlayer_" + id;

            // 嘗試從根目錄或子目錄找腳本
            var script = go.GetComponentInChildren<CathayCrossing.HD2D.OctopathPlayerController>();
            if (script != null) {
                script.isLocalPlayer = false;
                script.targetPosition = startPos;
                script.enabled = true;
                Debug.Log($"[Network] 成功生成玩家 {id}，位置: {startPos}");
            } else {
                Debug.LogWarning($"[Network] 警告：生成的 Prefab '{go.name}' 身上找不到 OctopathPlayerController 腳本！");
            }
            
            var cc = go.GetComponentInChildren<CharacterController>();
            if (cc != null) cc.enabled = false;

            _otherPlayers.Add(id, go);
            _lastLog = "已生成玩家: " + id;
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
            try {
                string json = $"{{\"type\":\"MOVE\",\"action\":\"{actionName}\",\"position\":{{\"x\":{transform.position.x.ToString("F3")},\"y\":{transform.position.y.ToString("F3")},\"z\":{transform.position.z.ToString("F3")}}},\"rotation\":{transform.eulerAngles.y.ToString("F3")}}}";
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
            } catch { }
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
