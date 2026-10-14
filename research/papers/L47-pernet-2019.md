# L47 pernet（2019）

**EEG-BIDS, an extension to the brain imaging data structure for electroencephalography.**

Pernet CR; Appelhoff S; Gorgolewski KJ; Flandin G; Phillips C; Delorme A; Oostenveld R. Scientific data. 2019.

- 出典：[10.1038/s41597-019-0104-8](https://doi.org/10.1038/s41597-019-0104-8)
- 閲覧範囲：本文の関連節を確認。調査日：2026-10-14
- 本文：[保存PDF](../pdfs/L47.pdf)。保存版・取得条件は[PDF一覧](../pdfs/INDEX.md)参照。

## 研究の要約

| 項目 | 内容 |
|---|---|
| 目的・デザイン | EEGの再利用可能なデータ・メタデータ形式を定義する方法論文。 |
| 対象 | 標準化提案。 |
| 課題 | EEG記録・電極・イベント・参照・機器の記述。 |
| 計測・装置 | EEG-BIDS。 |
| FB・情報提示 | FB介入なし。 |
| 評価時点・指標 | データ構造と記述項目。 |

## 結果と限界

解析者が電極・イベント・取得条件を追跡できる形を提案。

規格本文の全条項は本調査で検証していない。BIDS準拠を宣言するには別途検証が必要。

## 本研究への示唆

小規模でも電極名・単位・参照・標本化・フィルタ・イベント・除外理由を残す。今回はBIDSを参考にしたメタデータ表とする。

## 確認位置

本文の導入・取得資料、全条項適合の検証なし。

数値・装置は当該研究の報告であり、手元の装置の仕様や再現性能を保証する値ではない。提案との関係は[全体まとめ](../synthesis.md)・[装置比較](../implementation/device-comparison.md)参照。
