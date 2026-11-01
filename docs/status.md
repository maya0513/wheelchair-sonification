# 確認状況と次に行うこと

実装、PC上の確認、実機での測定性能、研究の実施許可を区別する。本計測の判断と担当は[開始条件G1–G6](operations/study-operations.md#本計測開始条件)に集約する。

## 使える機能と確認範囲

| 対象 | 実装・PC上の確認 | 次に行うこと |
|---|---|---|
| 準備・チェック | 案内付き割当・校正、実物選択、部分再確認、HMD単体操作を合成入力で確認 | 実物との対応、目印の位置、装着・キャンバー、全走路を確認 |
| 速度FB | 左右独立の連続音、車体固定の半透明縦バーを実装。計算・無提示条件・画像・波形を確認 | 独立参照で速度・追跡・実出力を測り、左右音・可読性・床やコーンの視認を確認 |
| 視線 | 全群の試行記録に左右眼球・頭部・表示配置を保存。合成入力で欠測・例外・座標変換・保存を確認 | HMD標準の眼球校正、取得率・取得間隔、再装着、処理負荷を実機で確認 |
| PC操作・回収 | 手動課題選択・正式計時・回答・試行別ZIP回収を確認 | 実際の合図音、手動停止、無線、WindowsへのUSBコピー、単独見守りを確認 |
| 実験運営 | 手順・教示・分析・開始条件を文書化 | 倫理・同意、会場、正式保管先・期間、2–3名の通し予備実験、条件固定 |

機能チェックの「確認済み」は測定精度の合格ではない。手順と受入れ目標は[検証計画](specifications/validation.md)を参照する。実機や責任者の確認を、APKの生成・画像確認で代替しない。

## 確認証拠を探す

以下はアプリv0.6.0、ソース`ae2055d4e06ba484374637fb4feeaf7171b7a780`のPC上の確認記録。証拠ファイルはこの開発PCのGit管理外にあり、別PCでは[開発手順](development/README.md)から再実行する。

| 確認 | 結果・記録 |
|---|---|
| 自動試験 | Unity Edit Mode 57件、Play Mode 17件、Node 66件成功。`.local/logs/final-verification.json`と同ディレクトリのテストXML |
| 実通信・回収 | Linux Unityの合成入力18試行を約76.1秒で記録・回収。`.local/console-integration-2026-11-01T18-34-15-438Z/result.json` |
| 回収照合 | 同ディレクトリの`archive-verification.json`。18 ZIP・内部198ファイルのSHA-256が一致。視線・表示2232フレーム分を含み、Linuxでは視線を取得不可として記録 |
| 再生 | 同じ元データ・時刻・校正から速度・提示値を再現。`.local/replay-reports/ResearchSessions-1793558147747/` |
| 画面・音 | Unity 44画像、Chrome 27画像を生成・目視確認。9 WAV（連続2分を含む）の左右分離・無音・クリップ・不連続を検査。実機試聴は未確認 |
| 梱包 | `.local/logs/distribution-verification.json`。コンソールをリポジトリ外へ展開しNodeだけで起動、ZIPとチェックサムを照合 |

現在の文書リンク・文献一覧・BibTeX・PDFの対応は`just dev docs-check`で検査する。配置整理後の推敲・参照確認は`.local/logs/document-reorganization-review.json`に記録する。文献の閲覧範囲は[文献一覧](../research/catalog.md)、原著と提案の区別は各ノートで確認する。

画像とWAVは`.local/ui-preview/index.html`、PC画面は`.local/console-preview/index.html`で開く。[画像付き概要](app-overview.md)は要点だけを掲載する。90分継続試験は任意で、上のソースでの成功証拠はない。20分休憩を実際に待つ自動試験は行わない。

## 配布物

生成済みのローカル一式は`.local/builds/android-release/`。署名付きv0.6.0／versionCode 6のAPK、対応コンソール、概要・画像・音見本、ビルド情報、チェックサムを含む。ARM64・IL2CPP・非デバッグ・XR・通信形式3・ライセンス・署名・既存鍵との一致を検査済み。対象ソースと検査結果は一式の`build-info.json`を確認する。

実際に公開されたバージョンと添付物は[Releases](https://github.com/maya0513/wheelchair-sonification/releases)を参照する。導入は[実施者の準備手順](operations/setup.md)、再生成・公開は[開発者のビルド手順](development/android-build.md)に従う。
