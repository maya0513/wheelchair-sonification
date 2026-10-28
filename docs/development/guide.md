# 開発の入口

Unity 6000.6.4f1、Input System 1.19.0、Test Framework 1.8.0、VIVE OpenXR 2.5.1、Unity OpenXR 1.18.0、XR Interaction Toolkit 3.4.1を維持する。SDKの取得と版固定はスクリプトで行う。

```sh
mise install
just check
just unity-prepare
just unity-check
just unity-test-edit
just unity-test-play
just unity-preview
just console-preview
```

責務は次の境界に分ける。今回の研究に集中し、汎用フレームワークや別の実験進行エンジンは導入しない。

| 責務 | 正本 |
|---|---|
| 機器取得 | IPoseSource。実測・合成・記録再生 |
| 案内・割当候補・校正・機能チェック | PreparationWorkflow / PreparationInference。HMD内に一つの状態を保持 |
| 画面 | PreparationViewとPCブラウザー。状態を表示し、共通処理へ操作を要求 |
| 計測・FB | WheelSpeedEstimator / SpeedFeedbackEngine。準備と研究試行で計算を共有 |
| 試行・正式計時 | HMDが記録状態、PCが正式時計。試行IDで対応付け |
| 保存・転送 | DeviceCheckLog / ResearchSessionLog / SessionExporter / OperatorLink |

HMDとPCは同じ世代番号付き操作を使う。同時操作と古い要求を拒否し、中止を優先する。単体チェックにはPCの生存監視を要求せず、本実験の通信断時の停止は維持する。通信形式3と研究試行保存形式2を混同しない。

計測入力、左右速度の計算、提示、保存を分離する。実測・合成・再生で共通処理を通す。Unity UIは編集可能なPrefabを使い、公式XRIのWorld Space UIを手本にする。HTMLでUnity画面を描き直さない。

[仕様](../specifications/feedback.md) → [校正](tracker-measurement.md) → [PC操作](experiment-console.md) → [検証](../specifications/validation.md)の順で読む。計測とPC時計は別の時間軸。テストは固定時計でタイマーを検証し、実時間の待機を通常チェックへ入れない。`just experiment-soak`は任意の継続試験。

画像・音見本は[プレビュー](xr-preview.md)、APKは[Android配布](android-build.md)。初心者向けのモック・単体／結合・実機試験の解説は[技術調査](../../research/implementation/development-and-testing-guide.md)。現在の証拠と未確認項目は[残作業](remaining-work.md)。

案内付き準備のUIと資料の根拠は[技術ノート](../../research/implementation/guided-preparation-ui.md)。単体チェックは機能の成立を確かめ、測定精度の検証を代替しない。
