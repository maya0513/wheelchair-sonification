# Focus VisionへAPKを入れる・Ubuntuから公開する

VIVE Focus Vision本体で動くAndroid / OpenXRアプリを配布する。普段のUbuntuでビルドしてGitHub Releasesへ置き、共用Windows PCではダウンロードとUSBコピーだけを行う。

実施者がこのUbuntu PCのブラウザーから進行し、被験者はアプリやコントローラーを操作しない。Ultimate Tracker 5台の役割・3次元校正・接触候補推定・保存、来訪の順序と理解確認・休憩、PCへの記録回収を実装している。5台の実機性能と現地での成立性は未確認。[実施者用の起動・操作](experiment-console.md)・[残作業](remaining-work.md)

## Releasesからインストールする

1. 共用PCのブラウザーで[Releases](https://github.com/maya0513/wheelchair-sonification/releases)を開き、対象のプレリリースを選ぶ。Assetsの**WheelchairSonification.apk**をダウンロードする。Source codeのzipは不要。
2. Focus VisionとPCをUSB接続し、ゴーグル側で「ファイル転送」を選ぶ。APKを本体のDownloadフォルダーへコピーする。
3. 本体の設定で「不明のアプリを許可する」を有効にし、ストレージのファイルブラウザーからAPKを選択してインストールする。
4. ライブラリーからアプリを起動する。初回は実施者が**座った状態**で、PCとの接続、パススルー、文字、3状態と音量を確認する。被験者へゴーグル内の設定操作を求めない。

| Asset | 使う場所 |
|---|---|
| `WheelchairSonification.apk` | ゴーグルのDownloadへコピーしてインストール |
| `operator-console.zip` | 実施者用Ubuntu PCで展開し、Node.js 24で`sh start.sh` |
| `ui-preview.zip` | PCで展開し、`index.html`から被験者用・実施者用画像を確認 |
| `build-info.json` | コミット、設定、署名・静的検証、実機未確認事項を確認 |
| `SHA256SUMS.txt` | 上記4ファイルのチェックサム |

ゴーグルへZIPやビルド情報を入れる必要はない。実験時は[PCのホットスポットと操作画面](experiment-console.md)を使い、外部回線は不要。

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
just release-apk v0.3.0 3
```

引数は**タグ**と**Android versionCode**。例の次の版なら`v0.3.1 4`のようにする。コマンドは以下を確認・実行する。

1. ghの認証、origin、クリーンな作業ツリー、HEADがGitHubにあること、署名設定、タグとReleaseが未使用であることを確認する。
2. 現行の計測・進行・PC通信コードについて、90分のUnity＋PC合成入力試験が完了し、来訪完了と記録回収があることを確認する。同じコミットからUnityとChromeの画像を新しく生成する。APKを新しくビルドし、署名・更新互換性・ARM64・非デバッグ・XRと通信のManifest・必須ライブラリ・ライセンスを検証する。
3. ビルド中もソースが変わっていないことを確認し、Draft Releaseを作る。APK・操作コンソールZIP・ビルド情報・画像一覧ZIP・チェックサムをアップロードする。
4. 5ファイルの名前とサイズを確認してからプレリリースとして公開し、URLを表示する。

毎回のpushで自動公開はしない。GitHub Actionsやクラウド用のUnity認証情報は不要。既存の公開物・タグは上書きしない。

90分試験の終了を待たず先行公開すると明示的に判断した場合だけ、`WHEELCHAIR_RELEASE_SKIP_SOAK=1 just release-apk v0.3.0 3`を使う。Release本文とビルド情報へ試験未完了を記録し、成功した試験として扱わない。署名・内容・ソース一致・添付の検証は省略しない。実機の受入れ条件G1–G6も変わらない。

アップロード途中で失敗した場合はDraftを残し、ローカルの成果物の場所を表示する。[Releases](https://github.com/maya0513/wheelchair-sonification/releases)でDraftを開いて添付を確認する。未公開Draftに不足ファイルを追加してから公開するか、そのDraftを整理して別の版で再実行する。添付が揃うまで公開しない。

### 公開せずAPKだけ作る

```sh
just unity-build-release
# 同じ処理: just unity-build-apk
```

版を一時指定する場合は`UNITY_BUILD_VERSION=0.3.0 UNITY_ANDROID_VERSION_CODE=3 just unity-build-release`とする。Unityの標準配置以外の場合は`UNITY_EDITOR`へ実行ファイルの絶対パスを指定する。

| 項目 | 設定 |
|---|---|
| 対象 | VIVE Focus Vision / Android / OpenXR |
| Scene | `Assets/Scenes/FeedbackDemoXR.unity`のみ |
| 実行 | ARM64 / IL2CPP / Releaseコンパイラー設定 |
| 開発用機能 | Development Build・Script Debugging・Profiler自動接続なし |
| 生成形式 | 単一APK。AAB・OBB分割・Androidプロジェクト書き出しなし |
| アプリID | `io.github.maya0513.wheelchairsonification` |
| 描画 | OpenGLES3、Single Pass Instanced、Render Scale 1.0、MSAA 4、透明背景、HDR・後処理なし |
| XR | VIVE XR Support・Focus 3 Controller Interaction・VIVE XR Passthrough・VIVE XR Tracker |
| Android SDK | 最小29、対象36を固定。Internet・ネットワーク状態の権限とローカルHTTP設定を検査 |
| 操作 | 実施者のPCブラウザー。被験者用画面に設定・数値パッド・開始終了ボタンを出さない |
| 入力 | 既知角度の模擬提示とUltimate Trackerの明示的な実測開始。接続不良から模擬へ切り替えない。コントローラー姿勢を推進角として扱わない |

出力はGit管理外の`.local/builds/android-release/`。APKと一緒に`build-info.json`・`SHA256SUMS.txt`を生成する。公開時には`operator-console.zip`・`ui-preview.zip`とローカルの`release-notes.md`も作る。添付は上表の5ファイル。チェックサムはAPK・ビルド情報・操作コンソール・画像ZIPの4ファイル分を含む。画像ZIPを展開してindex.htmlを開けば、閲覧側にUnityは不要。[画像の用途・制約](xr-preview.md)。ログは`.local/logs/unity-release-android.log`。

`build-info.json`には対象機種、Scene、Unity・SDK版、アプリ版、模擬／実測入力の対応範囲、計測性能の未検証状態、XR・パススルー設定、ソースコミット、未コミット変更の有無、APKのSHA-256、静的検証と実機確認の状態を残す。署名パスワードは含めない。

ビルドは一時ディレクトリーで行い、今回の終了コード・ログ・完了記録・APKを検証してから保存先へ置く。失敗時に前回のAPKを今回の成功として扱わない。直前の成功品は`android-release.previous/`に残す。

一時的なPlayer SettingsはUnity側で戻し、Node側でも`ProjectSettings/ProjectSettings.asset`・`Assets/Settings/Mobile_RPAsset.asset`・`Assets/Settings/UniversalRenderPipelineGlobalSettings.asset`と、`Assets/XR/`・`Assets/XRI/`・`Assets/VIVE/`以下の設定asset、および実験Resources・TMP設定と動的フォントassetを元のバイト列へ戻す。SDKが作った空の番号付き設定フォルダーも整理する。ビルド中は同じプロジェクトを別のEditorで開かない。強制終了後は`git diff`と残ったロックを確認する。

新しく生成されたビルド識別ファイルと`Assets/Resources/PerformanceTestRun*.json`・付随するmetaも終了時に整理する。開始前から存在したファイルは保持する。プレビューの一時テスト情報を、次のAPKやGitへ残さない。

Windowsで再ビルドする必要がある場合も同じNodeコマンドを使える。Unity・Android Build Support・Node・just・署名設定・SDK取得が別途必要になるため、通常は配布済みAPKを使う。

## 機器情報と実機での確認

ゴーグルはユーザー確認により**VIVE Focus Vision**。車いす計測用は**VIVE Ultimate Tracker 5台**、ドングルと接続部品ありとのユーザー確認。5台の動作確認と現地性能は未実施。設定・開始・終了・回収は実施者のPCから操作する。被験者へコントローラーを渡さない。[接続・校正手順](tracker-measurement.md)

| 確認するもの | 場所・方法 |
|---|---|
| 本体のシステム版 | 設定→一般→状態→ヘッドセットの状態。ラベル・備品記録とも照合 |
| コントローラー | 本体OSの事前設定には実施者が使う場合がある。実験アプリの被験者操作・追跡の必須条件にはしない |
| PC通信 | ホットスポット、接続承認、全走路の疎通、ブラウザー終了・通信断からの停止・復旧 |
| 計測用トラッカー | 5台同時取得、1台ずつ動かして役割照合、再接続・遮蔽・校正、左右の傾斜軸 |
| 実表示・実音 | 両眼の日本語表示、3領域マーカー、440/660/880 Hz、快適な音量 |
| パススルー | 周囲が見えること、表示の透明部分、アプリ中断・復帰後の再生成 |
| 記録 | PCの記録ページの件数、終了・中断、PCへのZIP回収・照合、Download/WheelchairSonificationExportsへZIP書出し、Windowsへのコピーとチェックサム |

このデモでは実走行せず、座って確認する。走行中の視認性・遅延・計測精度は[本計測前の受入れ条件](../specifications/validation.md)で別途確認する。HMD追跡やフォーカスを失ったセッションは未完了として保持し、復帰だけで音やマーカーを自動再開しない。

機器のシリアル番号は型番と異なり、相談のために共有する必要はない。[HTC：本体の状態・ラベル](https://www.vive.com/jp/support/focusvision/category_howto/where-to-find-headset-serial-number.html)

## 確認状況

既存版はユーザーからFocus Visionでの起動報告がある。今回の版は、UnityとChromeの実描画画像、計算・保存・進行・通信と復元のテスト、合成入力による通し確認を行う。署名付きAPKの生成・静的検証も行い、その結果はReleaseのbuild-info.jsonへ残す。画像には仮の視野角・背景とソースコミットを記載する。テストの準備完了状態は模擬したもので、実機確認とは区別する。

**ソフト試験の証拠は[残作業表](remaining-work.md)で確認する。今回の版の本体インストール、レンズ越しの可読性、5台同時追跡、推定精度・実出力遅延、MediaStore書出しとWindowsコピー、実機での90分稼働、屋外性能は未確認。G1–G6は未達のまま。** 停止・着座で動作を確認し、[検証計画](../specifications/validation.md)に従って進める。8の字の正式な所要時間は外部動画から採点する。

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
