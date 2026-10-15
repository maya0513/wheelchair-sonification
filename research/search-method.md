# 調査方法と採否

調査・書誌照合日：2026-10-14。失われた資料を補い卒業研究を設計し直すための、重点領域を掘り下げるスコーピング調査。単独で検索・選別・要約した。二名独立選別、登録プロトコル、正式な系統的レビューやメタ解析の手続きは実施していない。

## 情報源と探索

一般検索からPubMed／PMC、出版社、大学リポジトリ、J-STAGE／CiNiiへ到達。論文名・DOIをEurope PMCのcore書誌・抄録で照合し、必要に応じCrossref・Zenodo・J-STAGEの原記録で確認。公開本文は出版社PDF・大学公開稿・Europe PMCのfullTextXMLを利用した。検索サービスの掲載日を研究の発表年としない。

古典から2026年の研究までを対象としたが、全データベースの全件を網羅的にダウンロードしたわけではない。Scholar系・OpenAlex等は発見の補助とし、結論は原著、原著の抄録、著者公開稿、公式技術資料に結びつける。

中間発表で引用された[L05](papers/L05-fujii-2016.md)から運動学習・音FBを追い、[L13](papers/L13-gref-2016.md) [L14](papers/L14-yan-2021.md) [L18](papers/L18-symonds-review-2018.md)から車いすの近縁研究を追った。装置は[L16](papers/L16-guo-optipush-2011.md) [L25](papers/L25-ojeda-2014.md) [L33](papers/L33-kimura-2016.md) [L51](papers/L51-nown-2023.md)を中心に、構成と検証を抽出。EEGは[L34](papers/L34-krigolson-2017.md) [L39](papers/L39-bilalpur-2018.md) [L40](papers/L40-yang-2023.md) [L46](papers/L46-lee-2026.md)から機器検証・負荷・運動ノイズを分けて探索した。

## 実行した検索語の主な例

下記は実際に使用した検索語。各採択文献の個別タイトル・DOI照合も実施した。長い候補リストを検索サービスから得ても、人数・装置・結果を検索抜粋だけで確定しない。

| ID | 検索語 | 意図 |
|---|---|---|
| Q01 | `"More Feedback Is Better than Less" Fujii Lulic Chen 2016` | 中間発表の引用を復元 |
| Q02 | `wheelchair propulsion real time auditory feedback sonification sensor` | 直接の近縁研究 |
| Q03 | `wheelchair propulsion kinematic feedback contact angle wrist sensors learning` | 推進指標・計測 |
| Q04 | `mobile EEG motor task motion artifacts consumer headset validation` | 動作中EEG |
| Q05 | `"sonification" "wheelchair" "2016" training` | 既存の車いす音化 |
| Q06 | `wheelchair propulsion feedback training Rice 2013 overground randomized` | 視覚・教示・対照 |
| Q07 | `wheelchair propulsion real time feedback systematic review Symonds 2017` | 引用探索 |
| Q08 | `motor learning augmented feedback guidance hypothesis Winstein Schmidt 1990 Salmoni 1984 Sigrist 2013 review` | 理論 |
| Q09 | `Sigrist Rauter Marchal-Crespo Riener Wolf 2015 sonification complex rowing task feedback` | 成功・不成功例 |
| Q10 | `wheelchair propulsion wearable sensors contact angle validation Ojeda Ding 2014 2024` | 低価格計測 |
| Q11 | `consumer EEG validation Muse Krigolson 2017 Debener 2012 Badcock 2013 mobile artifacts` | 機器検証 |
| Q12 | `車いす 推進 フィードバック 接触角 聴覚 ソニフィケーション J-STAGE` | 国内文献 |
| Q13 | `"圧力センサのフットスイッチを用いた聴覚フィードバック装置の開発と信頼性"` | 国内の簡素な実装 |
| Q14 | `"A comprehensive evaluation framework for consumer-grade EEG devices"` | 新しい少数ch比較 |
| Q15 | `"A mapping review of real-time movement sonification systems for movement rehabilitation"` | 実装の網羅的な地図 |
| Q16 | `"auditory feedback" "retention" "20 min" motor learning` | 同日内評価 |
| Q17 | `"wheelchair" feedback "immediate" "retention" learning` | 保持の時間尺度 |
| Q18 | `site:docs.unity3d.com AudioSettings GetDSPBufferSize AudioSource PlayScheduled dspTime` | 出力遅延・API |
| Q19 | `site:labstreaminglayer.readthedocs.io time synchronization clock drift time_correction` | 同期 |
| Q20 | `"Preservation of Upper Limb Function" filetype:pdf site:pva.org` | 公式ガイドライン |

## 採択基準

手動車いすの推進・初心者学習・FB、運動学習の解釈、運動ソニフィケーションの設計、転用可能な計測・提示装置、簡易EEGの品質・同期・探索分析に関係する文献を採択した。直接関連する原著と装置検証を優先し、否定的・限定的な結果も残す。

| 候補 | 採否・理由 |
|---|---|
| [L13](papers/L13-gref-2016.md) 車いす音化、[L17](papers/L17-symonds-sensewheel-2016.md) 言語FB | 採択。直接の近縁例で「最初の音FB」といった新規性を点検できる |
| [L06](papers/L06-sigrist-terminal-2013.md) 聴覚不成功、[L49](papers/L49-nikmaram-2019.md) 臨床利益が限定的な追試 | 採択。肯定的研究だけに偏らない |
| [L01](papers/L01-salmoni-1984.md) [L02](papers/L02-winstein-1990.md) | 書誌・歴史的原典として採択。本文未取得なので結果を直接根拠にしない |
| Yan 2020 preprint | 2021正式版[L14](papers/L14-yan-2021.md)に統合。同じ研究を二重計上しない |
| LSL 2024 preprint | 2025正式版[L45](papers/L45-kothe-2025.md)に統合 |
| 車いすのEEG制御・motor imagery BCI | 本調査の主目的から外す。推進中の補助EEGと制御信号を混同しない |
| 一般的なスマート電動車いす総説 | 手動推進の技能・FB装置へ直接つながらないものは不採択 |
| 書誌の転載・ブログ・SNS・商品広告 | 発見補助に限定。学術的結果の根拠に使わない |
| 原著の保存版と出版社版 | 同一IDへ統合。版・年・閲覧範囲を区別 |

## 抽出と限界

全採択に、目的、対象・人数、課題、装置、FB、時点・指標、結果、限界、本研究への示唆、確認位置を記載。本文未取得の場合は「抄録で未確認」とし、本文に記載がないと断定しない。本文を確認した場合も、重要な方法・結果・限界を重点的に読んだ範囲であり、全参考文献や全数式を独立再計算したものではない。

採択した資料の集計と閲覧範囲は[文献一覧](catalog.md)、検索・照合の実施記録は[照合ログ](search-log.md)を参照。母集団・課題・モダリティ・FB時点が違う研究を統合効果量にしない。

単独調査・アクセス制限・英日中心の探索による見落としは残る。卒論の新規性を「世界初」と確定したり、15人の検出力を類似研究の大きな効果量だけで保証したりしない。更新時は新しい検索日・機器型番・採否を追加する。

## 課題・FB設計を調べた検索語

| ID | 実行した検索式・探索先 | 採択・判断 |
|---|---|---|
| Q21 | `Wheelchair Skills Program manual wheelchair skills test roll forward slalom turn while moving 2025 2026 manual pdf` | D09。技能の範囲と全WSTの負担 |
| Q22 | `Wheelchair Propulsion Test 10 m reliability validity push frequency wheelchair test original study` | L54。10 mの方法と測定特性 |
| Q23 | `wheelchair skill test short training able bodied novices slalom figure eight timed test reliability` | L55–L57。課題候補の比較 |
| Q24 | `車椅子8の字駆動テスト 開発` | L53。若年健常成人・上肢駆動・5 mの直接候補 |
| Q25 | `manual wheelchair auditory path feedback training study` | L61。電動車いすの近縁例。手動への直接の根拠にしない |
| Q26 | `manual wheelchair dual-task novice motor control` | L58。負荷追加の解釈 |
| Q27 | `Hand Rim Wheelchair Propulsion Training Using Biomechanical Real-Time Visual Feedback Based on Motor Learning Theory Principles` | L59。画面情報と訓練設計 |
| Q28 | `site:jstage.jst.go.jp 日本語版NASA-TLX 1996` | L60。日本語項目と簡便法 |

原発表の片手片足8字に関する2007年会議抄録（10.14900/cjpt.2006.0.B1295.0）は背景候補として確認したが、今回の両上肢駆動・初心者介入の主課題根拠には採らなかった。坂・段差・スポーツの課題は会場と目的から不採用。公開元から保存できたPDFのみを残し、取得失敗を本文未記載と混同しない。

採択資料は61研究等＋9公式資料の70件。本文重点確認は29件。L60は関連節、L56は抄録、L57・L61は抄録・公開抜粋、D09はWord本文関連節。保存PDF32件とWord1件。閲覧範囲は[文献一覧](catalog.md)。
