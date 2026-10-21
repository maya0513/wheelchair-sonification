# 模擬入力版を動かす

本資料はデスクトップの模擬入力版の説明である。Focus Visionの現行操作は[PCコンソール](experiment-console.md)を使い、被験者へ設定操作を求めない。

既知の接触開始・離手・角度を手で入力し、個人目標の算定、3状態判定、音・表示、記録を確認するアプリ。このページのデスクトップ版は実測を扱わない。Focus Vision版の実測は[機器・校正](tracker-measurement.md)と[実測記録](research-records.md)を参照する。提示の規則は[フィードバック仕様](../specifications/feedback.md)を参照する。

## 起動する

リポジトリのルートで実行する。Unity Editorは[プロジェクト指定の版](../../wheelchair-sonification/ProjectSettings/ProjectVersion.txt)とLinux Build Supportを使用する。

```sh
just unity-open
```

Editorのメニューから **Experiment → Open feedback demo** を選び、Playボタンを押す。直接開く場合は`Assets/Scenes/FeedbackDemo.unity`を使う。

Linuxアプリとして動かす場合は、Editorを閉じてビルドする。

```sh
just unity-build
.local/builds/feedback-demo/FeedbackDemo.x86_64
```

生成物はGit管理外の`.local/builds/feedback-demo/`に置く。同梱フォントのライセンスは、その中の`ThirdPartyNotices/Noto-OFL.txt`に含める。Unityの操作にはMCPサーバーの起動・導入を必要としない。

Focus Visionでは[ReleasesからAPKを入れる](android-build.md#releasesからインストールする)手順を使う。PC側の再ビルドは不要。XR版には模擬提示とUltimate Tracker計測の入口があり、以下のデスクトップ版は模擬イベントの検証に使う。

## Focus Visionで操作する

[実施者用の起動・操作](experiment-console.md)に従う。起動は無提示で、HMDに操作パネルを出さない。全体の課題順・反復・理解確認・休憩はプログラムが管理し、実施者がPCで準備と安全を確認して進める。被験者はコントローラーを使わない。

[画像確認](xr-preview.md)、[取付・校正](tracker-measurement.md)、[記録と回収](research-records.md)を参照する。以下の「基本操作」はデスクトップの計算・提示確認用で、被験者の手順ではない。旧HMDパネルはEditorの開発確認用として残し、通常のAndroid起動では表示しない。

## 基本操作

1. 「セッション設定」で事前2反復の有効角をカンマ区切りで入力する。各反復1本以上・合計4本以上が必要。正の有限値だけを受け付け、無効値を黙って除外しない。初期例の各`60, 60`では目標70°になる。
2. 群と音量を選び、「セッション開始」を押す。目標・群・音量は終了まで固定される。開始時は準備・無提示で、前のセッションの課題や接触状態は引き継がない。
3. 「課題・計測状態」で時点を「訓練」、課題を「10 m直進」、動作を「直進」にし、「中央2–8 m内」「追跡が有効」をオンにする。
4. 角度を入力し、「接触開始」→「離手・角度確定」の順に押す。初期目標なら60°・70°・80°でそれぞれSHORT・TARGET・LONGを確認できる。A群は音、V群は画面下部のマーカー、N群は無提示となる。
5. プッシュ途中で追跡をオフにして戻す、中央区間外へ切り替える、評価や8の字へ切り替えるなどして、無効判定と取消を確認する。不正角の検証には角度欄へ`NaN`も入力できる。
6. 「記録を終了」を押して保存を閉じる。次のセッションでは群などを変更できる。

操作パネルは右上のボタンまたは**F1**で隠せる。再表示もF1を使う。参加者用プレビューのCanvasは操作パネルと独立し、角度数値・履歴・操作欄を含まない。プレビューはデスクトップ画面であり、実空間の視界やHMD表示を再現したものではない。

## 記録を確認する

保存先は画面に表示する。標準では`Application.persistentDataPath`配下の`DemoSessions/`を使い、セッションごとにUTC時刻とランダムIDで新しいディレクトリを作る。別の保存先を指定する場合は、アプリ起動時に`--demo-log-root /絶対パス`を渡す。

| ファイル | 内容 |
|---|---|
| `session.json` | 模擬入力の明示、基準角、固定目標、群、デジタル音量、提示設定、時計の起点、Unity版、ビルドGUID |
| `events.csv` | 接触・離手・状態変更、角度と判定、提示依頼、ソフト側の開始終了、取消・無効理由 |
| `completed.json` | 記録を正常終了した印。個々のプッシュの成立性はイベント記録で別に判断する |

CSVはUTF-8、角度は度、時刻は秒、小数点は`.`とする。該当しない値は空欄で、不正入力の`NaN`等はその値を残す。主な列の意味は以下のとおり。

| 列 | 意味 |
|---|---|
| `sequence` | セッション内の連番 |
| `software_time_s` | 単調時計で測った処理・記録時刻 |
| `input_time_s` | 模擬入力を作った時刻。同じ時計を使用し、センサー測定時刻とは呼ばない |
| `event`, `stroke_id` | イベントの種類と対応するプッシュ |
| `angle_degrees`, `state`, `reason` | 入力角、SHORT／TARGET／LONG、取消や無効の理由 |
| `group`, `phase`, `task`, `motion`, `in_central_area`, `tracking_valid` | その記録時点の条件 |
| `audio_dsp_time_s` | 音の再生指示時に読んだ音声時計。画面やスピーカーの実出力時刻ではない |

書き出しは通常0.5秒間隔で行い、正常終了時にも書き出す。保存失敗時は提示と試行を止め、操作欄へエラーを表示する。正常終了の印がない記録は次回起動時に未完了として数え、保持する。ウィンドウを閉じる、Playを止める、強制終了する等の中断では正常終了の印を付けない。異常終了時の未書き出し分やOS側の永続化は保証しない。

## 自動テストとビルドの確認

Editorを閉じ、次を実行する。補助スクリプトのテストにはNodeとPython、Unityテストの結果検査にはPythonを使う。これらは既存のmise設定から解決する。

```sh
just check
just unity-test
just unity-build
```

`just unity-test`はEdit Mode、Play Modeを順に実行し、結果を`.local/logs/unity-test-edit.xml`と`unity-test-play.xml`へ保存する。終了コードに加えてXMLを検査し、失敗、結果なし、0件、件数不一致を成功扱いにしない。

| 確認対象 | この実装で確認する範囲 |
|---|---|
| T01・T02 | 目標算定、中央値と基準量、両端を含む境界判定 |
| T05–T11 | 模擬イベントによる欠損・無提示、群別提示、時間・取消・置換のソフト側の動作 |
| T12 | 固定した入力イベントと時計での再現性。実測位置姿勢の記録再生は含まない |
| T14・T15 | 保存失敗時の停止、書き出した記録の保持、未完了の識別、上書き防止 |
| T16 | 複数セッションの初期化と90分を超える模擬時計。実時間の耐久・メモリ・電池試験は別途必要 |

テストIDの定義は[テストケース一覧](test-cases.md)に置く。T03・T04の車輪回転・校正、T13の実機IDと再接続、実動作の検出精度、物理的な出力遅延はこの模擬入力版の確認対象に含まれない。

Development Buildには、起動時だけ明示的に有効にできる確認機能もある。

```sh
.local/builds/feedback-demo/FeedbackDemo.x86_64 \
  --demo-smoke-output "$PWD/.local/demo-verification"
```

この機能は3群の操作を自動で行い、画面PNG、ソフト側の音声出力サンプル、セッション記録、`smoke-result.json`を出力して終了する。音声は`AudioSource.GetOutputData`で確認し、スピーカーやヘッドホンの実出力を測ったとは扱わない。この関数は初回呼出しから履歴を蓄積するため、取得を開始してからサンプルが届くのを待つ。低遅延の時刻測定には使わない。[Unity公式の説明](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/AudioSource.GetOutputData.html)

画面はPNGを目視して日本語・配置・マーカーを確認する。通常起動では自動操作を行わない。

## 実装の配置

`Assets/Experiment/Core/`に目標算定・3状態・提示制御を置き、Unityを参照しない。`Runtime/`に模擬入力、単調時計、操作画面、音・表示、ファイル保存を置く。入力・時計・提示・ログはインターフェースで差し替える。`Tests/`で計算とUnity統合を分け、`Editor/`で専用Sceneの作成とLinuxビルドを扱う。

実機の精度や実出力の成立性は[検証計画](../specifications/validation.md)に従って別に確認する。
