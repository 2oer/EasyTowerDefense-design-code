using UnityEngine;

namespace Game.UnityBridge
{
    /// <summary>
    /// 音效播放服务:自带一组 AudioSource 轮换,支持 2D/3D 混用。
    /// 资源未配置时静默跳过,不阻塞战斗逻辑。
    /// </summary>
    public sealed class AudioService : MonoBehaviour
    {
        [SerializeField] private AudioLibrary library;
        [SerializeField] private int voiceCount = 12;
        [SerializeField] private AudioSource musicSource;

        private AudioSource[] _voices;
        private int _cursor;

        public static AudioService Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _voices = new AudioSource[Mathf.Max(2, voiceCount)];
            for (int i = 0; i < _voices.Length; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                _voices[i] = source;
            }
        }

        private void Start()
        {
            if (musicSource == null || library == null || library.Music == null) return;
            musicSource.clip = library.Music;
            musicSource.loop = true;
            musicSource.volume = library.MusicVolume;
            musicSource.Play();
        }

        public void Play(string id)
        {
            Play(id, Vector3.zero, false);
        }

        public void Play(string id, Vector3 position, bool spatial)
        {
            if (library == null) return;

            AudioLibrary.Sfx sfx = library.Find(id);
            if (sfx == null || sfx.clips == null || sfx.clips.Length == 0) return;

            AudioSource source = _voices[_cursor];
            _cursor = (_cursor + 1) % _voices.Length;

            AudioClip clip = sfx.clips[Random.Range(0, sfx.clips.Length)];
            source.Stop();
            source.transform.position = position;
            source.spatialBlend = spatial ? 1f : 0f;
            source.volume = sfx.volume;
            source.pitch = 1f + Random.Range(-sfx.pitchJitter, sfx.pitchJitter);
            source.clip = clip;
            source.Play();
        }

        /// <summary>升级 / 建造这类界面音效统一入口。</summary>
        public void PlayUi(string id)
        {
            Play(id);
        }
    }
}
