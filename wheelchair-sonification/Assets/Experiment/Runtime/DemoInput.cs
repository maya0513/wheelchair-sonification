using System;
using System.Diagnostics;
using WheelchairSonification.Core;

namespace WheelchairSonification.Runtime
{
    public sealed class MonotonicClock : IClock
    {
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        public string OriginUtc { get; } = DateTime.UtcNow.ToString("O");
        public double Now => stopwatch.Elapsed.TotalSeconds;
    }

    public sealed class DemoInput : IExperimentInput
    {
        private readonly IClock clock;
        public event Action<ExperimentInput> Received;
        public DemoInput(IClock clock) { this.clock = clock; }
        public void Contact() => Received?.Invoke(ExperimentInput.Contact(clock.Now));
        public void Release(double angle) => Received?.Invoke(ExperimentInput.Release(clock.Now, angle));
        public void ChangeContext(ExperimentContext context) =>
            Received?.Invoke(ExperimentInput.ChangeContext(clock.Now, context));
    }
}
