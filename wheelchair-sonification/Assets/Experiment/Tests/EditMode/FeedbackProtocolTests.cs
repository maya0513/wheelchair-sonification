using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WheelchairSonification.Core;

namespace WheelchairSonification.Tests
{
    public class FeedbackProtocolTests
    {
        [TestCase(60, 70)]
        [TestCase(84.9, 94.9)]
        [TestCase(85, 95)]
        [TestCase(90, 95)]
        [TestCase(94.9, 95)]
        [TestCase(95, 95)]
        [TestCase(97, 97)]
        public void TargetMatchesProtocolExamples(double baseline, double expected)
        {
            var result = FeedbackProtocol.CalculateTarget(new[] { baseline, baseline }, new[] { baseline, baseline });
            Assert.That(result.Success, Is.True);
            Assert.That(result.TargetDegrees, Is.EqualTo(expected).Within(1e-10));
        }

        [Test]
        public void BaselineUsesPooledMedianRatherThanEqualWeightingRepeatMedians()
        {
            var result = FeedbackProtocol.CalculateTarget(new[] { 10.0 }, new[] { 20.0, 60, 80, 100 });
            Assert.That(result.BaselineDegrees, Is.EqualTo(60));
            Assert.That(result.TargetDegrees, Is.EqualTo(70));
        }

        [Test]
        public void EvenMedianAndSufficientCountsAreHandled()
        {
            var result = FeedbackProtocol.CalculateTarget(new[] { 50.0 }, new[] { 60.0, 70, 80 });
            Assert.That(result.BaselineDegrees, Is.EqualTo(65));
            Assert.That(result.TargetDegrees, Is.EqualTo(75));
        }

        [Test]
        public void InsufficientOrInvalidBaselineNeverProducesATarget()
        {
            Assert.That(FeedbackProtocol.CalculateTarget(new[] { 60.0, 60, 60, 60 }, Array.Empty<double>()).Success, Is.False);
            Assert.That(FeedbackProtocol.CalculateTarget(new[] { 60.0 }, new[] { 60.0, 60 }).Success, Is.False);
            Assert.That(FeedbackProtocol.CalculateTarget(null, new[] { 60.0, 60, 60, 60 }).Success, Is.False);
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0, -1 })
                Assert.That(FeedbackProtocol.CalculateTarget(new[] { 60.0, invalid }, new[] { 60.0, 60 }).Success, Is.False);
        }

        [TestCase(64.9, FeedbackState.Short)]
        [TestCase(65, FeedbackState.Target)]
        [TestCase(70, FeedbackState.Target)]
        [TestCase(75, FeedbackState.Target)]
        [TestCase(75.1, FeedbackState.Long)]
        public void ClassificationIncludesBothBoundaries(double angle, FeedbackState expected)
        {
            Assert.That(FeedbackProtocol.Classify(angle, 70), Is.EqualTo(expected));
        }

        [Test]
        public void InvalidAnglesAreRejectedRatherThanClassified()
        {
            foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0, -1 })
                Assert.Throws<ArgumentException>(() => FeedbackProtocol.Classify(value, 70));
            Assert.Throws<ArgumentException>(() => FeedbackProtocol.Classify(70, double.NaN));
        }

        [TestCase(FeedbackState.Short, 440)]
        [TestCase(FeedbackState.Target, 660)]
        [TestCase(FeedbackState.Long, 880)]
        public void TonesHaveCorrectStereoDurationFrequencyAndFades(FeedbackState state, int expectedHz)
        {
            float[] data = ToneGenerator.CreateStereo(state);
            Assert.That(data.Length, Is.EqualTo(28800), "300 ms at 48 kHz, two identical channels");
            Assert.That(data[0], Is.Zero);
            Assert.That(data[data.Length - 1], Is.Zero);
            for (int i = 0; i < 14400; i++)
            {
                Assert.That(data[i * 2], Is.EqualTo(data[i * 2 + 1]));
                Assert.That(Math.Abs(data[i * 2]), Is.LessThanOrEqualTo(1));
            }
            // Estimate frequency independently from interpolated rising zero crossings.
            var crossings = new List<double>();
            for (int i = 481; i < 13920; i++)
            {
                float previous = data[(i - 1) * 2], current = data[i * 2];
                if (previous < 0 && current >= 0) crossings.Add(i - 1 - previous / (current - previous));
            }
            double frequency = (crossings.Count - 1) * 48000 / (crossings.Last() - crossings.First());
            Assert.That(frequency, Is.EqualTo(expectedHz).Within(0.01));
            double plateauRms = Math.Sqrt(Enumerable.Range(480, 13440).Average(i => (double)data[i * 2] * data[i * 2]));
            Assert.That(plateauRms, Is.EqualTo(0.7071).Within(0.002));
            foreach (int start in new[] { 0, 13920 })
            {
                double fadeRms = Math.Sqrt(Enumerable.Range(start, 480).Average(i => (double)data[i * 2] * data[i * 2]));
                Assert.That(fadeRms, Is.LessThan(0.45));
            }
        }
    }
}
