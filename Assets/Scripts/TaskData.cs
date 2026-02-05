using UnityEngine;

namespace OfficeLife
{
    [CreateAssetMenu(menuName = "OfficeLife/Task")]
    public class TaskData : ScriptableObject
    {
        public string taskId;
        public string title;
        [TextArea] public string description;
        public string requiredItemId;
        public string targetId;
    }
}
