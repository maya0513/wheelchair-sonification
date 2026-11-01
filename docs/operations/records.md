# 保存先と記録の回収

実施者が各試行の保存・PCコピー・バックアップを確認する手順。列や座標系の意味は[記録形式](../specifications/recording.md)を参照する。研究ID・来訪ID・試行IDで対応付け、実名は入力しない。

## 保存先

| 場所 | 内容・確認方法 |
|---|---|
| PC：`~/WheelchairSonificationData/` | 初期回収先。画面に実際の絶対パスを表示する。回収ZIP、SHA-256、`archives.json`、来訪ごとの`operator.json` |
| HMD：`Application.persistentDataPath/ResearchSessions/<trial-id>/` | 研究試行の元データ。位置姿勢・速度・視線・表示・設定・校正など |
| HMD：`Application.persistentDataPath/DeviceChecks/<check-id>/` | 案内付きチェック。研究試行と分けて保存 |
| HMD：`Download/WheelchairSonificationExports/` | アプリから書き出すUSB回収用ZIP |

Androidのアプリ内保存先は通常`Android/data/io.github.maya0513.wheelchairsonification/files/`配下。実機の実際の場所を確認する。Windowsから元アプリデータを直接編集する必要はない。

## 各試行の終了後

1. 安全停止後に記録を終了する。一件分のZIPが自動でPCへ転送される。
2. コピーとSHA-256照合が成功したことをPCの「記録」で確認する。
3. 未完了・未回収があれば理由を記録する。コピー後もHMDの元データは残す。

ZIPの内部ファイルと外側のチェックサムは、コピーの一致を確かめるもの。正しい課題完了や計測精度の保証ではない。`completed.json`もファイルを正常に閉じた印で、正式採点の印ではない。

## 失敗・再起動から回収する

「未回収の記録をコピー」で古い記録から再回収する。転送失敗時は元データと未回収一覧を残し、別試行のZIPへ上書きしない。PCなしで行った単体チェックも接続後に回収できる。

HMD再起動後の再回収は保存済み試行を再探索するため、既存ZIPと重複しうる。試行IDで照合し、別試行として数えない。過去の来訪は自動再開せず、同じ研究IDの新しい来訪を作り、前後の来訪IDを備考で対応付ける。

PCの回答・計時の保存失敗はブラウザー内にも一時保持し、次の起動で再保存を試みる。ブラウザー再起動をまたいだ時計は復元しない。保存開始だけで終了のない記録を未完了として確認する。

## USBでコピーする

アプリの記録ページから終了・中断済み記録をZIPへ書き出す。AndroidのMediaStoreを使い、上記Download配下へ出力する。Windowsでは「内部共有ストレージ → Download → WheelchairSonificationExports」を開いてPCへコピーする。書出しに失敗しても元データは残る。

## 正式保管

PCは現地回収先で、正式保管先ではない。照合・バックアップと施設規則の確認が済むまで元データを削除しない。実名・同意書・対応表は計測データと別管理し、参加者データをリポジトリやReleaseへ入れない。アクセス権・保存期間・撤回期限は[責任者が確定する事項](study-operations.md#募集安全データ管理)に従う。
