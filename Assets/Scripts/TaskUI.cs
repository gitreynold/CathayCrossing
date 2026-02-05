using TMPro;
using UnityEngine;

namespace OfficeLife
{
    public class TaskUI : MonoBehaviour
    {
        public TMP_Text titleText;
        public TMP_Text bodyText;

        private void Start()
        {
            if (TaskManager.Instance != null)
            {
                TaskManager.Instance.OnTaskChanged += Refresh;
            }

            Refresh();
        }

        private void OnDestroy()
        {
            if (TaskManager.Instance != null)
            {
                TaskManager.Instance.OnTaskChanged -= Refresh;
            }
        }

        private void Refresh()
        {
            var manager = TaskManager.Instance;
            if (manager == null || manager.CurrentTask == null)
            {
                if (titleText != null) titleText.text = "No tasks";
                if (bodyText != null) bodyText.text = "";
                return;
            }

            var task = manager.CurrentTask;
            if (titleText != null)
            {
                string prefix = manager.state == TaskState.None ? "Available: " : "Active: ";
                titleText.text = prefix + task.title;
            }

            if (bodyText != null) bodyText.text = task.description;
        }
    }
}
