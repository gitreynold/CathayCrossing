using UnityEngine;

namespace OfficeLife
{
    [CreateAssetMenu(menuName = "OfficeLife/Dialogue")]
    public class DialogueData : ScriptableObject
    {
        public string speakerName;
        [TextArea] public string[] lines;
    }
}
