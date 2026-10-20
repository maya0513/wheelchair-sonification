# 実測記録の形式・保存・取り出し

実験手順の[記録項目](../operations/record-forms.md)を、アプリのファイルへ対応付ける。実機の取得・実出力・現地での取り出しは別途確認する。[模擬入力の記録](feedback-demo.md#記録を確認する)と実測を混ぜない。

## 保存先と対応

- 実測：`Application.persistentDataPath/ResearchSessions/<UTC時刻＋ランダムID>/`。
- 模擬：同じ基点の`DemoSessions/`。既存記録を移動・削除しない。
- 実測の1ディレクトリは1試行。`participant_id`は研究用ID、`visit_id`で同じ来訪、`trial_id`で各試行を対応付ける。繰返し番号と実施順を混同しない。

Androidの基点は通常`/storage/emulated/0/Android/data/io.github.maya0513.wheelchairsonification/files/`だが、端末のAPIが返す値を使う。WindowsからAndroid/dataを直接操作することを取り出し手順にしない。[Unityの保存先](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/Application-persistentDataPath.html)

| ファイル | 内容と欠測の扱い |
|---|---|
| `session.json` | ID、群・時点・課題・反復・主要側、入力源、目標、基準2反復、音量、時計、ビルド。`target_available=false`の数値欄は未算定で、0°という目標ではない |
| `calibration.json` | 5役割の機器キー・実行中ID、車体基準、左右の中心・軸・符号・接触基準、コース、検出閾値 |
| `poses.csv` | 1更新につき5役割。取得時刻、利用可能なInput System時刻、役割・機器、追跡の有効性、位置[m]・姿勢[xyzw]。無効時の位置姿勢は空欄にし、理由を記録 |
| `events.csv` | 既存の判定・提示イベント列を維持。目標未算定の事前角は`stroke_measured`、算定後の状態は`stroke_classified`。無効は`stroke_invalid` |
| `trials.csv` | 試行作成、準備後の開始、終了要求、正常終了または中断の時刻・理由。ソフト上の経過時間を動画の正式採点と区別 |
| `diagnostics.csv` | Unity更新間隔等の診断。取得できない機器情報を推測で付けない |
| `completed.json` | 正常に記録を閉じた印。正しい課題完了、検出精度、全プッシュ成立を保証する印ではない |

群をまだ割り付けていない事前記録は`session.json`で`group=unallocated`とする。共通EngineのイベントCSVでは提示を止めるため内部的に`NoFeedback`を使うが、これはN群への割付を意味しない。群の解析では設定JSONと同じ来訪IDの割付後記録を対応付ける。

CSVはUTF-8、小数点は`.`、角度は度、時刻は秒。座標はUnityのOpenXR世界座標、位置はm。校正により初期車体基準へ変換し、進行方向はコースから求める。時計・座標の意味は設定と校正を合わせて読む。

`acquired_time_s`はアプリがそのフレームを取得した単調時計の時刻。`input_system_time_s`は取得できた場合の入力システムの時刻で、センサーの測定時刻ではない。研究用Engineの`software_time_s`は、各フレームの処理開始に固定した同じ時計の値であり、API呼出し瞬間や実出力の測定値ではない。音声DSPの時刻も[実出力とは区別する](logging-and-timing.md#音と表示の実出力を測る)。

## 保存失敗と中断

通常0.5秒間隔と正常終了時に書き出す。書込みに失敗したら提示・試行を止める。再起動しても既存ディレクトリを上書きせず、正常終了の印がない記録を未完了として一覧に数える。強制終了や電源断で未書出し分・OS側バッファが保存されることは保証しない。未完了記録も取り出せる。

## Windowsへ取り出す

1. 試行を終了・停止し、ゴーグルの「記録」を開く。
2. 「実測記録を書き出す」または「模擬記録を書き出す」を選ぶ。ZIP作成とコピー中は完了を待つ。
3. WindowsへUSB接続してファイル転送を選ぶ。
4. エクスプローラーで **VIVE Focus Vision → 内部共有ストレージ → Download → WheelchairSonificationExports** を開く。
5. 新しいZIPを研究用の保管場所へコピーし、展開してファイルと`SHA256SUMS.txt`を確認する。

Android 10以降のMediaStore.Downloadsを使い、OSのファイル選択画面や広範なストレージ権限に依存しない。書込み中は未確定の項目とし、失敗したコピーを公開フォルダーへ残さない。元の記録は削除しない。[Android公式資料](https://developer.android.com/training/data-storage/shared/media)

アプリのZIPには実名・対応表・外部動画を含めない。公開GitHubやReleaseへ実測データを添付しない。学内の保存先、アクセス担当、保存期間、バックアップは[データ管理方針](../operations/study-operations.md#データ管理)に従って責任者と確認する。共有ストレージへ書き出したZIPは、回収後も共用端末に残る点を含めて管理する。

## 元データから再解析する

PCでは次を実行する。ゴーグル内の「直近の実測を再解析」も同じ処理を使う。

```sh
just unity-replay /絶対パス/試行ディレクトリ
```

元の`poses.csv`、`calibration.json`、`session.json`、開始時点を使って同じ推定処理へ通す。元データは変更せず、再解析のCSVと`replay-result.json`を別の場所へ保存する。プッシュ角・状態・無効理由を元の判定と照合する。角度の比較許容差は0.0001°、状態・無効理由は一致を要求する。これは実測精度を意味する許容差ではない。開始前に中断された記録には推進判定の再解析対象がないことを表示する。

公式資料の確認日：2026-10-20。Unity 6000.6.4f1、Android最小API 29。
