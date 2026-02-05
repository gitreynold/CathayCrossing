using System;
using System.Collections.Generic;
using UnityEngine;

namespace OfficeLife
{
    public enum TaskState
    {
        None,
        InProgress
    }

    [Serializable]
    public class TaskDefinition
    {
        public string id;
        public string title;
        public string description;
        public string requiredItemId;
        public string targetId;
        public string startNpcId;
    }

    [Serializable]
    public class TaskDatabase
    {
        public TaskDefinition[] tasks;
    }

    public class TaskManager : MonoBehaviour
    {
        public static TaskManager Instance { get; private set; }

        public string resourcePath = "Tasks/tasks";
        public List<TaskDefinition> tasks = new List<TaskDefinition>();
        public int currentIndex = 0;
        public TaskState state = TaskState.None;

        public Action OnTaskChanged;

        public TaskDefinition CurrentTask
        {
            get
            {
                if (tasks.Count == 0) return null;
                if (currentIndex < 0 || currentIndex >= tasks.Count) return null;
                return tasks[currentIndex];
            }
        }

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
            TextAsset asset = Resources.Load<TextAsset>(resourcePath);
            if (asset == null)
            {
                Debug.LogWarning("TaskManager: missing resource at " + resourcePath);
                return;
            }

            TaskDatabase db = JsonUtility.FromJson<TaskDatabase>(asset.text);
            tasks = new List<TaskDefinition>(db != null && db.tasks != null ? db.tasks : new TaskDefinition[0]);
            currentIndex = Mathf.Clamp(currentIndex, 0, Mathf.Max(0, tasks.Count - 1));
            state = TaskState.None;
            Notify();
        }

        public bool TryStartWith(string npcId)
        {
            TaskDefinition task = CurrentTask;
            if (task == null) return false;
            if (state != TaskState.None) return false;
            if (!string.IsNullOrEmpty(task.startNpcId) && task.startNpcId != npcId) return false;

            state = TaskState.InProgress;
            Notify();
            return true;
        }

        public bool TryComplete(string targetId, Inventory inventory)
        {
            TaskDefinition task = CurrentTask;
            if (task == null) return false;
            if (state != TaskState.InProgress) return false;
            if (!string.IsNullOrEmpty(task.targetId) && task.targetId != targetId) return false;

            if (!string.IsNullOrEmpty(task.requiredItemId))
            {
                if (inventory == null || !inventory.HasItem(task.requiredItemId))
                {
                    Debug.Log("TaskManager: missing item " + task.requiredItemId);
                    return false;
                }
            }

            AdvanceTask();
            return true;
        }

        private void AdvanceTask()
        {
            currentIndex = Mathf.Min(currentIndex + 1, Mathf.Max(0, tasks.Count - 1));
            state = TaskState.None;
            Notify();
        }

        private void Notify()
        {
            if (OnTaskChanged != null) OnTaskChanged.Invoke();
        }
    }
}
