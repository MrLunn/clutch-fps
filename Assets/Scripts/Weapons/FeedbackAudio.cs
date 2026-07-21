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
        private static AudioClip _headshotKill;

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

        /// The money shot: a headshot that kills. Louder and meatier than a
        /// normal kill — a bright crack layered over a low thump and a ringing
        /// bell, the sound you want to keep chasing.
        public static void PlayHeadshotKill(Vector3 position)
        {
            EnsureClips();
            AudioSource.PlayClipAtPoint(_headshotKill, position, 0.95f);
        }

        private static void EnsureClips()
        {
            if (_hitTick != null) return;
            _hitTick = MakeTone("HitTick", 1700f, 0.045f, 55f);
            _headTick = MakeTone("HeadTick", 2400f, 0.05f, 50f);
            _killDing = MakeTwoTone("KillDing", 880f, 1320f, 0.2f);
            _headshotKill = MakeHeadshotKill("HeadshotKill");
        }

        /// Three layers summed into one clip: a short noise-burst "crack"
        /// transient, a low sine "thump" for body, and a bright two-partial
        /// bell that rings out. Soft-clipped so the stacked layers stay punchy
        /// without digital harshness.
        private static AudioClip MakeHeadshotKill(string name)
        {
            const int rate = 44100;
            const float duration = 0.42f;
            int count = (int)(rate * duration);
            var samples = new float[count];
            var rng = new System.Random(1337);

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / rate;

                // Crack: filtered noise, gone in ~25ms — the initial impact.
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float crack = noise * Mathf.Exp(-t * 140f) * 0.7f;

                // Thump: low body that gives it weight.
                float thump = Mathf.Sin(2f * Mathf.PI * 140f * t) * Mathf.Exp(-t * 26f) * 0.8f;

                // Bell: two partials ringing, the satisfying "ding" tail.
                float bell =
                    (Mathf.Sin(2f * Mathf.PI * 1180f * t) * 0.6f +
                     Mathf.Sin(2f * Mathf.PI * 1760f * t) * 0.4f)
                    * Mathf.Exp(-t * 12f) * 0.5f;

                float mixed = crack + thump + bell;
                // Soft clip (tanh-ish) to keep the stack punchy but clean.
                samples[i] = mixed / (1f + Mathf.Abs(mixed));
            }

            var clip = AudioClip.Create(name, count, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
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
