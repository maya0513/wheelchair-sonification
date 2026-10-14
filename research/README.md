# 先行研究調査

調査日：2026-10-14。初回の52研究等＋8公式資料に、研究仕様の具体化に必要な9研究等＋1公式資料を追加し、**61研究等＋9公式資料＝70件**を整理した。本文重点確認29件、関連節・抄録・書誌のみを区別。保存資料はPDF32件と公式Word1件。

## 読む順序

1. [研究全体のまとめ](synthesis.md)：文献から言えることと限界。
2. [車いす技能の課題選定](topics/wheelchair-skill-assessment.md)：8の字・WPT・WST等の測定特性と採否。
3. [FB内容・画面・音の比較](topics/feedback-design-alternatives.md)：採用した案と代替案の根拠。
4. [装置比較](implementation/device-comparison.md)：先行研究の計測・処理・出力・検証。
5. **[本研究の現行計画・手順・発表版](../docs/README.md)**：本研究に固有の情報はdocsへ移した。

## 調査からの重要な点

- 車いすの音FBには先例がある。「初めて音を使う」を新規性にしない。[L13](papers/L13-gref-2016.md) [L17](papers/L17-symonds-sensewheel-2016.md)
- 視覚FBにも保持の報告がある。聴覚の一般的優位性を前提にしない。[L14](papers/L14-yan-2021.md)
- 接触角の推定は検証を要し、位置から力・トルクは直接測れない。[L25](papers/L25-ojeda-2014.md)
- 8の字原著の検者間ICCは、再テスト信頼性・学習への反応性を意味しない。[L53](papers/L53-kamegaya-2021.md)
- 追加負荷が初心者の群差を必ず明瞭にするわけではない。[L58](papers/L58-salm-2025.md)
- 20分休憩後の評価は同日内の短時間保持。48時間後の長期定着と同等ではない。[L03](papers/L03-kantak-2012.md)

## 領域別レビュー

| 領域 | 資料 |
|---|---|
| 運動学習 | [成績・保持・1回来訪](topics/motor-learning.md) |
| 情報FB | [KR/KP・時点・頻度・統制](topics/information-feedback.md) |
| 音 | [ソニフィケーション](topics/auditory-feedback.md)・[具体案比較](topics/feedback-design-alternatives.md) |
| 車いす | [推進の指標](topics/wheelchair-propulsion.md)・[技能と課題](topics/wheelchair-skill-assessment.md) |
| 装置 | [計測の読み方](topics/sensing-and-devices.md)・[実装比較](implementation/device-comparison.md) |
| EEG | [将来の補助評価に関する調査](topics/eeg.md)。今回の実験には組み込まない |

## 出典と保存資料

[文献一覧](catalog.md)、[BibTeX](references.bib)、[PDF一覧](pdfs/INDEX.md)、[PDF manifest](pdfs/manifest.json)、[Word取得条件](sources/INDEX.md)、[検索方法](search-method.md)、[照合ログ](search-log.md)。原著の報告・本研究の提案・確認待ちの事実を分けて読む。

[原発表の復元](../docs/project-baseline.md)、[旧案・EEG保留資料](../docs/archive/README.md)はdocsに移した。現在は走行中FB・8の字主要評価・直進と8の字の両方を訓練する案。以前の区間後FB・角度主要・独立転移・EEG追加の提案は現行計画ではない。

```text
research/
  README.md / synthesis.md
  search-method.md / search-log.md / catalog.md / references.bib
  topics/           領域レビュー・課題選定・FB比較
  papers/           70件の個別ノート
  implementation/   先行研究の装置比較
  pdfs/             関連PDF32件・取得元・ハッシュ
  sources/          公式Word1件・取得条件
```

実験アプリ・解析コード・参加者の実測データはこの調査に含まない。EEG機器は未到着のため保留し、文献は将来の検討用として残した。
