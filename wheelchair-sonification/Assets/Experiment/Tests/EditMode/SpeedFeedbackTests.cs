using System;
using System.Linq;
using NUnit.Framework;
using WheelchairSonification.Core;
namespace WheelchairSonification.Tests
{
    public sealed class SpeedFeedbackTests
    {
        [TestCase(0,0,false)][TestCase(.02,0,false)][TestCase(.05,.05/1.5,true)][TestCase(.75,.5,true)][TestCase(1.5,1,true)][TestCase(2,1,true)][TestCase(-.75,.5,true)]
        public void MappingPreservesSignedRawAndClipsOutput(double raw,double normalized,bool active)
        {var e=new SpeedFeedbackEngine(new FeedbackSettings());var f=e.Update(0,raw,.3,true,StudyGroup.Visual,true);Assert.That(f.left.rawRps,Is.EqualTo(raw));Assert.That(f.left.normalized,Is.EqualTo(normalized).Within(1e-9));Assert.That(f.left.active,Is.EqualTo(active));Assert.That(f.right.normalized,Is.EqualTo(.2).Within(1e-9));}
        [Test] public void ExponentialPitch(){var e=new SpeedFeedbackEngine(new FeedbackSettings());var f=e.Update(0,.75,1.5,true,StudyGroup.Auditory,true);Assert.That(f.left.frequencyHz,Is.EqualTo(220*Math.Sqrt(3)).Within(1e-9));Assert.That(f.right.frequencyHz,Is.EqualTo(660).Within(1e-9));}
        [Test] public void SmoothingUsesElapsedTime(){var e=new SpeedFeedbackEngine(new FeedbackSettings());e.Update(0,0,0,true,StudyGroup.Visual,true);var f=e.Update(.05,1,1,true,StudyGroup.Visual,true);Assert.That(f.left.filteredRps,Is.EqualTo(1-Math.Exp(-1)).Within(1e-9));}
        [Test] public void HysteresisAndDuplicateTime(){var s=new FeedbackSettings{smoothingSeconds=.00001};var e=new SpeedFeedbackEngine(s);Assert.That(e.Update(0,.049,0,true,StudyGroup.Auditory,true).left.active,Is.False);Assert.That(e.Update(.01,.05,0,true,StudyGroup.Auditory,true).left.active,Is.True);Assert.That(e.Update(.02,.035,0,true,StudyGroup.Auditory,true).left.active,Is.True);Assert.That(e.Update(.02,1,0,true,StudyGroup.Auditory,true).left.filteredRps,Is.EqualTo(.035).Within(.00001));Assert.That(e.Update(.03,.029,0,true,StudyGroup.Auditory,true).left.active,Is.False);}
        [Test] public void MissingResetsBothChannels(){var e=new SpeedFeedbackEngine(new FeedbackSettings());e.Update(0,.5,.5,true,StudyGroup.Auditory,true);var f=e.Update(.01,.5,.5,false,StudyGroup.Auditory,true);Assert.That(f.enabled,Is.False);Assert.That(f.valid,Is.False);Assert.That(e.Update(.02,0,0,true,StudyGroup.Auditory,true).left.active,Is.False);}
        [Test] public void TrainingBothTasksOnly(){Assert.That(new TrialSelection{phase=StudyPhase.Training,task=StudyTask.FigureEight}.FeedbackAllowed,Is.True);Assert.That(new TrialSelection{phase=StudyPhase.RetentionAssessment,task=StudyTask.Straight}.FeedbackAllowed,Is.False);}
        [Test] public void StereoAndSilence(){var e=new SpeedFeedbackEngine(new FeedbackSettings());var f=e.Update(0,.5,0,true,StudyGroup.Auditory,true);var tone=new StereoToneGenerator();var samples=new float[96000];tone.Render(samples,2,48000,f,.1);Assert.That(samples.Where((_,i)=>i%2==1).All(v=>v==0),Is.True);Assert.That(samples.Max(v=>Math.Abs(v)),Is.InRange(.099f,.101f));tone.Render(samples,2,48000,default,.1);Assert.That(samples.Skip(2000).All(v=>v==0),Is.True);}
        [Test] public void VisualNeverProducesAudio(){var e=new SpeedFeedbackEngine(new FeedbackSettings());var f=e.Update(0,1,1,true,StudyGroup.Visual,true);var samples=new float[960];new StereoToneGenerator().Render(samples,2,48000,f,.1);Assert.That(samples.All(v=>v==0),Is.True);}
    }
}
