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

計測入力、左右速度の計算、提示、保存を分離する。実測・合成・再生で共通処理を通す。Unity UIは編集可能なPrefabを使い、公式XRIのWorld Space UIを手本にする。HTMLでUnity画面を描き直さない。

[仕様](../specifications/feedback.md) → [校正](tracker-measurement.md) → [PC操作](experiment-console.md) → [検証](../specifications/validation.md)の順で読む。計測とPC時計は別の時間軸。テストは固定時計でタイマーを検証し、実時間の待機を通常チェックへ入れない。`just experiment-soak`は任意の継続試験。

画像・音見本は[プレビュー](xr-preview.md)、APKは[Android配布](android-build.md)。初心者向けのモック・単体／結合・実機試験の解説は[技術調査](../../research/implementation/development-and-testing-guide.md)。現在の証拠と未確認項目は[残作業](remaining-work.md)。
