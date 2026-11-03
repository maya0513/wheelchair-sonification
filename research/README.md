# 文献調査

車いす推進、運動学習、情報フィードバック、ソニフィケーション、計測装置の調査。[全体のまとめ](synthesis.md)から領域別レビュー、個別文献へ進む。本研究で採用する条件・数値は[研究資料](../docs/README.md)で管理する。

## 研究の背景を読む

| 調べたいこと | レビュー |
|---|---|
| 練習中の成績と保持を区別する | [運動学習](topics/motor-learning.md) |
| 情報の種類・時点・頻度を選ぶ | [情報フィードバック](topics/information-feedback.md) |
| 動作の音化と効果を理解する | [聴覚FB](topics/auditory-feedback.md) |
| 車いすの指標と課題を選ぶ | [推進指標](topics/wheelchair-propulsion.md)・[技能評価](topics/wheelchair-skill-assessment.md) |
| 車輪・手首・車体などの候補を比較する | [FB情報の比較](topics/feedback-design-alternatives.md) |

## 装置・開発の根拠を調べる

| 調べたいこと | 技術調査 |
|---|---|
| センサー、処理、音生成、遅延、検証 | [計測・FB装置の比較](implementation/sensing-and-feedback-devices.md) |
| 準備画面、縦バー、透明度、眼球取得 | [XR画面と視線記録](implementation/xr-ui-and-eye-tracking.md) |
| 開始地点から離れたときのVIVE警告 | [境界警告の調査](implementation/vive-boundary-warnings.md) |
| 会場回線なしのPC操作と時計 | [オフライン通信](implementation/offline-local-control.md) |
| モック・記録再生・テストで開発する | [開発とテストの入門](implementation/development-and-testing-guide.md) |

これらは文献・公式資料と設計判断を整理する資料。現行アプリの操作は[実施手順](../docs/operations/setup.md)、実行コマンドは[開発資料](../docs/development/README.md)を参照する。

## 出典と調査範囲を確認する

| 情報 | 保存場所 |
|---|---|
| 書誌・閲覧範囲・引用データ | [文献一覧](catalog.md)・[BibTeX](references.bib) |
| 各研究の目的・方法・結果・限界・確認位置 | 文献一覧からリンクする`papers/`の個別ノート |
| 探索・採否の方法 | [調査方法](search-method.md) |
| 検索・書誌照合の実施日と結果 | [照合ログ](search-log.md) |
| 保存PDF、取得元、利用条件 | [PDF一覧](pdfs/INDEX.md)・[容量とハッシュ](pdfs/manifest.json) |
| 公式Word等の原本 | [保存資料一覧](sources/INDEX.md) |

本文重点確認・関連節・抄録・書誌のみの確認を区別する。各研究の数値を、手元の装置で再現できる性能とは扱わない。個別ノートは出典単位で残し、総説や方法論を同じ研究として二重計上しない。

## 採用していない案を調べる

[推進角・目標誤差の計測可能性](implementation/propulsion-angle-measurement-feasibility.md)は、接触区間を測る案の検討資料。[簡易EEG](topics/eeg.md)は将来の参考で、今回の計測には組み込まない。原発表の内容は[保存資料](../docs/archive/README.md)を参照する。参加者の映像や生ログは文献資料に含めない。
