using UnityEngine;

namespace ClutchFPS.Weapons
{
    /// Procedural confirmation sounds for the hit/kill loop: a sharp tick on
    /// hit, higher tick on headshot, and a two-tone ding on kill. Placeholder
    /// quality but instant and dependency-free; swap for real SFX later.
    public static class FeedbackAudio
    {
        private static AudioClip _hitTick;
        private static AudioClip _headTick;
        private static AudioClip _killDing;

        public static void PlayHit(Vector3 position, bool headshot)
        {
            EnsureClips();
            AudioSource.PlayClipAtPoint(headshot ? _headTick : _hitTick, position, 0.55f);
        }

        public static void PlayKill(Vector3 position)
        {
            EnsureClips();
            AudioSource.PlayClipAtPoint(_killDing, position, 0.7f);
        }

        private static void EnsureClips()
        {
            if (_hitTick != null) return;
            _hitTick = MakeTone("HitTick", 1700f, 0.045f, 55f);
            _headTick = MakeTone("HeadTick", 2400f, 0.05f, 50f);
            _killDing = MakeTwoTone("KillDing", 880f, 1320f, 0.2f);
        }

        private static AudioClip MakeTone(string name, float frequency, float duration, float decay)
        {
            const int rate = 44100;
            int count = (int)(rate * duration);
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / rate;
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * Mathf.Exp(-t * decay) * 0.6f;
            }
            var clip = AudioClip.Create(name, count, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip MakeTwoTone(string name, float startFreq, float endFreq, float duration)
        {
            const int rate = 44100;
            int count = (int)(rate * duration);
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / rate;
                // Second tone takes over halfway through: a little rising "ding-ding".
                float freq = t < duration * 0.5f ? startFreq : endFreq;
                float envelope = Mathf.Exp(-(t % (duration * 0.5f)) * 22f);
                samples[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * envelope * 0.5f;
            }
            var clip = AudioClip.Create(name, count, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
