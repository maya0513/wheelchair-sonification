using UnityEngine;
using WheelchairSonification.Core;
namespace WheelchairSonification.Runtime
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class UnityFeedbackOutput : MonoBehaviour
    {
        sealed class AudioState { public BilateralFeedback value; public double volume; }
        volatile AudioState state = new AudioState();
        readonly StereoToneGenerator generator = new StereoToneGenerator();
        IFeedbackGauge gauge; int sampleRate = 48000;
        public float Volume { get; set; } = .1f;
        public BilateralFeedback Last { get; private set; }
        public void BindGauge(IFeedbackGauge value) { gauge = value; Stop(); }
        void Awake()
        {
            sampleRate = AudioSettings.outputSampleRate;
            var source = GetComponent<AudioSource>(); source.playOnAwake = false; source.loop = true;
            source.spatialBlend = 0; source.panStereo = 0; source.dopplerLevel = 0; source.reverbZoneMix = 0;
            source.bypassEffects = source.bypassListenerEffects = source.bypassReverbZones = true;
            // A silent carrier keeps the callback active. Samples are produced by the shared generator.
            source.clip = AudioClip.Create("Continuous stereo carrier", sampleRate, 2, sampleRate, false);
            source.Play();
        }
        public void Apply(BilateralFeedback value)
        {
            Last = value; state = new AudioState { value = value, volume = Volume };
            bool visible = value.enabled && value.valid && value.group == StudyGroup.Visual;
            gauge?.SetValues(visible ? value.left.normalized : 0, visible ? value.right.normalized : 0); gauge?.SetVisible(visible);
        }
        public void Stop() => Apply(default);
        void OnAudioFilterRead(float[] data, int channels) { var s = state; generator.Render(data, channels, sampleRate, s.value, s.volume); }
        void OnDisable() { Stop(); }
    }
}
