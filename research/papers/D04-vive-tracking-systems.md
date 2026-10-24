# D04 VIVEのトラッキング方式とUltimateの対応

提供元：HTC。閲覧日：2026-10-20。公式技術資料であり、介入効果を証明する研究論文ではない。

- [公式資料1](https://www.vive.com/us/support/ultimate-tracker/)
- [公式資料2](https://blog.vive.com/us/tracking-in-virtual-reality-and-beyond-vr-101-part-iii/)

## 確認した内容

Tracker 3.0はベースステーション方式、Ultimateはカメラによる自己追跡。Ultimateの公式サポートにはFocus Vision等の設定手順がある。

## 本研究での利用

Focus系と旧Lighthouse Trackerの直接互換を仮定しない。異なる座標系を混ぜる場合の校正は追加作業。本研究の手元の機器はFocus VisionとUltimate Tracker 5台で、ドングル・接続部品ありとのユーザー確認。これは機器情報であり、5台の動作・性能の確認結果ではない。

参照：[フィードバック仕様](../../docs/specifications/feedback.md)、[EEGの品質と解釈](../topics/eeg.md)。

## Android単体実行と制約

- [VIVE XR Trackerの公式設定](https://developer.vive.com/resources/openxr/unity/tutorials/tracker-overview/vivexrtracker/)はFocus VisionのAIOとUltimateを扱い、Androidでは最大5台。SDK 2.5.1の`ViveXRTracker`レイアウトと`Ultimate Tracker 0..4`のusageを実装根拠とする。SDKが作る連番・シリアル表現を恒久的な実機識別子とは扱わず、毎回動かして役割を照合する。
- [公式のドングル接続](https://www.vive.com/jp/support/ultimate-tracker/category_howto/attaching-dongle-to-focus-vision-or-focus-3.html)は付属ケーブルと固定部品を使用。直挿し・USBハブの自己流構成を前提にしない。
- [メーカー製品情報](https://business.vive.com/us/product/vive-ultimate-tracker/)は直射日光と低照度を推奨しない。屋外第一候補という研究側の予定から、屋外性能が保証されるとは推論しない。

SDK入力時刻はUnity Input Systemのソフトウェア時刻であり、センサー測定時刻や実提示時刻とは区別する。今回、接触区間は推定しない。スポーツ車いすのキャンバーを含む左右独立の相対回転速度の計算は本研究の設計で、メーカーによる妥当性検証ではない。[実装手順](../../docs/development/tracker-measurement.md)
