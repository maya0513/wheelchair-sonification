using WheelchairSonification.Core;
namespace WheelchairSonification.Runtime
{
    public interface IFeedbackGauge
    {
        bool Visible { get; }
        void SetVisible(bool visible);
        void SetValues(double left, double right);
    }
}
