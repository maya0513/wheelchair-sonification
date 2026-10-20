using WheelchairSonification.Core;

namespace WheelchairSonification.Runtime
{
    public interface IFeedbackGauge
    {
        bool Visible { get; }
        FeedbackState? Marker { get; }
        void ShowMarker(FeedbackState state);
        void HideMarker();
        void SetVisible(bool visible);
    }
}
