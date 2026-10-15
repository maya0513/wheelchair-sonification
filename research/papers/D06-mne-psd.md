# D06 MNE-Python: Welch PSDと不良区間注釈

提供元：MNE contributors。閲覧日：2026-10-14。公式技術資料であり、介入効果を証明する研究論文ではない。

- [公式資料1](https://mne.tools/stable/generated/mne.time_frequency.psd_array_welch.html)
- [公式資料2](https://mne.tools/stable/auto_tutorials/preprocessing/20_rejecting_bad_data.html)

## 確認した内容

Welchの窓長・FFT・重なりを指定できる。不良区間はBADで始まる注釈として残し、除外に利用できる。

## 本研究での利用

単位はV/µVを明記し、振幅閾値の単位を揃える。解析環境のバージョンを固定。MNE導入自体は少数電極の妥当性や自動ノイズ除去を保証しない。

参照：[フィードバック仕様](../../docs/specifications/feedback.md)、[EEGの品質と解釈](../topics/eeg.md)。
