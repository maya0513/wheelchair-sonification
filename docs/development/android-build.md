# Focus VisionへAPKを入れる・Ubuntuから公開する

VIVE Focus Vision本体で動くAndroid / OpenXR版の模擬入力デモを配布する。普段のUbuntuでビルドしてGitHub Releasesへ置き、共用Windows PCではダウンロードとUSBコピーだけを行う。

実トラッカーの位置姿勢、接触検出、推定接触角の計測は含まない。研究実験に使える測定装置としての検証は別途必要。[模擬入力版の操作](feedback-demo.md)・[検証計画](../specifications/validation.md)

## Releasesからインストールする

1. 共用PCのブラウザーで[Releases](https://github.com/maya0513/wheelchair-sonification/releases)を開き、対象のプレリリースを選ぶ。Assetsの**WheelchairSonification.apk**をダウンロードする。Source codeのzipは不要。
2. Focus VisionとPCをUSB接続し、ゴーグル側で「ファイル転送」を選ぶ。APKを本体のDownloadフォルダーへコピーする。
3. 本体の設定で「不明のアプリを許可する」を有効にし、ストレージのファイルブラウザーからAPKを選択してインストールする。
4. ライブラリーからアプリを起動する。初回は**座った状態**でパススルー、文字、コントローラー操作、音量を確認する。

機器への導入は[HTC公式手順](https://www.vive.com/jp/support/focusvision/category_howto/installing-apk-on-headset.html)に従う。共用PCへのUnity・Node・just・VIVE Hubの追加導入、USBデバッグ、PCストリーミングはこの経路では不要。本体の初回設定・コントローラーのペアリングは済んでいる前提。

更新版は同じアプリID・署名鍵を使用する。署名が異なる場合にアンインストールで回避すると端末内の記録を失うことがあるため、まず配布元の鍵と版を確認する。versionCodeは更新のたびに増やす。

**実機で確認していない項目はRelease本文とbuild-info.jsonに残す。** APKを生成・検証できたことだけで、パススルーや操作の成立を確認済みとはしない。

## Ubuntuで初回だけ準備する

Unity Hubで**6000.6.4f1**を導入し、ライセンスを有効にする。同じEditorにAndroid Build Support、Android SDK & NDK Tools、OpenJDKを追加する。Git、Node.js 24以降、just 1.58.0以降、GitHub CLI（gh）を使用する。

```sh
gh auth login
just unity-prepare
just android-signing-init
```

`unity-prepare`はHTC OpenXR 2.5.1の公式配布アーカイブを取得し、固定したSHA-256と照合する。初回は約378 MBを取得する。保存先はGit管理外の`.local/unity-packages/`で、以後は一致するキャッシュを再利用する。取得失敗・不一致のファイルをUnityへ渡さない。GitソースのアーカイブにはLFSポインターが含まれるため、公式の配布用tgzを使う。利用者がGit LFSを設定する必要はない。

EditorをHubから直接開く場合も、先に`just unity-prepare`を実行する。`unity-open`・`unity-check`・`unity-test`・`unity-build`・`unity-build-release`からは自動で準備する。初回取得にはネットワークが必要。プロジェクトで保存済みのXR設定とSceneを使い、SDKの手動インポートは行わない。

### 署名鍵を保存する

`android-signing-init`はUnity同梱のkeytoolで、更新版にも使う配布用のRSA鍵を作成する。既存の鍵・設定があれば停止し、上書きしない。

| ファイル | 用途 |
|---|---|
| `.local/signing/wheelchair-sonification.keystore` | APKに署名する秘密鍵 |
| `.local/android-signing.json` | 鍵の場所・Alias・ランダム生成したパスワード |

**両ファイルを安全な場所へバックアップする。** このリポジトリをcloneし直しても鍵は復元されない。Ubuntuでは鍵ディレクトリーを所有者のみアクセス可、鍵・設定を所有者のみ読み書き可にする。設定には平文のパスワードを含むため、チャット・GitHub・Releaseへ添付しない。コマンドはパスワードを表示せず、keytoolには環境変数で渡す。

既存の鍵を使う場合は次の4環境変数を設定する。1つでも設定されていれば環境変数の組を優先し、ローカル設定の別の鍵と混ぜない。

- `UNITY_ANDROID_KEYSTORE`
- `UNITY_ANDROID_KEY_ALIAS`
- `UNITY_ANDROID_KEYSTORE_PASSWORD`
- `UNITY_ANDROID_KEY_ALIAS_PASSWORD`

## ビルド・公開する

Editorを閉じ、変更を確認してcommit・pushした状態で実行する。公開先は`maya0513/wheelchair-sonification`に固定している。

```sh
just release-apk v0.2.0 2
```

引数は**タグ**と**Android versionCode**。例の次の版なら`v0.2.1 3`のようにする。コマンドは以下を確認・実行する。

1. ghの認証、origin、クリーンな作業ツリー、HEADがGitHubにあること、署名設定、タグとReleaseが未使用であることを確認する。
2. 対象版のAPKを新しくビルドし、署名・ARM64・非デバッグ設定・XR Manifest・必要なネイティブライブラリ・ライセンス同梱を検証する。
3. ビルド中もソースが変わっていないことを確認し、Draft Releaseを作る。APK・ビルド情報・チェックサムをアップロードする。
4. 3ファイルの名前とサイズを確認してからプレリリースとして公開し、URLを表示する。

毎回のpushで自動公開はしない。GitHub Actionsやクラウド用のUnity認証情報は不要。既存の公開物・タグは上書きしない。

アップロード途中で失敗した場合はDraftを残し、ローカルの成果物の場所を表示する。[Releases](https://github.com/maya0513/wheelchair-sonification/releases)でDraftを開いて添付を確認する。未公開Draftに不足ファイルを追加してから公開するか、そのDraftを整理して別の版で再実行する。添付が揃うまで公開しない。

### 公開せずAPKだけ作る

```sh
just unity-build-release
# 同じ処理: just unity-build-apk
```

版を一時指定する場合は`UNITY_BUILD_VERSION=0.2.0 UNITY_ANDROID_VERSION_CODE=2 just unity-build-release`とする。Unityの標準配置以外の場合は`UNITY_EDITOR`へ実行ファイルの絶対パスを指定する。

| 項目 | 設定 |
|---|---|
| 対象 | VIVE Focus Vision / Android / OpenXR |
| Scene | `Assets/Scenes/FeedbackDemoXR.unity`のみ |
| 実行 | ARM64 / IL2CPP / Releaseコンパイラー設定 |
| 開発用機能 | Development Build・Script Debugging・Profiler自動接続なし |
| 生成形式 | 単一APK。AAB・OBB分割・Androidプロジェクト書き出しなし |
| アプリID | `io.github.maya0513.wheelchairsonification` |
| 描画 | OpenGLES3、Single Pass Instanced、透明背景、HDRなし |
| XR | VIVE XR Support・Focus 3 Controller Interaction・VIVE XR Passthrough |
| 入力 | 既知角度の模擬イベント。コントローラー姿勢を推進角として扱わない |

出力はGit管理外の`.local/builds/android-release/`。APKと一緒に`build-info.json`・`SHA256SUMS.txt`を生成する。公開時にはローカルの`release-notes.md`も作るが、添付は3ファイルのみ。ログは`.local/logs/unity-release-android.log`。

`build-info.json`には対象機種、Scene、Unity・SDK版、アプリ版、模擬入力、XR・パススルー設定、ソースコミット、未コミット変更の有無、APKのSHA-256、静的検証と実機確認の状態を残す。署名パスワードは含めない。

ビルドは一時ディレクトリーで行い、今回の終了コード・ログ・完了記録・APKを検証してから保存先へ置く。失敗時に前回のAPKを今回の成功として扱わない。直前の成功品は`android-release.previous/`に残す。

一時的なPlayer SettingsはUnity側で戻し、Node側でも`ProjectSettings/ProjectSettings.asset`・`Assets/Settings/Mobile_RPAsset.asset`・`Assets/Settings/UniversalRenderPipelineGlobalSettings.asset`と、`Assets/XR/`・`Assets/XRI/`・`Assets/VIVE/`以下の設定assetを元のバイト列へ戻す。SDKが作った空の番号付き設定フォルダーも整理する。ビルド中は同じプロジェクトを別のEditorで開かない。強制終了後は`git diff`と残ったロックを確認する。

Windowsで再ビルドする必要がある場合も同じNodeコマンドを使える。Unity・Android Build Support・Node・just・署名設定・SDK取得が別途必要になるため、通常は配布済みAPKを使う。

## 機器情報と実機での確認

ゴーグルはユーザー確認により**VIVE Focus Vision**。車いす計測に使うトラッカーの製品名・型番・個数・接続先は未確認。「付属の手持ち機器」は今回のUI操作に使うコントローラーと、独立した計測用トラッカーを区別して確認する。

| 確認するもの | 場所・方法 |
|---|---|
| 本体のシステム版 | 設定→一般→状態→ヘッドセットの状態。ラベル・備品記録とも照合 |
| コントローラー | 本体の接続状態、左右のポインターとトリガー、A/X・B/Yの操作 |
| 計測用トラッカー | 本体・箱のModel表記、個数、ドングルの接続先。コントローラーと混同しない |
| 実表示・実音 | 両眼の日本語表示、3領域マーカー、440/660/880 Hz、快適な音量 |
| パススルー | 周囲が見えること、表示の透明部分、アプリ中断・復帰後の再生成 |
| 記録 | 詳細画面の保存先と、終了・中断後の記録。初期保存先はアプリ専用領域 |

このデモでは実走行せず、座って確認する。走行中の視認性・遅延・計測精度は[本計測前の受入れ条件](../specifications/validation.md)で別途確認する。HMD追跡やフォーカスを失ったセッションは未完了として保持し、復帰だけで音やマーカーを自動再開しない。

機器のシリアル番号は型番と異なり、相談のために共有する必要はない。[HTC：本体の状態・ラベル](https://www.vive.com/jp/support/focusvision/category_howto/where-to-find-headset-serial-number.html)

## 確認状況

Ubuntuで署名付きAPKを生成し、ARM64専用・非デバッグ設定・署名・OpenXRのManifestとネイティブライブラリ・ライセンス同梱を確認した。補助スクリプト44件、Unity Edit Mode 47件、Play Mode 14件のテストが成功し、既存のLinuxデスクトップ版もビルドできた。テスト内の準備完了状態は模擬したもので、実機の動作確認ではない。

Focus Visionへのインストール、実際の両眼表示・パススルー・コントローラー操作・実音・復帰は未確認。共用Windows PCでの再ビルドも未確認。まずReleasesのAPKを本体へ導入して確認する。

## 実装と出典

SDK取得は`scripts/unity-xr-sdk.mts`、署名設定は`scripts/android-signing.mts`、ビルドは`scripts/unity-release.mts`、APK静的検証は`scripts/android-apk-validation.mts`、公開は`scripts/github-apk-release.mts`。自動テストでは実際のGitHub Releaseを作らない。

XRのLoaderはAndroidだけに設定し、Sceneで入力Actionを作成・有効化してから初期化する。デスクトップ版のSceneと既存の単体・統合テストを残す。SDKのバージョン固定はメーカーによる本プロジェクトの動作保証を意味せず、Unityコンパイル・実APK・端末の確認を分ける。

以下は2026-10-20に確認した公式資料。本資料内のアプリID・コマンド・数値入力・公開時の検査は本プロジェクトの設計。

- [HTC：VIVE OpenXR 2.5.1配布](https://github.com/ViveSoftware/VIVE-OpenXR-Unity/releases/tag/versions/2.5.1)
- [HTC：OpenXR初期設定](https://developer.vive.com/resources/openxr/unity/tutorials/setup-and-installation/getting-started-with-openxr/)・[Passthrough](https://developer.vive.com/resources/openxr/unity/tutorials/passthrough/)
- [Unity：XR Interaction Toolkit 3.4のUI設定](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@3.4/manual/ui-setup.html)
- [Unity：XR Management 4.5](https://docs.unity3d.com/Packages/com.unity.xr.management@4.5/manual/index.html)
- [Unity 6000.6：コマンドライン](https://docs.unity3d.com/6000.6/Documentation/Manual/EditorCommandLineArguments.html)・[Keystore Manager](https://docs.unity3d.com/6000.6/Documentation/Manual/android-keystore-manager.html)
- [GitHub CLI：Release作成](https://cli.github.com/manual/gh_release_create)・[添付](https://cli.github.com/manual/gh_release_upload)
