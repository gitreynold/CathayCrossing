using UnityEngine;

namespace OfficeLife
{
    public class GameBootstrap : MonoBehaviour
    {
        [Header("Optional Scene References")]
        public TaskManager taskManager;
        public DialogueSystem dialogueSystem;

        private void Awake()
        {
            EnsureManager(ref taskManager, "TaskManager");
            EnsureManager(ref dialogueSystem, "DialogueSystem");
        }

        private void EnsureManager<T>(ref T instance, string name) where T : MonoBehaviour
        {
            if (instance != null) return;

            T existing = FindObjectOfType<T>();
            if (existing == null)
            {
                GameObject go = new GameObject(name);
                instance = go.AddComponent<T>();
                DontDestroyOnLoad(go);
            }
            else
            {
                instance = existing;
            }
        }
    }
}
