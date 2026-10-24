# L66｜Sonification of sculler movements, development of preliminary methods

Gaël Dubus, Roberto Bresin. Interactive Sonification Workshop (ISon 2010), pp.39–43. [公式プロシーディング](https://interactive-sonification.org/files/ISon2010_full_proceedings.pdf)、[大学収録版](https://pub.uni-bielefeld.de/download/2277223/2784318/ISon2010_full_proceedings.pdf)。確認日：2026-10-21。閲覧範囲：著者公開本文の実装・考察の関連節。公式PDFの取得が不安定なため未保存。DOI未確認。

| 項目 | 内容 |
|---|---|
| 目的・装置 | スカルの動きをリアルタイムに音へ変える予備的実装。Nokia N95のGPS・慣性センサー等 |
| 対応 | 移動平均からの速度偏差を指数式で純音の周波数へ対応。音楽的な音色等も試作 |
| 評価・人数 | 工学的試作と予備的な使用感。統制された学習比較の人数・成績は確認範囲に記載なし |
| 限界 | 漕艇の試作であり、車いす・左右分離・本研究の数値の最適性を示さない |

確認位置：§4.3.1の周波数対応、§4.4（p.42）の使用感。長い純音や反復音の不快・退屈が問題になり得る。波形が滑らかなことと聴いて疲れないことは別で、2分の実機試聴と質問が必要。本研究では正弦波を初期音色とし、快適だと断定しない。
