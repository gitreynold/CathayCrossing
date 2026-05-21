using UnityEngine;

namespace CathayCrossing.Characters
{
    /// <summary>
    /// Tilts the character's Spine bone every LateUpdate to compensate for
    /// a baked-in lean in the source FBX. Sits below CharacterDefinition's
    /// spineCorrectionEuler — the spawner attaches one of these to a
    /// freshly instantiated character body, sets <see cref="targetBone"/>
    /// to the Spine transform it finds in the rig, and copies the
    /// definition's euler value into <see cref="correctionEuler"/>.
    ///
    /// Why LateUpdate: Animator writes its output during Update, so any
    /// modification has to land after that or it gets clobbered next frame.
    /// We post-multiply, so the correction stacks on top of whatever the
    /// idle/walk clip just produced; the character stays in sync with its
    /// animation but tilted by a constant offset.
    /// </summary>
    [DefaultExecutionOrder(10)]
    public class PostureCorrection : MonoBehaviour
    {
        [Tooltip("Bone to rotate. Usually the character's Spine (so legs " +
                 "stay planted while the torso tilts).")]
        public Transform targetBone;

        [Tooltip("Euler offset (degrees) added in bone-local space.")]
        public Vector3 correctionEuler;

        void LateUpdate()
        {
            if (targetBone == null) return;
            if (correctionEuler == Vector3.zero) return;
            targetBone.localRotation *= Quaternion.Euler(correctionEuler);
        }

        // ─── Convenience attach ─────────────────────────────────────────

        /// <summary>
        /// Walks <paramref name="root"/>'s child Transforms looking for a
        /// bone named <paramref name="boneName"/> ("Spine" by default) and
        /// attaches a PostureCorrection component configured with
        /// <paramref name="euler"/>. No-op when the bone is missing or the
        /// euler is zero.
        /// </summary>
        public static PostureCorrection Attach(GameObject root, string boneName,
                                               Vector3 euler)
        {
            if (root == null) return null;
            if (euler == Vector3.zero) return null;

            Transform bone = null;
            foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (t != null && t.name == boneName) { bone = t; break; }
            }
            if (bone == null) return null;

            var pc = root.AddComponent<PostureCorrection>();
            pc.targetBone = bone;
            pc.correctionEuler = euler;
            return pc;
        }
    }
}
