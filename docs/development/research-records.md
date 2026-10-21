# 記録の形式・保存・取り出し

実験手順の[記録項目](../operations/record-forms.md)を、アプリのファイルへ対応付ける。実測取得、実出力、現地回収の確認状況は[残作業表](remaining-work.md)で管理する。

## 保存先と対応

ゴーグルの`Application.persistentDataPath`を基点に保存する。通常のAndroidでは`/storage/emulated/0/Android/data/io.github.maya0513.wheelchairsonification/files/`に相当するが、端末のAPIが返す値を使う。WindowsからAndroid/dataを直接操作する手順にはしない。[Unity公式](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/Application-persistentDataPath.html)

```text
アプリ専用領域/
├── ResearchVisits/<visit_id>/
│   ├── visit.json
│   ├── operator-events.jsonl
│   └── questionnaire.json       # 回答または省略理由を保存した場合
├── ResearchSessions/<UTC時刻＋ランダムID>/
│   ├── session.json
│   ├── calibration.json
│   ├── poses.csv
│   ├── events.csv
│   ├── trials.csv
│   ├── diagnostics.csv
│   └── completed.json           # 正常に記録を閉じた場合だけ
├── DemoSessions/                # 既知角度の機器確認・理解確認の模擬イベント
└── ExportStaging/               # 回収用ZIPの一時領域
```

`ResearchSessions`の1ディレクトリーは1試行。`participant_id`は研究用ID、`visit_id`は来訪、`trial_id`は試行で、ディレクトリー名と試行IDは同一とは限らない。予定反復と実施順を分け、再試行は`replaces_trial_id`で元の試行へ対応付ける。

**合成位置姿勢の技術試験もResearchSessions形式を使う。** `input_source=synthetic_pose_fixture`を実測へ混ぜない。実機は`vive_ultimate_tracker`、既知角度の模擬提示は`simulated_angle_events`として区別する。入力不良から自動で合成入力へ変わることはない。

## 来訪単位の記録

| ファイル | 内容 |
|---|---|
| `visit.json` | 手順・反復・経過時間・状態・群・主要側・音量・目標成立、再試行回数、試行対応、処理済み命令、理解確認の問題・口頭回答・正答・タイムアウト・所要時間、基準2反復とその測定完了フラグ、入力方式、ソース識別 |
| `operator-events.jsonl` | 受理した操作、UTCと来訪経過時間、操作識別子・対象状態・入力内容。質問紙の回答操作も対応付ける |
| `questionnaire.json` | TLX6尺度、不快・疲労、自由記述。`present`で回答の有無を区別し、未回答の数値0を回答値とみなさない。省略時は`missing`と理由を保存 |

JSONの空配列だけから「基準測定済み」と判定しない。測定完了フラグを保存し、未測定・正常計測だが少数・技術欠測を区別する。保存済み質問紙は上書きせず、復元時に実施者が元の回答を照合して進める。

来訪状態は重要な操作と定期書出しで保存する。新しい試行は記録を開く前に識別と予定を保存し、再送や再起動で二重開始しない。経過時間は単調時計に加えUTCとの差を保守的に照合し、再起動中の時間を来訪から除かない。時計の後退等で成立性が不明なら停止し、休憩や90分上限を自動で救済しない。

## 試行単位の記録

| ファイル | 内容と欠測の扱い |
|---|---|
| `session.json` | ID、元試行、群・時点・課題・反復・主要側、入力源、固定目標、基準2反復、音量、時計、ビルド。`target_available=false`は未算定で、0°という目標ではない |
| `calibration.json` | 5役割の機器キー・実行中ID、車体基準、左右の中心・軸・符号・接触基準、コース、検出閾値 |
| `poses.csv` | 1更新につき5役割。取得時刻、利用可能なInput System時刻、機器・追跡状態、位置[m]・姿勢[xyzw]。無効値は空欄と理由を残す |
| `events.csv` | 接触・離手候補、判定、提示・取消・無効理由。目標未算定は`stroke_measured`、算定後は`stroke_classified`、無効は`stroke_invalid` |
| `trials.csv` | 作成、10秒準備後の開始、動き始めのソフト推定、訓練FB区間終了、安全停止後の終了、中断の時刻・理由 |
| `diagnostics.csv` | Unity更新間隔等。取得できない電池・機器時刻を推測で付けない |
| `completed.json` | 正常に記録を閉じた印。正しい経路完了、動画成立、検出精度、全プッシュ成立は保証しない |

割付前は`session.json`で`group=unallocated`。イベントCSVの内部`NoFeedback`は、この段階ではN群への割付ではない。群の解析は来訪の割付記録と設定JSONを対応付ける。

CSVはUTF-8、小数点`.`、角度は度、時刻は秒。座標はUnityのOpenXR世界座標で位置はm、姿勢はxyzw。校正で初期車体基準へ変換し、コースから進行方向を求める。

`acquired_time_s`はアプリが取得した単調時計の時刻、`input_system_time_s`は取得可能なソフト入力時刻で、センサーの測定時刻ではない。Engineの`software_time_s`は各フレームの処理開始へ固定した時刻で、API呼出し瞬間・実出力時刻ではない。音声DSP時刻も[実出力と区別する](logging-and-timing.md#音と表示の実出力を測る)。

訓練のFB区間を終えた後も、安全停止まで元データを記録する。準備、走行のソフト計時、はみ出し、外部動画の正式な時間を混在させない。

## 保存失敗と復元

通常0.5秒間隔と終了時に試行を書き出す。保存失敗は提示・試行を止め、元記録を保持する。空き容量64 MiBは書込みを開始・継続するための実装上の予備領域であり、研究性能の受入れ基準ではない。記録量に応じた実際の容量・電池・処理負荷は予備確認する。

再起動で既存ディレクトリーを上書きしない。正常終了の印がない記録は未完了として扱い、回収できる。未書出し分やOSバッファの電源断耐性は保証しない。記録の終了印と予定が食い違う場合、自動再試行せず元ファイル・動画と照合する。[復旧の操作](experiment-console.md#中断したら)

## PCブラウザーへ回収する

1. 停止後に「記録・例外対応」から「終了・中断済み記録をPCへ回収」を押す。
2. 来訪・試行のZIP作成、転送、SHA-256照合の完了を待つ。回収中は新しい試行を始めない。
3. PCの表示された保存先とZIPを確認する。標準は起動したリポジトリーまたはコンソール展開先の`.local/experiment-data/`。ファイルは所有者用の権限で作成する。
4. ZIP内の`ResearchVisits/`・`ResearchSessions/`と`SHA256SUMS.txt`を照合し、動画・紙の採点表とIDで対応付ける。アクセス制限のある学内保管先へコピーする。

PC側のZIPと外側の`.sha256`を保持する。転送失敗時もゴーグルの元記録を削除しない。PCコンソールを再起動しても回収済みのファイルは残る。PCの回収一覧は`archives.json`から読み戻す。

## Windowsへ取り出す

通信で回収できない場合も、被験者に操作を求めない。

1. PCの「記録・例外対応」で「USB用ZIPをゴーグルのDownloadへ出力」を押す。
2. 書出し完了を確認し、WindowsへUSB接続してファイル転送を選ぶ。
3. **VIVE Focus Vision → 内部共有ストレージ → Download → WheelchairSonificationExports**を開く。
4. 新しいZIPを研究用保管先へコピーし、内容とチェックサムを照合する。

Android 10以降のMediaStore.Downloadsを使う。広範なストレージ権限やAndroid/dataの直接操作を求めず、書込み中を未確定として扱う。失敗した共有領域のコピーは整理し、元データは保持する。[Android公式](https://developer.android.com/training/data-storage/shared/media)

共有ストレージへ出力したZIPは共用端末にも残る。学内の保管・回収方針に従って管理する。ZIPには実名・対応表・外部動画を含めず、公開GitHub・Releaseへ実測を添付しない。[正式保管先・年限等の確認事項](../operations/study-operations.md#データ管理)

## 元データから再解析する

```sh
just unity-replay /絶対パス/試行ディレクトリ
```

`poses.csv`、`calibration.json`、`session.json`、開始・終了と訓練FB区間終了の制御記録を共通の推定処理へ通す。元ファイルを変更せず、再解析CSVと`replay-result.json`を別の場所へ保存する。状態・無効理由は一致、角度の比較許容差は0.0001°とする。これは再現性の許容差で、実測精度ではない。

準備中に中断した記録や開始後のフレームがない記録は、再解析対象がないことを明示する。再生は実出力を行わず、実時間提示の検証にも代用しない。通常の被験者画面に再解析用メニューは置かない。

公式資料の確認日：2026-10-20。Unity 6000.6.4f1、Android最小API 29・対象36。
