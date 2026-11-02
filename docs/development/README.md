# 開発の入口

コードを変更し、テスト・画像確認・ビルドを行う人向け。アプリを使う場合は[導入・装着・準備](../operations/setup.md)から読む。開発とテストの考え方は[入門解説](../../research/implementation/development-and-testing-guide.md)にまとめる。

## 環境と実行入口

Unityのバージョンは[ProjectVersion.txt](../../wheelchair-sonification/ProjectSettings/ProjectVersion.txt)、依存パッケージは[manifest.json](../../wheelchair-sonification/Packages/manifest.json)と[ロック](../../wheelchair-sonification/Packages/packages-lock.json)を確認する。HTC SDKは既存スクリプトが固定URL・SHA-256で取得する。別のSDKや汎用実験エンジンは導入しない。

```sh
mise install --locked      # just未導入の場合
just setup
just dev check             # 文書整合・補助スクリプト。実Releaseは作らない
just dev unity-check
just dev unity-test-edit
just dev unity-test-play
just dev unity-preview
just dev console-preview
```

実施者向けの入口は[ルートjustfile](../../justfile)の`setup`・`update`・`start`だけにする。開発・検証・ビルド・公開は[tasks/dev.just](../../tasks/dev.just)へまとめ、`just dev`または`just --list dev`で一覧を表示する。サブディレクトリから実行しても、コマンドはリポジトリ直下で動く。

[画像・音確認](xr-preview.md)と[署名付きAPKのビルド・公開](android-build.md)は独立した手順として読む。20分の実待機は通常テストへ入れず、`just dev experiment-soak`の90分継続確認は任意とする。実Release作成・90分確認・機器設定の変更を`check`の依存先にしない。補助スクリプトの役割は[scripts/README](../../scripts/README.md)を参照する。

## ツールとスキルの管理

`just setup`は[mise.toml](../../mise.toml)の全ツールをmise.lockに従って導入し、Unity側のMCPパッケージ指定をサーバーと同じリリースへ揃え、固定版のHTC SDKを取得・検証する。コマンドにツール名を列挙せず、ツールの追加・削除はmise.tomlで管理する。初回の準備と更新にはインターネット接続が必要。Unity EditorとAndroid Build Supportの導入は[ビルド手順](android-build.md)に従う。通常の実行もmiseのロックされた環境を使う。

`just update`は全ツールの要求バージョンを再解決して環境を準備し、このプロジェクトで管理する全スキルを更新する。更新後はmise.lock・`.mise/locks/`・UnityのPackages/・`.agents/skills/`・skills-lock.jsonの差分を確認する。Unity EditorとVIVE SDKの固定バージョンはこの操作では変更しない。

`pypi:mcpforunityserver`は`latest`を指定し、更新時点の最新安定版をロックする。`just setup`がUnityのmanifest.jsonを同じ版のGitタグへ揃え、Unityの次回起動でpackages-lock.jsonを更新する。更新後は`just dev unity-check`と`just dev unity-mcp`でコンパイル・接続・簡単な操作を確認する。[公式の版指定処理](https://github.com/CoplayDev/unity-mcp/blob/main/MCPForUnity/Editor/Helpers/AssetPathUtility.cs)

yomiyasuは[.agents/skills/yomiyasu/](../../.agents/skills/yomiyasu/SKILL.md)を使用する。更新用CLIの`npm:skills`は`latest`を指定し、`just update`で最新版へ解決する。セットアップと通常実行はmise.lockの解決済みバージョンを使う。別のリポジトリcloneは置かない。スキルの更新は文書の推敲を実行する操作ではない。[公式の更新オプション](https://github.com/vercel-labs/skills/blob/main/docs/commands.md#skills-update)

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
