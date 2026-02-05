using TMPro;
using UnityEngine;

namespace OfficeLife
{
    public class InteractionPromptUI : MonoBehaviour
    {
        public static InteractionPromptUI Instance { get; private set; }

        public GameObject root;
        public TMP_Text promptText;

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

        public void Show(string text)
        {
            if (promptText != null) promptText.text = text;
            if (root != null) root.SetActive(true);
        }

        public void Hide()
        {
            if (root != null) root.SetActive(false);
        }
    }
}
