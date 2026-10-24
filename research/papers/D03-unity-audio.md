# D03 Unity音出力の時刻・DSPバッファ

提供元：Unity Technologies。閲覧日：2026-10-14。公式技術資料であり、介入効果を証明する研究論文ではない。

- [公式資料1](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.PlayScheduled.html)
- [公式資料2](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.GetDSPBufferSize.html)

## 確認した内容

PlayScheduledはdspTime上の絶対時刻へ予約。DSPバッファは遅延と安定性に関係する。2026-10-14にUnity 6.0のページを確認。

## 本研究での利用

今回のHMD音は速度に応じた連続生成で、接触後のプリロード音は使わない。DSPバッファと出力の遅れを確認し、Update周期やAPI時刻を実際の音開始時刻と同一視しない。PC開始合図の時計とは分けて[検証](../../docs/specifications/validation.md)する。

参照：[フィードバック仕様](../../docs/specifications/feedback.md)、[EEGの品質と解釈](../topics/eeg.md)。
