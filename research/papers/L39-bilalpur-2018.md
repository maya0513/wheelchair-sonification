# L39 bilalpur（2018）

**EEG-based Evaluation of Cognitive Workload Induced by Acoustic Parameters for Data Sonification**

Maneesh Bilalpur; Mohan Kankanhalli; Stefan Winkler; Ramanathan Subramanian. Proceedings of the 20th ACM International Conference on Multimodal Interaction. 2018.

- 出典：[10.1145/3242969.3243016](https://doi.org/10.1145/3242969.3243016)
- 閲覧範囲：本文の方法・結果・限界を重点確認。調査日：2026-10-14
- 本文：[保存PDF](../pdfs/L39.pdf)。保存版・取得条件は[PDF一覧](../pdfs/INDEX.md)参照。

## 研究の要約

| 項目 | 内容 |
|---|---|
| 目的・デザイン | 音のパラメータが認知負荷に及ぼす影響を主観・正答率・EEGで探索。 |
| 対象 | 20名。 |
| 課題 | 画像のぼけの程度を音等に対応づけるImmediate/Compared Recall課題。 |
| 計測・装置 | Emotiv EPOC14ch、128 Hz、MATLAB PsychToolbox。 |
| FB・情報提示 | noise・pitch・roughness等、視覚と複合条件。 |
| 評価時点・指標 | 主観負荷、正答率、ERP、PSD、負荷分類。 |

## 結果と限界

音の対応の分かりやすさで負荷が異なり、低負荷条件でαが高い傾向。分類の最高F1は0.64。

座位の知覚課題で、運動中の研究ではない。負荷ラベルは主観に依存。14chで使ったICAを1chへそのまま移せない。抄録の強い表現より条件別の結果を重視。

## 本研究への示唆

脳波とソニフィケーションを直接つなぐ参考。音なら必ず低負荷という仮説を避け、主観指標を併用する。

## 確認位置

本文3.2–3.5、5.1 Data Preprocessing、5.2 Spectral Analysis、6分類。保存は会議採択のarXiv著者版。

数値・装置は当該研究の報告であり、手元の装置の仕様や再現性能を保証する値ではない。提案との関係は[全体まとめ](../synthesis.md)・[装置比較](../implementation/device-comparison.md)参照。
