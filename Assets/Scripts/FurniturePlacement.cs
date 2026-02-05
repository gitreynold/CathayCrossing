using UnityEngine;

namespace OfficeLife
{
    public class FurniturePlacement : MonoBehaviour
    {
        public bool buildMode = false;
        public float gridSize = 1f;
        public GameObject[] placeables;
        public int selectedIndex = 0;
        public KeyCode toggleKey = KeyCode.B;

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                buildMode = !buildMode;
                Debug.Log("Build Mode: " + (buildMode ? "On" : "Off"));
            }

            if (!buildMode) return;

            if (Input.GetKeyDown(KeyCode.Alpha1)) selectedIndex = 0;
            if (Input.GetKeyDown(KeyCode.Alpha2)) selectedIndex = 1;
            if (Input.GetKeyDown(KeyCode.Alpha3)) selectedIndex = 2;

            if (Input.GetMouseButtonDown(0))
            {
                TryPlace();
            }
        }

        private void TryPlace()
        {
            if (placeables == null || placeables.Length == 0) return;
            if (selectedIndex < 0 || selectedIndex >= placeables.Length) return;

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                Vector3 pos = hit.point;
                pos.x = Mathf.Round(pos.x / gridSize) * gridSize;
                pos.z = Mathf.Round(pos.z / gridSize) * gridSize;
                pos.y = hit.point.y;

                Instantiate(placeables[selectedIndex], pos, Quaternion.identity);
            }
        }
    }
}
