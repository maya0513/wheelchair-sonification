# D03 Unity音出力の時刻・DSPバッファ

提供元：Unity Technologies。閲覧日：2026-10-14。公式技術資料であり、介入効果を証明する研究論文ではない。

- [公式資料1](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.PlayScheduled.html)
- [公式資料2](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.GetDSPBufferSize.html)

## 確認した内容

PlayScheduledはdspTime上の絶対時刻へ予約。DSPバッファは遅延と安定性に関係する。2026-10-14にUnity 6.0のページを確認。

## 本研究での利用

完了後に分かる接触角を過去へ予約できない。プリロード音をイベント後に鳴らし実測する。Update周期や予約時刻を実際の音開始時刻と同一視しない。

参照：[装置案](../../docs/specifications/feedback.md)、[EEG案](../../docs/archive/exploratory-eeg.md)。
