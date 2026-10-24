# L28｜Monitoring wheelchair propulsion patterns: feasibility and validity of using wearable sensors.

Fathian R; Khandan A; Rahmanifar N; Ho C; Rouhani H. Journal of neuroengineering and rehabilitation. 2025.

- 出典：[10.1186/s12984-025-01823-5](https://doi.org/10.1186/s12984-025-01823-5)
- 閲覧範囲：本文の方法・結果・限界を重点確認。調査日：2026-10-14
- 本文：[保存PDF](../pdfs/L28.pdf)。保存版・取得条件は[PDF一覧](../pdfs/INDEX.md)参照。

## 研究の要約

| 項目 | 内容 |
|---|---|
| 目的・デザイン | 手装着IMUとCWT特徴から4推進パターンを分類。 |
| 対象 | 12名、経験者8・未経験者4。 |
| 課題 | ローラー上で各パターン1分。各人各パターン40周期、計1920周期。 |
| 計測・装置 | 利き手の手背にXsens MTws、100 Hz。CWT、180特徴、SVMまたはMLP。 |
| FB・情報提示 | 学習FBの有効性ではなく分類手法。区間検出用に10 Hz、6次のno-lagフィルタ。 |
| 評価時点・指標 | 参加者を丸ごと除くLOSO交差検証。 |

## 結果と限界

MLP accuracy95%、F1 89%。SVM多項式accuracy94%、F1 88%。

実験室・指示された動作・少数被験者。オンライン実装未検証。100 Hz標本化に対して本文の周波数記述には100 Hzまでとの説明があり、Nyquist範囲・実装はコードで追加確認が必要。

## 本研究への示唆

被験者単位の分割は参考になる。今回ML分類器や周期検出は導入せず、車体に対する左右車輪速度の検証を優先する。[検証計画](../../docs/specifications/validation.md)を参照。

## 確認位置

Methods: Experimental design / Propulsion pattern classification / Performance analysis、Discussion。
