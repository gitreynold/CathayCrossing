using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace CathayCrossing.HD2D
{
    public class CharacterIdDisplay : MonoBehaviour
    {
        [Header("Refs")]
        public Canvas canvas;
        public TextMeshProUGUI textMesh;
        public Vector3 offset = new Vector3(0, 2.0f, 0);

        [Header("Settings")]
        public bool lookAtCamera = true;
        public Color textColor = Color.white;
        public float fontSize = 24f;

        private Camera _mainCam;
        private bool _isLocalPlayer;
        private RectTransform _canvasRect;

        void Awake()
        {
            EnsureUI();
        }

        void EnsureUI()
        {
            if (canvas == null)
            {
                Transform t = transform.Find("ID_Canvas");
                if (t != null)
                {
                    canvas = t.GetComponent<Canvas>();
                    textMesh = t.GetComponentInChildren<TextMeshProUGUI>();
                }

                if (canvas == null)
                {
                    GameObject canvasGo = new GameObject("ID_Canvas");
                    // We still keep it as a child for organizational purposes, 
                    // but we will override its position in LateUpdate.
                    canvasGo.transform.SetParent(transform, false);
                    
                    canvas = canvasGo.AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.WorldSpace;
                    
                    canvasGo.AddComponent<CanvasScaler>();
                    canvasGo.AddComponent<GraphicRaycaster>();
                    
                    _canvasRect = canvasGo.GetComponent<RectTransform>();
                    _canvasRect.sizeDelta = new Vector2(200, 50);
                    _canvasRect.localScale = new Vector3(0.01f, 0.01f, 0.01f);
                    _canvasRect.localPosition = offset;

                    GameObject textGo = new GameObject("ID_Text");
                    textGo.transform.SetParent(canvasGo.transform, false);
                    
                    textMesh = textGo.AddComponent<TextMeshProUGUI>();
                    textMesh.alignment = TextAlignmentOptions.Center;
                    textMesh.fontSize = fontSize;
                    textMesh.color = textColor;
                    textMesh.text = "";
                    
                    textMesh.fontMaterial.EnableKeyword("OUTLINE_ON");
                    textMesh.outlineWidth = 0.25f;
                    textMesh.outlineColor = Color.black;

                    RectTransform textRect = textGo.GetComponent<RectTransform>();
                    textRect.anchorMin = Vector2.zero;
                    textRect.anchorMax = Vector2.one;
                    textRect.sizeDelta = Vector2.zero;
                    textRect.localPosition = Vector3.zero;

                    canvas.sortingOrder = 1000;
                }
            }
            
            if (canvas != null && _canvasRect == null)
            {
                _canvasRect = canvas.GetComponent<RectTransform>();
            }
        }

        void Start()
        {
            _mainCam = Camera.main;
            
            if (CompareTag("Player"))
            {
                _isLocalPlayer = true;
            }
        }

        public void SetId(string id)
        {
            EnsureUI();
            if (textMesh != null)
            {
                textMesh.text = id;
            }
        }

        void LateUpdate()
        {
            if (textMesh == null || canvas == null || _canvasRect == null) return;

            // Update ID for local player if needed
            if (_isLocalPlayer && (string.IsNullOrEmpty(textMesh.text) || textMesh.text == "連線中..."))
            {
                if (CathayCrossing.Network.NetworkManager.Instance != null)
                {
                    string myId = CathayCrossing.Network.NetworkManager.Instance.MyPlayerId;
                    if (!string.IsNullOrEmpty(myId) && myId != "連線中...")
                    {
                        SetId(myId);
                    }
                }
            }

            // FORCE POSITION: Always set world position based on the current transform position + offset.
            // This ensures it follows even if the hierarchy is complex or if there is interpolation lag.
            _canvasRect.position = transform.position + offset;

            if (lookAtCamera)
            {
                if (_mainCam == null) _mainCam = Camera.main;
                if (_mainCam != null)
                {
                    _canvasRect.rotation = _mainCam.transform.rotation;
                }
            }
        }
    }
}
