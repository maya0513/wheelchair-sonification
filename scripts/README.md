# 補助スクリプト

実行入口は`just --list`。仕様は[開発資料](../docs/development/guide.md)へ集約する。

| 入口 | 役割 |
|---|---|
| `just check` | Nodeのスクリプト・時計・HTTP・保存・配布のテスト。実Releaseは作らない |
| `unity.sh` | Editor起動・コンパイル・Edit/Play Mode・Linux合成入力版 |
| `unity-xr-sdk.mts` | HTC SDKの固定URL・ハッシュ・キャッシュ検証 |
| `unity-release.mts` | 署名付きAPK、ARM64／XR／権限／ライセンス・更新署名の検証 |
| `github-apk-release.mts` | 同じソースのAPK・コンソール・画像とWAV、画像付き概要資料をDraft添付後に公開 |
| `android-signing.mts` | ローカル鍵の初回作成・読込み。秘密は出力しない |
| `unity-preview.mts` | 共通Unity UIのPNG・WAV・閲覧HTML |
| `console-preview.mts` | 実ブラウザーのPNG・切り出し・閲覧HTML |
| `experiment-console.mts` / `console/` | オフラインの手動操作・PC時計・回答保存 |
| `console-integration.mts` | Unity合成入力とPCの短時間結合確認／任意の継続確認 |
| `console-validation.mts` | 継続確認の成功証拠がある場合だけ採用。未実施でも公開を妨げない |
| `console-packaging.mts` | 機密・参加者ログを除いた配布ZIP |
| `unity-results.py` | NUnit XMLの失敗・件数・空結果の検査 |
| `unity-state.mts` | バッチ実行後にUnity設定・動的フォントの変更を戻す |
| `unity-mcp.mts` | 任意のEditor接続。ビルドに不要 |

ローカル回収先は既定で`~/WheelchairSonificationData/`。テストの合成記録は`.local/`と一時ディレクトリへ分離する。固定時計で2分・20分を確認し、実時間待機を通常のテストに持ち込まない。`just experiment-soak`だけが任意で90分稼働を確認する。
