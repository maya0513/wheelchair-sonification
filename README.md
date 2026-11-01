# wheelchair-sonification

左右車輪の回転速度を音・ゲージで返し、車いす初心者の練習を支援する卒業研究です。VIVE Focus Visionで計測・提示・保存し、実施者がPCブラウザーで記録と正式計時を操作します。

| したいこと | 最初に読むもの |
|---|---|
| どんなアプリか画像で見る | [アプリの概要](docs/app-overview.md) |
| 研究計画・実験手順・分析を読む | [研究資料の入口](docs/README.md) |
| 機器をつなぎ、動作を確かめる | [導入・装着・準備](docs/operations/setup.md) |
| 実装を変更・テスト・配布する | [開発の入口](docs/development/README.md) |
| 先行研究と設計の根拠を調べる | [文献調査](research/README.md) |
| 実施前に残っている確認を知る | [確認状況](docs/status.md) |

本計測へ進む判断は[開始条件G1–G6](docs/operations/study-operations.md#本計測開始条件)に従います。

普段使うコマンドは次の3つです。開発・検証・ビルド・公開の一覧は`just dev`で表示します。

```sh
just setup   # ツールとSDKの準備
just update  # ツールとスキルの更新
just start   # 実験用PC画面の起動
```
