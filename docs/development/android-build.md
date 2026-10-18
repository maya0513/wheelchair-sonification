# WindowsでAPKをビルドし、ゴーグルへ入れる

研究室の共用Windows PCへリポジトリをcloneし、署名付きAndroid APKを作成する手順。`just unity-build-release`を入口とする。Ubuntuでも同じコマンドを使えるが、ゴーグルの接続・導入は共用PCで行う。

**現状の中身は[模擬入力の2Dデモ](feedback-demo.md)。このコマンドの「release」はDevelopment Buildを無効にした配布用のビルド設定を指す。VR表示、パススルー、トラッカー入力や研究装置の検証が完了したという意味ではない。** 機種に対応するXR設定は未導入で、APKを作るだけでVRアプリになるわけではない。現在のデモをゴーグルの2Dアプリとして操作できるかも実機確認が必要。

## 共用PCを準備する

1. Git、Node.js 24以降、just 1.58.0以降を使用できるようにする。既存の`just setup`はLinux向けのツール導入を含むため、Windowsでこのビルドだけを行うためには実行不要。Nodeとjustは既存の導入方法またはmiseを使う。[justの導入方法](https://just.systems/man/en/packages.html)
2. Unity Hubから、[ProjectVersion.txt](../../wheelchair-sonification/ProjectSettings/ProjectVersion.txt)と同じUnity **6000.6.4f1**を導入し、ライセンスを有効にする。そのEditorへ**Android Build Support / Android SDK & NDK Tools / OpenJDK**を追加する。
3. リポジトリをcloneし、ルートへ移動する。Hubで`wheelchair-sonification/`を開き、初回のパッケージ取得を終えたらEditorを閉じる。UnityがGit依存パッケージを取得するためにもGitが必要。

VIVE HubやMCPサーバーはAPKビルドには不要。初回の依存取得や、必要なAndroid SDKの追加取得にはネットワーク接続が必要になる。

## 初回だけ署名鍵を用意する

**キーストア**は、APKが同じ開発者の更新版であることを示す署名鍵のファイル。自分用に端末へ入れる場合も使用する。このコマンドはPCごとに変わるデバッグ鍵へ自動で切り替えない。

UnityのPlayer SettingsでAndroidを選び、Publishing Settings内のKeystore Managerからキーストアと鍵を作成する。設定名・配置はUnityの画面に従う。キーストアはリポジトリの外へ置き、ファイル、鍵のAlias、キーストアのパスワード、鍵のパスワードを保管する。別のPCで更新版を作る場合にも同じ鍵を使う。[Unity：Keystore Manager](https://docs.unity3d.com/6000.6/Documentation/Manual/android-keystore-manager.html)

次のPowerShell設定は、その端末セッションだけに適用する。パスとAliasは自分の設定へ置き換える。パスワードは入力時に表示せず、コマンド履歴へ直接書かない。

```powershell
$env:UNITY_ANDROID_KEYSTORE = "D:\ResearchKeys\wheelchair-sonification.keystore"
$env:UNITY_ANDROID_KEY_ALIAS = "wheelchair-sonification"
$env:UNITY_ANDROID_KEYSTORE_PASSWORD = [System.Net.NetworkCredential]::new("", (Read-Host "キーストアのパスワード" -AsSecureString)).Password
$env:UNITY_ANDROID_KEY_ALIAS_PASSWORD = [System.Net.NetworkCredential]::new("", (Read-Host "鍵のパスワード" -AsSecureString)).Password
```

`.keystore`と`.jks`はGit管理対象から除外している。鍵やパスワードをGitHub Releaseへ添付しない。

## ビルドする

Editorを閉じ、リポジトリのルートで実行する。

```powershell
just unity-build-release
```

`just unity-build-apk`も同じ処理を行う。Windowsではcmdを介してNodeを呼び、Unity本体はシェルを介さず起動するため、Bash・WSL・Pythonを必要としない。既存のLinux用`just unity-build`は引き続きLinuxデモ用。

通常はUnity Hubの標準インストール先から、指定の版を探す。別の場所へ入れた場合だけ、実行ファイルを指定する。

```powershell
$env:UNITY_EDITOR = "D:\Unity\6000.6.4f1\Editor\Unity.exe"
just unity-build-release
```

| 項目 | このコマンドの設定 |
|---|---|
| 出力 | ARM64 / IL2CPPのAPK。AABや外部OBBに分割しない |
| 開発設定 | Development Build、Script Debugging、Profiler接続を無効化。IL2CPPのコンパイラー設定はRelease |
| 署名 | 上記の4環境変数で指定した鍵を使用。未設定ならEditor起動前に失敗 |
| アプリID | `io.github.maya0513.wheelchairsonification`。更新時も固定する |
| Scene | Editor Build Settingsで有効なScene。現在は`FeedbackDemo.unity` |
| 版 | 原則Player Settingsの値。`UNITY_BUILD_VERSION`と`UNITY_ANDROID_VERSION_CODE`でそのビルドだけ上書き可能 |
| 保存先 | `.local/builds/android-release/`。Git管理外 |
| ログ | `.local/logs/unity-release-android.log`。直前のログは`.previous` |

例として次のように版を指定できる。バージョン文字列は表示用、versionCodeはAndroidが更新を判断する整数で、配布する更新ごとに増やす。

```powershell
$env:UNITY_BUILD_VERSION = "0.1.0"
$env:UNITY_ANDROID_VERSION_CODE = "1"
just unity-build-release
```

生成物は次の3ファイル。フォントのライセンス本文は[StreamingAssets](https://docs.unity3d.com/6000.6/Documentation/Manual/StreamingAssets.html)からAPK内の`assets/Noto-OFL.txt`へ含める。

```text
.local/builds/android-release/
├── WheelchairSonification.apk
├── build-info.json
└── SHA256SUMS.txt
```

`build-info.json`にはUnity版、Scene、ビルド設定、アプリ版、模擬入力であること、Gitコミットと未コミット変更の有無、APKのSHA-256を残す。Git情報を取得できない場合は`null`になる。

今回用の一時ディレクトリでビルドし、Unityの終了コード・新しいログ・BuildReport由来の完了記録・APKの存在を確認してから保存先へ置く。失敗時に以前のAPKを今回の成功と表示しない。前回の成功品は`android-release.previous/`へ残す。一時的なPlayer SettingsはUnity側で戻し、Editor終了後にNode側でも設定ファイルの元の内容を復元する。ビルド中は同じプロジェクトを別のEditorで開かない。NodeやOSの強制終了後は、`git diff`で設定の変更を確認する。

Node側の復元対象は`ProjectSettings/ProjectSettings.asset`と、ビルド時にUnityが更新する`Assets/Settings/Mobile_RPAsset.asset`・`Assets/Settings/UniversalRenderPipelineGlobalSettings.asset`。ビルド開始前の内容を保持し、成功・失敗のどちらでもEditor終了後に戻す。

## ゴーグルへ入れる、またはReleaseで配布する

**Focus Visionの場合の公式手順**は、PCとゴーグルをUSBで接続→本体で「ファイル転送」→APKを本体のDownloadへコピー→「不明のアプリを許可する」を有効化→本体のファイルブラウザでAPKを選択、となる。機種が違えばその機種の手順を確認する。[HTC：APKのインストール](https://www.vive.com/jp/support/focusvision/category_howto/installing-apk-on-headset.html)

GitHubのReleases画面で、対応するコミットのタグを選び、上記の3ファイルを添付すれば、別のPCでは再ビルドせずAPKを取得できる。配布する際は未コミット変更のない状態でビルドし、検証した機種・OS・アプリが模擬入力版であることを説明する。

VIVE Hubの同一ネットワーク接続は、PC上で実行するアプリの映像・音を送る**ストリーミング**の経路。このAPKを本体で実行する経路とはビルド先が異なる。トラッカーがPCでしか取得できない構成なら、機器確認後にWindows用アプリとストリーミング、またはデータ通信の構成を検討する。[HTC：VIVE Streaming](https://www.vive.com/jp/support/vs/category_howto/about-vive-streaming.html)

## 型番・接続方式をどこで確認するか

まず製品名・Model表記・個数が分かればよい。シリアル番号は個体の識別番号で、型番とは異なる。

| 確認するもの | 確認場所 | 記録する内容 |
|---|---|---|
| ゴーグル | 本体の製品ラベル、箱、研究室の備品記録。Focus Visionなら本体の「設定→一般→状態→ヘッドセットの状態」も確認 | 製品名（Focus Vision / Focus 3 / XR Eliteなど）、Model表記、システム版。画面に製品名がなければラベルと照合 |
| トラッカー | 本体裏側のラベル、箱、備品記録。SteamVRやVIVE Hubで表示されるデバイス名も照合 | Tracker 3.0 / Ultimateなどの製品名、Model表記、使用できる個数 |
| 受信機と接続先 | トラッカー用USBドングルがPC・ゴーグルのどちらへ刺さっているか実物を確認 | 接続先、ドングル数、SteamVRとVIVE Hubのどちらで認識しているか |
| ベースステーション | 室内の設置機器のラベルと認識画面 | 有無、製品の世代、台数。ゴーグルとトラッカーで追跡方式が同じとは限らない |
| 共用PC | `Win + R`→`dxdiag`のシステム・ディスプレイ。VIVE HubとSteamVRのバージョン表示 | Windows版、GPU、メモリ、VIVE Hub版、SteamVR版 |

Focus Visionの上記設定画面とラベル位置は[HTC公式案内](https://www.vive.com/jp/support/focusvision/category_howto/where-to-find-headset-serial-number.html)を参照する。この案内はシリアルの所在説明なので、製品名が必ず表示されるという意味ではない。Tracker 3.0の規制上のModel表記は`20AB100`と[公式資料](https://dl.vive.com/safety-guide/vive-tracker3/tracker-3.0-safety-guide.pdf)で確認できる。外観だけで世代を判断しない。

開発者設定・USBデバッグを有効にできる機種なら、PCのADB（Androidへ接続する開発ツール）でもメーカー・モデル・Android版を取得できる。Unity同梱ADBの標準例は次のとおり。表示結果の製品コードと販売名が一致しない場合もある。[Android：ADB](https://developer.android.com/tools/adb?hl=ja)

```powershell
$adb = "C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
& $adb devices -l
& $adb shell getprop ro.product.manufacturer
& $adb shell getprop ro.product.model
& $adb shell getprop ro.build.version.release
& $adb shell getprop ro.product.cpu.abilist
```

PCと機器の接続を許可する確認がゴーグルに出たら、対象PCを確認して許可する。複数台を接続している場合は、`adb -s 対象のID shell ...`で対象を選ぶ。

この情報を基にXR用SDK、単体実行かPC実行か、トラッカー取得方法を決める。実空間の視認性、取り付け後の追跡、提示の遅延は、型番から保証せず[検証計画](../specifications/validation.md)で確かめる。

## 実装と確認の範囲

Node側の補助処理は`scripts/unity-release.mts`、Unity側の設定とBuildPipeline実行は`Assets/Experiment/Editor/ReleaseBuildTools.cs`。Windows標準インストール先、空白を含むパス、署名未設定、ロック、失敗、古い成果物、完了記録の不備、設定の復元を補助スクリプトのテストで確認する。

2026-10-18の確認では`just check`の33件が成功した。Linux上で一時的な検証用鍵を使ってAPKを生成し、ARM64のみ、非デバッグ設定、署名検証、ライセンス本文の同梱、SHA-256の一致、設定ファイルのバイト単位の復元を確認した。検証用鍵は処理終了時に削除し、成果物は通常の配布先から分けて`.local/verification/`へ保存した。**共用Windows PCでの実行と、ゴーグルへのインストール・表示・操作は未確認**。配布に使う鍵でのビルドと端末確認を別途行う。

公式資料の対象はUnity 6000.6、just Web現行資料、HTC/Android Web資料。確認日：2026-10-18。Unityのバッチ処理は[公式コマンドライン資料](https://docs.unity3d.com/6000.6/Documentation/Manual/EditorCommandLineArguments.html)、OS別シェル選択は[just公式資料](https://just.systems/man/en/configuring-the-shell.html)による。表の出力先・アプリID・署名環境変数などは本リポジトリでの設定。
