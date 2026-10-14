using System.Collections.Generic;
using UnityEngine;

namespace Veil.Audio
{
    /// <summary>
    /// Synthesised sound effects and a light ambient music loop — no audio assets required.
    /// </summary>
    public static class Sfx
    {
        public static AudioClip Whoosh, Sniper, Shotgun, Smg, Headshot, HitTick, Boom, PingSnd, Alarm, Step, Click, Hover, Shoot, Hit, HitMe, Dash, Pulse, Decoy, Orb, Core, Key, Capture, Vault, Eliminate, Beep, Start, Jump, Land, Objective, Error, Buy, Reveal;
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
            float wl = 0;
            Whoosh = Tone(0.7f, t => { wl += (Noise() - wl) * Mathf.Lerp(0.02f, 0.25f, Mathf.Sin(t / 0.7f * Mathf.PI)); return wl * 2.2f * Mathf.Sin(t / 0.7f * Mathf.PI); });
            Shoot = Gun(0.34f, 0.5f, 95f, 0.014f, 0.2f, 0.1f, 0.8f);
            Smg = Gun(0.2f, 0.45f, 120f, 0.01f, 0.11f, 0.07f, 0.6f);
            Sniper = Gun(1.7f, 1f, 52f, 0.022f, 1.3f, 0.32f, 1f);
            Shotgun = Gun(0.75f, 0.35f, 60f, 0.045f, 0.5f, 0.16f, 1f);
            float ilp = 0;
            Hit = Tone(0.12f, t =>
            {
                ilp += (Noise() - ilp) * 0.12f;
                return (ilp * 1.8f + Sin(t, Mathf.Lerp(140, 70, t / 0.12f)) * 0.7f) * Env(t, 0.12f, 0.001f) * 0.6f;
            });
            Headshot = Tone(0.32f, t => (Sin(t, 2637) * 0.55f + Sin(t, 3951) * 0.3f + Sin(t, 1318) * 0.25f) * Mathf.Exp(-t * 14f) * Mathf.Clamp01(t / 0.001f) * 0.45f);
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
            float hp = 0, hprev = 0;
            HitTick = Tone(0.045f, t =>
            {
                float n = Noise(); hp = 0.85f * (hp + n - hprev); hprev = n;
                return (hp * 0.6f + Sin(t, 3200) * 0.35f) * Mathf.Exp(-t * 90f) * 0.6f;
            });
            float blp = 0;
            Boom = Tone(1.1f, t =>
            {
                blp += (Noise() - blp) * Mathf.Lerp(0.5f, 0.03f, t / 1.1f);
                return (blp * 1.4f + Sin(t, Mathf.Lerp(90, 30, t / 1.1f)) * 0.8f) * Env(t, 1.1f, 0.002f) * 0.7f;
            });
            PingSnd = Tone(0.32f, t => (Sin(t, t < 0.1f ? 1046 : 1568) * 0.7f + Sin(t, 2093) * 0.15f) * Env(t, 0.32f, 0.002f) * 0.35f);
            Alarm = Tone(0.9f, t => Sq(t, (int)(t * 6) % 2 == 0 ? 740 : 554) * Env(t, 0.9f, 0.01f) * 0.22f);
            Reveal = Tone(0.6f, t => Sin(t, 440 + Mathf.Sin(t * 40) * 60) * Env(t, 0.6f, 0.01f) * 0.35f);

            _music = _host.AddComponent<AudioSource>();
            _music.loop = true;
            _music.clip = MakeMusic();
            _music.volume = MusicVolume;
            _music.Play();

            _ambient = _host.AddComponent<AudioSource>();
            _ambient.loop = true;
            _ambient.clip = MakeWind();
            _ambient.volume = 0;
            _ambient.Play();
        }

        private static AudioSource _ambient;
        private static bool _inMatch;

        /// <summary>In a match: quieter music under island wind; menus: music only.</summary>
        public static void SetMatchAudio(bool inMatch)
        {
            _inMatch = inMatch;
            if (_ambient) _ambient.volume = inMatch ? 0.32f * Volume : 0f;
            if (_music) _music.volume = inMatch ? MusicVolume * 0.35f : MusicVolume;
        }

        /// <summary>
        /// Layered gunshot: a sharp high-passed crack, a low body thump, a low-passed tail and a slap-back echo.
        /// </summary>
        private static AudioClip Gun(float len, float crack, float thumpHz, float crackLen, float tail, float echo, float vol)
        {
            float lp = 0, hp = 0, prev = 0, lp2 = 0;
            var dry = new float[(int)(len * Rate)];
            for (int i = 0; i < dry.Length; i++)
            {
                float t = i / (float)Rate;
                float n = Noise();
                hp = 0.92f * (hp + n - prev); prev = n;
                lp += (n - lp) * Mathf.Lerp(0.35f, 0.02f, Mathf.Clamp01(t / tail));
                lp2 += (lp - lp2) * 0.2f;
                float crackS = hp * crack * Mathf.Exp(-t / crackLen) * Mathf.Clamp01(t / 0.0004f);
                float body = Sin(t, Mathf.Lerp(thumpHz * 1.8f, thumpHz * 0.7f, Mathf.Clamp01(t / 0.08f))) * Mathf.Exp(-t * 28f) * 0.9f;
                float tailS = lp2 * 2.2f * Mathf.Exp(-t / (tail * 0.4f));
                dry[i] = crackS + body + tailS;
            }
            int lag = (int)(echo * Rate);
            var data = new float[dry.Length];
            for (int i = 0; i < data.Length; i++)
            {
                float v = dry[i] + (i >= lag ? dry[i - lag] * 0.18f : 0f) + (i >= lag * 2 ? dry[i - lag * 2] * 0.05f : 0f);
                data[i] = Mathf.Clamp((float)System.Math.Tanh(v * 1.15f) * vol * 0.7f, -1, 1);
            }
            int fade = Mathf.Min(800, data.Length / 4);
            for (int i = 0; i < fade; i++) data[data.Length - 1 - i] *= i / (float)fade;
            var c = AudioClip.Create("gun", data.Length, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }

        /// <summary>Soft looping island wind: low-passed noise with slow gusts.</summary>
        private static AudioClip MakeWind()
        {
            int rate = 22050, n = rate * 12;
            var data = new float[n];
            float lp = 0, lp2 = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float gust = 0.55f + 0.3f * Mathf.Sin(t * 2 * Mathf.PI / 6f) + 0.15f * Mathf.Sin(t * 2 * Mathf.PI / 2.4f + 1f);
                lp += (Noise() - lp) * (0.02f + 0.03f * gust);
                lp2 += (lp - lp2) * 0.08f;
                data[i] = lp2 * 3.5f * gust;
            }
            int fade = rate;
            for (int i = 0; i < fade; i++) { float k = i / (float)fade; data[n - fade + i] = data[n - fade + i] * (1 - k) + data[i] * k; }
            var clip = AudioClip.Create("wind", n - fade, 1, rate, false);
            var trimmed = new float[n - fade];
            System.Array.Copy(data, fade, trimmed, 0, n - fade);
            for (int i = 0; i < fade; i++) trimmed[n - 2 * fade + i] = data[n - fade + i];
            clip.SetData(trimmed, 0);
            return clip;
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
            s.minDistance = 5;
            s.maxDistance = 80;
            s.dopplerLevel = 0;
            s.pitch = pitch * Random.Range(0.94f, 1.06f);
            s.PlayOneShot(clip, vol * Volume);
        }

        private static readonly List<AudioSource> GunPool = new List<AudioSource>();
        private static readonly List<float> GunEnds = new List<float>();
        private const int MaxRemoteGuns = 4;

        /// <summary>
        /// Gunfire with a real mix: your own shots always play; other players' shots are limited to a few at once,
        /// fade with distance (log roll-off) and lose their highs far away, so a big fight doesn't turn into noise.
        /// </summary>
        public static void PlayGun(AudioClip clip, Vector3 pos, bool local, float vol)
        {
            if (clip == null || _host == null) return;
            if (GunPool.Count == 0)
            {
                for (int i = 0; i < 10; i++)
                {
                    var go = new GameObject("Gun" + i);
                    go.transform.SetParent(_host.transform, false);
                    var src = go.AddComponent<AudioSource>();
                    src.playOnAwake = false;
                    go.AddComponent<AudioLowPassFilter>();
                    GunPool.Add(src); GunEnds.Add(0);
                }
            }
            var cam = Camera.main;
            Vector3 ear = cam ? cam.transform.position : Vector3.zero;
            float dist = local ? 0f : Vector3.Distance(ear, pos);
            if (!local && dist > 90f) return;
            float now = Time.unscaledTime;
            if (!local)
            {
                int busy = 0;
                for (int i = 1; i < GunPool.Count; i++) if (GunEnds[i] > now) busy++;
                if (busy >= MaxRemoteGuns) return;
            }
            // slot 0 is reserved for your own gun (a new shot cuts your previous tail instead of stacking)
            int slot = 0;
            if (!local)
            {
                slot = -1;
                for (int i = 1; i < GunPool.Count; i++) if (GunEnds[i] <= now) { slot = i; break; }
                if (slot < 0) return;
            }
            var s = GunPool[slot];
            s.Stop();
            s.transform.position = pos;
            s.clip = clip;
            s.spatialBlend = local ? 0f : 0.9f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = 6f;
            s.maxDistance = 90f;
            s.dopplerLevel = 0;
            s.pitch = Random.Range(0.96f, 1.04f);
            s.volume = vol * Volume * (local ? 1f : 0.75f);
            s.GetComponent<AudioLowPassFilter>().cutoffFrequency = local ? 22000f : Mathf.Lerp(16000f, 1600f, Mathf.Clamp01(dist / 60f));
            s.Play();
            GunEnds[slot] = now + clip.length * 0.9f;
        }

        /// <summary>Finale: the music speeds up and gets louder while someone is about to win.</summary>
        public static void SetIntense(bool on)
        {
            if (!_music) return;
            _music.pitch = Mathf.MoveTowards(_music.pitch, on ? 1.14f : 1f, 0.02f);
            _music.volume = Mathf.MoveTowards(_music.volume, on ? Mathf.Min(1f, MusicVolume * 1.5f) : MusicVolume, 0.02f);
        }

        public static void SetMusic(float v)
        {
            MusicVolume = v;
            if (_music) _music.volume = _inMatch ? v * 0.35f : v;
        }

        /// <summary>Pause / resume the background music (keeps its position).</summary>
        public static void SetMusicOn(bool on)
        {
            if (!_music) return;
            if (on && !_music.isPlaying) _music.UnPause();
            if (on && !_music.isPlaying) _music.Play();
            if (!on) _music.Pause();
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
