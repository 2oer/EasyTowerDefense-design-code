using System;
using UnityEngine;

namespace Game.UnityBridge
{
    /// <summary>音效索引:按 id 取一组可随机轮换的片段,避免连续触发时听感重复。</summary>
    [CreateAssetMenu(menuName = "Game/Audio Library", fileName = "AudioLibrary")]
    public sealed class AudioLibrary : ScriptableObject
    {
        [Serializable]
        public sealed class Sfx
        {
            public string id;
            public AudioClip[] clips;
            [Range(0f, 1f)] public float volume = 0.8f;
            [Range(0.7f, 1.3f)] public float pitchJitter = 0.06f;
        }

        [SerializeField] private Sfx[] sfx;
        [SerializeField] private AudioClip music;
        [Range(0f, 1f)] [SerializeField] private float musicVolume = 0.5f;

        public AudioClip Music { get { return music; } }
        public float MusicVolume { get { return musicVolume; } }

        public Sfx Find(string id)
        {
            if (sfx == null) return null;
            for (int i = 0; i < sfx.Length; i++)
            {
                if (string.Equals(sfx[i].id, id, StringComparison.Ordinal)) return sfx[i];
            }
            return null;
        }
    }
}
