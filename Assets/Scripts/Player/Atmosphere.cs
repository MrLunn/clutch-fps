using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ClutchFPS.Core;

namespace ClutchFPS.Player
{
    /// Global mood pass: a runtime URP volume (color grade + vignette + grain)
    /// plus a dynamic low-HP state — the vignette bleeds red and pulses, the
    /// world desaturates, and a heartbeat kicks in as you near death.
    /// Self-bootstraps so no scene wiring is needed.
    public class Atmosphere : MonoBehaviour
    {
        private Volume _volume;
        private Vignette _vignette;
        private ColorAdjustments _color;

        private Health _health;
        private float _findTimer;
        private float _heartbeatTimer;
        private AudioSource _audio;
        private AudioClip _heartbeat;

        // Baseline mood (also the values we return to at full HP).
        private const float BaseVignette = 0.26f;
        private const float BaseSaturation = -10f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<Atmosphere>() != null) return;
            var go = new GameObject("Atmosphere");
            go.AddComponent<Atmosphere>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();

            _color = profile.Add<ColorAdjustments>(true);
            _color.saturation.Override(BaseSaturation);
            _color.contrast.Override(6f);
            _color.postExposure.Override(-0.15f);
            _color.colorFilter.Override(new Color(0.92f, 0.95f, 1f)); // faint cool wash

            _vignette = profile.Add<Vignette>(true);
            _vignette.intensity.Override(BaseVignette);
            _vignette.smoothness.Override(0.45f);
            _vignette.color.Override(Color.black);

            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.22f);
            grain.response.Override(0.8f);

            _volume = gameObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 10f;
            _volume.profile = profile;

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            _audio.volume = 0.7f;
            _heartbeat = BuildHeartbeat();
        }

        private void Update()
        {
            if (_health == null)
            {
                _findTimer -= Time.deltaTime;
                if (_findTimer <= 0f) { FindLocalHealth(); _findTimer = 0.5f; }
            }

            float hp = _health != null ? Mathf.Clamp01(_health.CurrentHealth / Mathf.Max(1f, _health.MaxHealth)) : 1f;

            // "hurt" ramps in once HP drops below 55%, maxing out near death.
            float hurt = Mathf.Clamp01((0.55f - hp) / 0.55f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * (5f + hurt * 5f));

            _vignette.intensity.value = BaseVignette + hurt * (0.22f + 0.16f * pulse);
            _vignette.color.value = Color.Lerp(Color.black, new Color(0.55f, 0f, 0f), hurt);
            _color.saturation.value = BaseSaturation - hurt * 45f;

            // Heartbeat once critically low (and still alive).
            if (hp > 0.001f && hp < 0.35f)
            {
                _heartbeatTimer -= Time.deltaTime;
                if (_heartbeatTimer <= 0f)
                {
                    if (_heartbeat != null) _audio.PlayOneShot(_heartbeat, 0.6f + hurt * 0.4f);
                    _heartbeatTimer = Mathf.Lerp(0.55f, 1.1f, hp / 0.35f); // faster as HP falls
                }
            }
            else
            {
                _heartbeatTimer = 0f;
            }
        }

        private void FindLocalHealth()
        {
            foreach (var r in FindObjectsByType<PlayerRespawn>(FindObjectsSortMode.None))
            {
                if (r.IsOwner && r.TryGetComponent<Health>(out var h)) { _health = h; return; }
            }
        }

        /// Two-thump "lub-dub" built procedurally so we ship no audio asset.
        private static AudioClip BuildHeartbeat()
        {
            const int rate = 44100;
            int len = (int)(rate * 0.45f);
            var data = new float[len];
            void Thump(float startSec, float amp, float freq)
            {
                int s = (int)(startSec * rate);
                int dur = (int)(0.12f * rate);
                for (int i = 0; i < dur && s + i < len; i++)
                {
                    float t = i / (float)rate;
                    float env = Mathf.Exp(-t * 34f);
                    data[s + i] += Mathf.Sin(2f * Mathf.PI * freq * t) * env * amp;
                }
            }
            Thump(0.00f, 0.9f, 62f);
            Thump(0.16f, 0.55f, 52f);
            var clip = AudioClip.Create("Heartbeat", len, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
