# D05 Lab Streaming Layer: Time Synchronization

提供元：LSL contributors。閲覧日：2026-10-14。公式技術資料であり、介入効果を証明する研究論文ではない。

- [公式資料1](https://labstreaminglayer.readthedocs.io/info/time_synchronization.html)

## 確認した内容

サンプル時刻・時計オフセットを記録。LabRecorder/XDFと公式importerによる時刻整合を確認。

## 本研究での利用

時計の同期と装置内部の遅れは別。OSC/LSLへ送信した時刻だけで、皮膚上の取得時点や音の到達時点を保証しない。L45と併読。

参照：[装置案](../../docs/feedback-specification.md)、[EEG案](../../docs/archive/exploratory-eeg-on-hold.md)。
