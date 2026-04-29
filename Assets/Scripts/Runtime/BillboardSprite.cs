using UnityEngine;

namespace CathayCrossing.HD2D
{
    /// Y-axis billboard so 2D character sprites stay vertical and face the camera,
    /// matching Octopath Traveler's HD-2D presentation.
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class BillboardSprite : MonoBehaviour
    {
        public enum Mode { YAxisOnly, FullBillboard }
        public Mode mode = Mode.YAxisOnly;

        Camera _cam;

        void LateUpdate()
        {
            if (_cam == null || !_cam.isActiveAndEnabled)
                _cam = Camera.main;
            if (_cam == null) return;

            if (mode == Mode.FullBillboard)
            {
                transform.forward = _cam.transform.forward;
            }
            else
            {
                Vector3 fwd = _cam.transform.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.0001f) return;
                transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            }
        }
    }
}
