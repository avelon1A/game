using System.Collections.Generic;
using UnityEngine;

namespace Veil.Audio
{
    /// <summary>
    /// Synthesised sound effects and a light ambient music loop — no audio assets required.
    /// </summary>
    public static class Sfx
    {
        public static AudioClip Step, Click, Hover, Shoot, Hit, HitMe, Dash, Pulse, Decoy, Orb, Core, Key, Capture, Vault, Eliminate, Beep, Start, Jump, Land, Objective, Error, Buy, Reveal;
        public static float Volume = 0.8f;
        public static float MusicVolume = 0.35f;

        private static readonly List<AudioSource> Pool = new List<AudioSource>();
        private static GameObject _host;
        private static AudioSource _music;
        private const int Rate = 44100;

        public static void Init()
        {
            if (_host != null) return;
            _host = new GameObject("Audio");
            Object.DontDestroyOnLoad(_host);
            for (int i = 0; i < 24; i++)
            {
                var s = _host.AddComponent<AudioSource>();
                s.playOnAwake = false;
                Pool.Add(s);
            }

            // soft, low footstep "thud": heavily low-passed noise + a falling low sine (no hissy click)
            float lp = 0;
            Step = Tone(0.09f, t =>
            {
                lp += (Noise() - lp) * 0.06f;
                return (lp * 1.6f + Sin(t, Mathf.Lerp(85, 55, t / 0.09f)) * 0.55f) * Env(t, 0.09f, 0.006f) * 0.3f;
            });
            Click = Tone(0.05f, t => Sq(t, 900) * Env(t, 0.05f, 0.002f) * 0.4f);
            Hover = Tone(0.03f, t => Sin(t, 1400) * Env(t, 0.03f, 0.002f) * 0.25f);
            Shoot = Tone(0.16f, t => (Saw(t, Mathf.Lerp(1300, 380, t / 0.16f)) * 0.5f + Noise() * 0.15f) * Env(t, 0.16f, 0.003f) * 0.5f);
            Hit = Tone(0.14f, t => (Noise() * 0.6f + Sin(t, Mathf.Lerp(220, 90, t / 0.14f))) * Env(t, 0.14f, 0.002f) * 0.55f);
            HitMe = Tone(0.25f, t => (Noise() * 0.5f + Sq(t, Mathf.Lerp(160, 60, t / 0.25f)) * 0.6f) * Env(t, 0.25f, 0.002f) * 0.6f);
            Dash = Tone(0.3f, t => Noise() * Mathf.Sin(t / 0.3f * Mathf.PI) * 0.45f * (0.5f + 0.5f * Sin(t, 30)));
            Pulse = Tone(0.9f, t => (Sin(t, Mathf.Lerp(200, 900, t / 0.9f)) * 0.6f + Sin(t, Mathf.Lerp(400, 1800, t / 0.9f)) * 0.25f) * Env(t, 0.9f, 0.01f) * 0.5f);
            Decoy = Tone(0.5f, t => Sin(t, 600 + 400 * Mathf.Floor(t * 16) % 3) * Env(t, 0.5f, 0.005f) * 0.35f);
            Orb = Tone(0.18f, t => (Sin(t, 1320) + Sin(t, 1980) * 0.5f) * Env(t, 0.18f, 0.002f) * 0.3f);
            Core = Tone(0.6f, t => (Sin(t, 660) + Sin(t, 880) * 0.7f + Sin(t, 1320) * 0.5f) * Env(t, 0.6f, 0.005f) * 0.3f);
            Key = Tone(0.5f, t => (Sin(t, 1567) + Sin(t, 2349) * 0.4f) * Env(t, 0.5f, 0.001f) * 0.35f);
            Capture = Tone(0.9f, t => Sin(t, t < 0.15f ? 523 : t < 0.3f ? 659 : 784) * Env(t, 0.9f, 0.005f) * 0.4f + Sin(t, 1046) * Env(t, 0.9f, 0.3f) * 0.1f);
            Vault = Tone(1.6f, t => (Sin(t, 392) + Sin(t, 587) * 0.7f + Sin(t, 784) * 0.5f + Noise() * 0.1f * Env(t, 0.3f, 0.001f)) * Env(t, 1.6f, 0.01f) * 0.35f);
            Eliminate = Tone(0.7f, t => Sq(t, Mathf.Lerp(600, 120, t / 0.7f)) * Env(t, 0.7f, 0.005f) * 0.3f);
            Beep = Tone(0.12f, t => Sin(t, 880) * Env(t, 0.12f, 0.003f) * 0.4f);
            Start = Tone(1.0f, t => (Saw(t, 220) * 0.4f + Saw(t, 330) * 0.3f + Sin(t, 440)) * Env(t, 1.0f, 0.02f) * 0.3f);
            Jump = Tone(0.12f, t => Sin(t, Mathf.Lerp(300, 600, t / 0.12f)) * Env(t, 0.12f, 0.002f) * 0.25f);
            Land = Tone(0.1f, t => (Noise() * 0.5f + Sin(t, 90)) * Env(t, 0.1f, 0.001f) * 0.3f);
            Objective = Tone(1.2f, t => (Sin(t, t < 0.2f ? 659 : t < 0.4f ? 784 : 1046) + Sin(t, 1318) * 0.3f) * Env(t, 1.2f, 0.005f) * 0.4f);
            Error = Tone(0.2f, t => Sq(t, 150) * Env(t, 0.2f, 0.002f) * 0.3f);
            Buy = Tone(0.3f, t => (Sin(t, 988) + Sin(t, 1318) * (t > 0.08f ? 1 : 0)) * Env(t, 0.3f, 0.002f) * 0.3f);
            Reveal = Tone(0.6f, t => Sin(t, 440 + Mathf.Sin(t * 40) * 60) * Env(t, 0.6f, 0.01f) * 0.35f);

            _music = _host.AddComponent<AudioSource>();
            _music.loop = true;
            _music.clip = MakeMusic();
            _music.volume = MusicVolume;
            _music.Play();
        }

        public static void Play(AudioClip clip, float vol = 1f, float pitch = 1f)
        {
            if (clip == null || _host == null) return;
            var s = Free();
            s.spatialBlend = 0;
            s.pitch = pitch * Random.Range(0.97f, 1.03f);
            s.PlayOneShot(clip, vol * Volume);
        }

        public static void PlayAt(AudioClip clip, Vector3 pos, float vol = 1f, float pitch = 1f)
        {
            if (clip == null || _host == null) return;
            var s = Free();
            s.transform.position = pos;
            s.spatialBlend = 0.85f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 4;
            s.maxDistance = 45;
            s.pitch = pitch * Random.Range(0.94f, 1.06f);
            s.PlayOneShot(clip, vol * Volume);
        }

        public static void SetMusic(float v)
        {
            MusicVolume = v;
            if (_music) _music.volume = v;
        }

        private static AudioSource Free()
        {
            foreach (var s in Pool) if (!s.isPlaying) return s;
            return Pool[Random.Range(0, Pool.Count)];
        }

        // ------------------------------------------------------------------ synthesis

        private static readonly System.Random Rnd = new System.Random(7);
        private static float Noise() => (float)Rnd.NextDouble() * 2 - 1;
        private static float Sin(float t, float f) => Mathf.Sin(2 * Mathf.PI * f * t);
        private static float Sq(float t, float f) => Mathf.Sign(Mathf.Sin(2 * Mathf.PI * f * t));
        private static float Saw(float t, float f) => 2 * (t * f - Mathf.Floor(t * f + 0.5f));
        private static float Env(float t, float len, float attack) => Mathf.Clamp01(t / attack) * Mathf.Pow(1 - Mathf.Clamp01(t / len), 2);

        private static AudioClip Tone(float len, System.Func<float, float> f)
        {
            int n = (int)(len * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate), -1, 1);
            var c = AudioClip.Create("sfx", n, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }

        /// <summary>16-bar chill loop: pad chords + soft arpeggio + light kick.</summary>
        private static AudioClip MakeMusic()
        {
            float bpm = 100, beat = 60f / bpm;
            int bars = 8;
            float len = bars * 4 * beat;
            int n = (int)(len * 22050);
            var data = new float[n];
            float[][] chords =
            {
                new[] { 220f, 261.6f, 329.6f }, new[] { 174.6f, 220f, 261.6f }, new[] { 196f, 246.9f, 293.7f }, new[] { 164.8f, 207.7f, 246.9f },
            };
            for (int i = 0; i < n; i++)
            {
                float t = i / 22050f;
                int bar = (int)(t / (4 * beat)) % bars;
                var ch = chords[bar / 2 % chords.Length];
                float pad = 0;
                foreach (var f in ch) pad += Mathf.Sin(2 * Mathf.PI * f * t) * 0.5f + Mathf.Sin(2 * Mathf.PI * f * 2.003f * t) * 0.12f;
                pad *= 0.12f * (0.8f + 0.2f * Mathf.Sin(t * 0.7f));
                float stepT = t / (beat / 2);
                int step = (int)stepT;
                float st = (stepT - step) * beat / 2;
                float arpF = ch[step % 3] * (step % 8 >= 4 ? 2 : 4);
                float arp = Mathf.Sin(2 * Mathf.PI * arpF * t) * Mathf.Exp(-st * 9) * 0.07f;
                float bt = t % beat;
                float kick = Mathf.Sin(2 * Mathf.PI * (50 + 80 * Mathf.Exp(-bt * 30)) * bt) * Mathf.Exp(-bt * 12) * 0.18f;
                float hat = ((int)(t / (beat / 2)) % 2 == 1) ? Noise() * Mathf.Exp(-((t % (beat / 2))) * 60) * 0.03f : 0;
                data[i] = Mathf.Clamp(pad + arp + kick + hat, -1, 1);
            }
            // smooth loop seam
            int fade = 2000;
            for (int i = 0; i < fade; i++) { float k = i / (float)fade; data[i] *= k; data[n - 1 - i] *= k; }
            var clip = AudioClip.Create("music", n, 1, 22050, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
