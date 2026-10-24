# L52｜Music-based biofeedback to reduce tibial shock in over-ground running: a proof-of-concept study.

Van den Berghe P; Lorenzoni V; Derie R; Six J; Gerlo J; Leman M; De Clercq D. Scientific reports. 2021.

- 出典：[10.1038/s41598-021-83538-w](https://doi.org/10.1038/s41598-021-83538-w)
- 閲覧範囲：本文の方法・結果・限界を重点確認。調査日：2026-10-14
- 本文：[保存PDF](../pdfs/L52.pdf)。保存版・取得条件は[PDF一覧](../pdfs/INDEX.md)参照。

## 研究の要約

| 項目 | 内容 |
|---|---|
| 目的・デザイン | 実走行ランニングで音楽＋ノイズにより脛骨衝撃を下げる概念実証。 |
| 対象 | 88名スクリーニングから高衝撃の10名。 |
| 課題 | 一定範囲の速度で20分の音FB走行。対照群なし。 |
| 計測・装置 | 小型加速度計LIS331×2、1000 Hz、Teensy3.2、Windowsタブレット、Java→OSC→Max/MSP、ヘッドホン。 |
| FB・情報提示 | 直近5歩の衝撃平均を6段階のpink noiseに変換。音楽のテンポは走者へ追従。 |
| 評価時点・指標 | FB中の終盤90秒と基準を比較。無FB保持の検証なし。 |

## 結果と限界

脛骨衝撃が27%低下。頻度の明確な増加はない。

選択された高衝撃走者、対照なし、音楽とノイズの効果を分離不可。375 msのピーク判定など検出窓を含み、「real-time」を無遅延と解釈できない。

## 本研究への示唆

マイコンから音出力までの実装経路を参考にする。今回はイベント平均・離散音量・音楽再生を採用せず、左右車輪速度を連続した音高へ対応付ける。[FB仕様](../../docs/specifications/feedback.md)を参照。

## 確認位置

Methods: Set-up / A smart music player / Data processing、Discussion: Limitations、図2。
