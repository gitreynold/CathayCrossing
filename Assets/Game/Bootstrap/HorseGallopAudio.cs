using UnityEngine;

namespace CathayCrossing.Bootstrap
{
    /// <summary>
    /// Looping gallop SFX for the Afa-ride-horse ("小金馬") NPC.
    ///
    /// The sound is a full 3D AudioSource, so it gets LOUDER as the listener
    /// (the scene camera/AudioListener) gets closer and fades out with distance.
    /// The loop is also gated on actual movement: a horse that has been summoned
    /// and parked in front of the player (frozen by AfaRideHorseSummon) goes
    /// quiet, then fades back in when it starts roaming again.
    ///
    /// Added at spawn time by OfficeAfaRideHorseSpawner — no inspector wiring.
    /// </summary>
    [DisallowMultipleComponent]
    public class HorseGallopAudio : MonoBehaviour
    {
        [Tooltip("Resources path of the looping gallop clip.")]
        public string clipResourcePath = "Audio/Harse_Gallop_Loop";

        [Tooltip("Loudest the gallop gets when the listener is within minDistance.")]
        [Range(0f, 1f)] public float maxVolume = 1f;

        [Tooltip("World-space radius (metres) inside which the gallop plays at " +
                 "full volume. Closer than this = max loudness.")]
        public float minDistance = 1.5f;

        [Tooltip("World-space radius (metres) beyond which the gallop is silent. " +
                 "Between min and max it scales down with distance.")]
        public float maxDistance = 16f;

        [Tooltip("Speed (m/s) above which the horse counts as moving and the " +
                 "gallop loop is audible.")]
        public float moveThreshold = 0.2f;

        [Tooltip("How fast the gallop eases in/out as the horse starts/stops.")]
        public float volumeLerp = 8f;

        AudioSource _src;
        Vector3 _lastPos;
        float _gate;   // 0..1 movement gate, eased

        void Start()
        {
            _src = gameObject.AddComponent<AudioSource>();
            _src.clip         = Resources.Load<AudioClip>(clipResourcePath);
            _src.loop         = true;
            _src.playOnAwake  = false;
            _src.spatialBlend = 1f;                       // full 3D → distance controls loudness
            _src.rolloffMode  = AudioRolloffMode.Linear;  // even, easy-to-hear "closer = louder" ramp
            _src.dopplerLevel = 0f;                       // no pitch warble as it darts around
            _src.spread       = 60f;                      // a little width so it isn't a hard point

            // The horse root is scaled down (~0.6). Unity scales an AudioSource's
            // min/max distance by the GameObject's lossyScale, which would shrink
            // the audible range unpredictably. Divide it back out so the values
            // above are real world-space metres.
            float s = Mathf.Max(transform.lossyScale.x, 0.01f);
            _src.minDistance = minDistance / s;
            _src.maxDistance = maxDistance / s;

            _src.volume = 0f;
            if (_src.clip != null) _src.Play();
            else Debug.LogWarning($"[HorseGallopAudio] Gallop clip not found at " +
                                  $"Resources/{clipResourcePath}. Horse will be silent.");

            _lastPos = transform.position;
        }

        void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float speed = (transform.position - _lastPos).magnitude / dt;
            _lastPos = transform.position;

            float target = speed > moveThreshold ? 1f : 0f;
            _gate = Mathf.MoveTowards(_gate, target, volumeLerp * Time.deltaTime);

            // Source volume sets the "loudness at the source"; the 3D rolloff
            // above then attenuates it by listener distance. Net effect: audible
            // only while moving, and louder the closer you stand to the horse.
            if (_src != null) _src.volume = _gate * maxVolume;
        }
    }
}
