# L50 niehorster（2017）

**The Accuracy and Precision of Position and Orientation Tracking in the HTC Vive Virtual Reality System for Scientific Research.**

Niehorster DC; Li L; Lappe M. i-Perception. 2017.

- 出典：[10.1177/2041669517708205](https://doi.org/10.1177/2041669517708205)
- 閲覧範囲：本文の方法・結果・限界を重点確認。調査日：2026-10-14
- 本文：PDFのローカル保存なし。保存版・取得条件は[PDF一覧](../pdfs/INDEX.md)参照。

## 研究の要約

| 項目 | 内容 |
|---|---|
| 目的・デザイン | 初期HTC Viveの位置・姿勢の正確さ・精密さと遅延の計測。 |
| 対象 | 装置実験、2台のViveや参照計測系。 |
| 課題 | 位置グリッド、追跡喪失と再捕捉、静止・移動。 |
| 計測・装置 | 初期HMD、Lighthouse、WorldViz、InertiaCube、高速度カメラ。 |
| FB・情報提示 | 視覚出力までのend-to-end計測。音FBの実験ではない。 |
| 評価時点・指標 | 位置・角度の偏り、ジッタ、再捕捉、画面変化までの遅れ。 |

## 結果と限界

高い精密さと約22 msの表示遅延を示すが、床面の傾き・追跡再捕捉後の基準ずれがある。

2017年のHMD系の値で、手元のトラッカーや音出力の保証値ではない。静止の精密さと動的な正確さは別。

## 本研究への示唆

追跡喪失をログし、その後の基準を再確認。実空間の長さ・回転・接触角で校正する。

## 確認位置

General Methods、Results: Changing Reference Plane / Examining Vive System Latency、Discussion。

数値・装置は当該研究の報告であり、手元の装置の仕様や再現性能を保証する値ではない。提案との関係は[全体まとめ](../synthesis.md)・[装置比較](../implementation/device-comparison.md)参照。
