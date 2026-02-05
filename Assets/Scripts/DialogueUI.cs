using TMPro;
using UnityEngine;

namespace OfficeLife
{
    public class DialogueUI : MonoBehaviour
    {
        public static DialogueUI Instance { get; private set; }

        public GameObject root;
        public TMP_Text speakerText;
        public TMP_Text lineText;
        public TMP_Text continueText;
        public KeyCode nextKey = KeyCode.Space;

        private string[] lines;
        private int index;

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (root != null) root.SetActive(false);
        }

        private void Update()
        {
            if (!IsOpen) return;

            if (Input.GetKeyDown(nextKey) || Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0))
            {
                NextLine();
            }
        }

        public void Open(DialogueEntry entry)
        {
            if (entry == null) return;

            lines = entry.lines;
            index = 0;
            IsOpen = true;

            if (root != null) root.SetActive(true);
            if (speakerText != null) speakerText.text = entry.speakerName;

            ShowLine();
        }

        private void ShowLine()
        {
            if (lineText == null || lines == null) return;
            if (lines.Length == 0)
            {
                Close();
                return;
            }

            lineText.text = lines[index];
        }

        private void NextLine()
        {
            if (lines == null) { Close(); return; }

            index++;
            if (index >= lines.Length)
            {
                Close();
                return;
            }

            ShowLine();
        }

        private void Close()
        {
            IsOpen = false;
            if (root != null) root.SetActive(false);
        }
    }
}
