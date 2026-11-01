# D10｜Getting The Data of Eye Tracker (Beta)

提供元：HTC。確認日：2026-11-02。

出典：[Getting The Data of Eye Tracker (Beta)](https://developer.vive.com/resources/openxr/unity/tutorials/face-data/getting-the-data-of-eye-tracker/)。公開HTMLの該当節と、D10は使用中SDK 2.5.1のソースを確認。

Focus Vision AIOはVIVE OpenXR 2.5.0以降の対応対象。現行2.5.1の公開APIとソースも照合し、視線・瞳孔・眼の開きの有効性を分けて保存する。SDK配列APIで非公開の時刻をセンサー時刻として補完しない。

採用する条件は[FB仕様](../../docs/specifications/feedback.md)、取得項目と時刻は[保存形式](../../docs/specifications/recording.md)を参照。
