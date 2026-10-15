# L44｜MEG and EEG data analysis with MNE-Python.

Gramfort A; Luessi M; Larson E; Engemann DA; Strohmeier D; Brodbeck C; Goj R; Jas M; Brooks T; Parkkonen L; Hämäläinen M. Frontiers in neuroscience. 2013.

- 出典：[10.3389/fnins.2013.00267](https://doi.org/10.3389/fnins.2013.00267)
- 閲覧範囲：抄録を確認。調査日：2026-10-14
- 本文：[保存PDF](../pdfs/L44.pdf)。保存版・取得条件は[PDF一覧](../pdfs/INDEX.md)参照。

## 研究の要約

| 項目 | 内容 |
|---|---|
| 目的・デザイン | MNE-Pythonの解析機能・設計を紹介するソフトウェア論文。 |
| 対象 | ソフトウェア論文。 |
| 課題 | EEG/MEGの前処理・推定・統計・可視化。 |
| 計測・装置 | Python、NumPy等、BSDライセンス。 |
| FB・情報提示 | FB介入なし。 |
| 評価時点・指標 | 機能と再現可能な解析手順。 |

## 結果と限界

一貫したAPIと公開コード・チュートリアルを提供。

抄録中心。2013年の論文を現在のAPI仕様の根拠としない。ソース推定等は少数電極に適するとは限らない。

## 本研究への示唆

CSV等の生データ→区間注釈→PSD→参加者集約という小さいオフライン解析に利用。現行APIはD06で確認。

## 確認位置

Abstract、現在のAPIはD06。
