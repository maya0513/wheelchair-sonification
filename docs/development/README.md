# 開発の入口

コードを変更し、テスト・画像確認・ビルドを行う人向け。アプリを使う場合は[導入・装着・準備](../operations/setup.md)から読む。開発とテストの考え方は[入門解説](../../research/implementation/development-and-testing-guide.md)にまとめる。

## 環境と実行入口

Unityのバージョンは[ProjectVersion.txt](../../wheelchair-sonification/ProjectSettings/ProjectVersion.txt)、依存パッケージは[manifest.json](../../wheelchair-sonification/Packages/manifest.json)と[ロック](../../wheelchair-sonification/Packages/packages-lock.json)を確認する。HTC SDKは既存スクリプトが固定URL・SHA-256で取得する。別のSDKや汎用実験エンジンは導入しない。

```sh
mise install
just check             # 文書整合・補助スクリプト。実Releaseは作らない
just unity-prepare
just unity-check
just unity-test-edit
just unity-test-play
just unity-preview
just console-preview
```

全コマンドは`just --list`、補助スクリプトの役割は[scripts/README](../../scripts/README.md)を参照する。[画像・音確認](xr-preview.md)と[署名付きAPKのビルド・公開](android-build.md)は独立した手順として読む。20分の実待機は通常テストへ入れず、`just experiment-soak`の90分継続確認は任意とする。

## 実装の境界

| 責務 | 処理 |
|---|---|
| 入力 | `IPoseSource`・`IEyeTrackingSource`。実測・合成・再生・取得不可を区別 |
| 準備 | `PreparationWorkflow`・`PreparationInference`。HMD内に一つの準備状態を保持 |
| UI | `PreparationView`とPCブラウザー。同じ状態を表示し共通処理へ操作を要求 |
| 計測・FB | `WheelSpeedEstimator`・`SpeedFeedbackEngine`。準備と研究試行で計算を共有 |
| 表示位置 | `FeedbackDisplayAnchor`。車体への相対配置と有効性 |
| 試行・時計 | HMDが記録状態、PCが正式時計を管理し、試行IDで対応付け |
| 保存・回収 | `DeviceCheckLog`・`ResearchSessionLog`・`EyeTrackingLog`・`SessionExporter`・`OperatorLink`。CSVは`CsvValue`、再生入力は`RecordedPoseSource` |

計測入力から保存までを分け、実測・合成・再生で同じ計算を通す。PCに識別・校正の別実装を作らない。HMDとPCは世代番号付き操作を使い、二重実行・古い要求を拒否し、中止を優先する。単体チェックはPCなしで動作し、本実験の通信断時は停止する。通信形式3と研究試行保存形式2は別の定義。

Unity画面は編集可能なPrefabと共通のUI生成処理を使う。準備パネルは白系背景・黒文字・灰色と青のアクセント、文字36以上・自動縮小なしを基本とする。参加者用の縦バーには[FB仕様](../specifications/feedback.md)の配色・不透明度を使う。HTMLでUnity画面を描き直さない。C#はCSharpier、補助スクリプトはPrettierで整形している。整形ツールはアプリ実行には不要。

設定・定義は[仕様一覧](../README.md#定義数値を確認する)、手元の証拠と実機未確認事項は[確認状況](../status.md)を参照する。実装の根拠は[装置比較](../../research/implementation/sensing-and-feedback-devices.md)、[XR画面と視線](../../research/implementation/xr-ui-and-eye-tracking.md)、[オフライン通信](../../research/implementation/offline-local-control.md)へ分けて記録する。
