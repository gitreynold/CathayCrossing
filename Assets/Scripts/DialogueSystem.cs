using System.Collections.Generic;
using UnityEngine;

namespace OfficeLife
{
    [System.Serializable]
    public class DialogueEntry
    {
        public string id;
        public string speakerName;
        public string[] lines;
    }

    [System.Serializable]
    public class DialogueDatabase
    {
        public DialogueEntry[] dialogues;
    }

    public class DialogueSystem : MonoBehaviour
    {
        public static DialogueSystem Instance { get; private set; }

        public string resourcePath = "Dialogue/dialogue_office";

        private Dictionary<string, DialogueEntry> map = new Dictionary<string, DialogueEntry>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Load();
        }

        private void Load()
        {
            map.Clear();
            TextAsset asset = Resources.Load<TextAsset>(resourcePath);
            if (asset == null)
            {
                Debug.LogWarning("DialogueSystem: missing resource at " + resourcePath);
                return;
            }

            DialogueDatabase db = JsonUtility.FromJson<DialogueDatabase>(asset.text);
            if (db == null || db.dialogues == null) return;

            foreach (var entry in db.dialogues)
            {
                if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
                map[entry.id] = entry;
            }
        }

        public DialogueEntry Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            map.TryGetValue(id, out DialogueEntry entry);
            return entry;
        }

        public void Play(string id)
        {
            if (DialogueUI.Instance == null) return;

            DialogueEntry entry = Get(id);
            if (entry == null)
            {
                Debug.LogWarning("DialogueSystem: missing dialogue id " + id);
                return;
            }

            DialogueUI.Instance.Open(entry);
        }
    }
}
