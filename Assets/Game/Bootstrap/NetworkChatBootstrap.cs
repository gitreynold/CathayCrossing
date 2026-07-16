using UnityEngine;
using UnityEngine.SceneManagement;

namespace CathayCrossing.Bootstrap
{
    /// <summary>
    /// 進到辦公室場景時，建立多人連線 + 聊天系統：
    /// NetworkManager（WebSocket 同步）+ ChatManager（訊息路由）+
    /// ChatInputUI（Enter 輸入框）。掛在 __GameInfra 上，只生一次，
    /// 之後 DontDestroyOnLoad 跨場景存活。
    ///
    /// 刻意只在 OfficeScene 觸發（不在 CharacterSelect / Customize 連線），
    /// 後端沒開也無妨 —— NetworkManager 會記「連線失敗」，聊天泡泡則退回
    /// 單機模式（只顯示自己的）。
    /// </summary>
    public class NetworkChatBootstrap : MonoBehaviour
    {
        [Tooltip("進入哪個場景時啟動連線與聊天。")]
        public string officeSceneName = "OfficeScene";

        bool _spawned;

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;

            var existing = SceneManager.GetSceneByName(officeSceneName);
            if (existing.IsValid() && existing.isLoaded) SpawnOnce();
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != officeSceneName) return;
            SpawnOnce();
        }

        void SpawnOnce()
        {
            if (_spawned) return;
            _spawned = true;

            if (CathayCrossing.Network.NetworkManager.Instance != null) return;

            // 先注入遠端玩家的正式外觀工廠，NetworkManager 收到 INIT/ENTER
            // 生成別人時就用得上（沒有它會退回膠囊替身）。
            CathayCrossing.Network.NetworkManager.RemoteAvatarBuilder = RemoteAvatarBuilder.Build;

            var go = new GameObject("__NetworkChat");
            DontDestroyOnLoad(go);
            go.AddComponent<CathayCrossing.Network.NetworkManager>();
            go.AddComponent<CathayCrossing.HD2D.ChatManager>();
            go.AddComponent<CathayCrossing.HD2D.ChatInputUI>();
            go.AddComponent<CathayCrossing.HD2D.ChatHistoryUI>();
            Debug.Log("[NetworkChatBootstrap] NetworkManager + ChatManager + ChatInputUI + ChatHistoryUI online.");
        }
    }
}
