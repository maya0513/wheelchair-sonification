using System;
using UnityEngine;
using WheelchairSonification.Core;

namespace WheelchairSonification.Runtime
{
    public sealed class UnityFeedbackOutput : MonoBehaviour, IFeedbackOutput
    {
        private readonly AudioClip[] clips = new AudioClip[3];
        private AudioSource source;
        private IFeedbackGauge gauge;
        public AudioSource AudioSource => source;
        public AudioClip Clip(FeedbackState state) => clips[(int)state];

        public void BindGauge(IFeedbackGauge value)
        {
            Stop();
            gauge?.SetVisible(false);
            gauge = value;
            gauge.SetVisible(false);
        }

        public void Initialize(GaugeView gaugeView)
        {
            gauge = gaugeView;
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0;
            source.panStereo = 0;
            source.dopplerLevel = 0;
            source.reverbZoneMix = 0;
            source.bypassEffects = true;
            source.bypassListenerEffects = true;
            source.bypassReverbZones = true;
            source.pitch = 1;
            source.volume = 0.1f;
            foreach (FeedbackState state in Enum.GetValues(typeof(FeedbackState)))
            {
                float[] samples = ToneGenerator.CreateStereo(state);
                var clip = AudioClip.Create(state.ToString(), samples.Length / FeedbackProtocol.Channels,
                    FeedbackProtocol.Channels, FeedbackProtocol.SampleRate, false);
                if (!clip.SetData(samples, 0)) throw new InvalidOperationException("音データを初期化できませんでした。");
                clips[(int)state] = clip;
            }
            SetGaugeVisible(false);
        }

        public void SetVolume(float volume)
        {
            if (float.IsNaN(volume) || float.IsInfinity(volume) || volume < 0 || volume > 1)
                throw new ArgumentOutOfRangeException(nameof(volume));
            source.volume = volume;
        }

        public double? Begin(StudyGroup group, FeedbackState state)
        {
            Stop();
            if (group == StudyGroup.Auditory)
            {
                source.clip = clips[(int)state];
                double dspTime = AudioSettings.dspTime;
                source.Play();
                return dspTime;
            }
            if (group == StudyGroup.Visual) gauge.ShowMarker(state);
            return null;
        }

        public void Stop()
        {
            if (source != null) source.Stop();
            gauge?.HideMarker();
        }

        public void SetGaugeVisible(bool visible) => gauge?.SetVisible(visible);

        private void OnDestroy()
        {
            Stop();
            foreach (var clip in clips)
                if (clip != null) Destroy(clip);
        }
    }
}
