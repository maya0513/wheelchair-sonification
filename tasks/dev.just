[unix]
set shell := ["bash", "-eu", "-o", "pipefail", "-c"]
[windows]
set shell := ["cmd.exe", "/D", "/C"]
set working-directory := '..'
set default-list := true

# 補助スクリプト・時計・HTTP・保存・配布と文書を確認（Editor・実Releaseは実行しない）
check:
    bash -n scripts/unity.sh
    mise exec --locked -- python scripts/check-docs.py
    mise exec --locked -- node --test scripts/*.test.mts

# 文書リンク・書誌・保存PDFを照合
docs-check:
    mise exec --locked -- python scripts/check-docs.py

# HTC公式SDKを固定URL・ハッシュで取得
unity-prepare:
    mise exec --locked -- node scripts/unity-xr-sdk.mts

# Unity Editorを開く（Vulkan、終了まで端末を使用）
unity-open:
    mise exec --locked -- bash scripts/unity.sh open

# Unityを閉じてから、依存解決とコンパイルを確認
unity-check:
    mise exec --locked -- bash scripts/unity.sh check

# 同じUnityプロジェクトを使うため、Edit Mode → Play Modeの順に実行
unity-test: unity-test-edit unity-test-play

# 計算・判定のテスト
unity-test-edit:
    mise exec --locked -- bash scripts/unity.sh test-edit

# Unity上の状態遷移・表示・保存のテスト
unity-test-play:
    mise exec --locked -- bash scripts/unity.sh test-play

# Linuxの合成入力版を.local/builds/へ作成
unity-build:
    mise exec --locked -- bash scripts/unity.sh build

# 本番用の署名付きFocus Vision APKを作成
unity-build-apk:
    mise exec --locked -- node scripts/unity-release.mts

# 指定版を新規ビルドし、GitHubプレリリースとして公開
release-apk tag version_code:
    mise exec --locked -- node scripts/github-apk-release.mts {{quote(tag)}} {{quote(version_code)}}

# ゴーグル・PCの画像と音見本を生成（Unity Editorを閉じてから）
preview:
    just dev unity-preview
    just dev console-preview

# 共通Unity UIからPNG・WAVを生成
unity-preview:
    mise exec --locked -- node scripts/unity-preview.mts capture

# EditorでXR Interaction Simulatorを起動（通常のAndroid実行には含めない）
unity-preview-open:
    mise exec --locked -- node scripts/unity-preview.mts open

# 元データ・時刻・校正から速度と提示値を再解析
unity-replay session_directory:
    mise exec --locked -- node scripts/unity-preview.mts replay {{quote(session_directory)}}

# 実ブラウザー画面を撮影
console-preview:
    mise exec --locked -- node scripts/console-preview.mts

# 短時間のUnity＋PC確認（先にunity-build。実際の20分待機なし）
experiment-integration:
    mise exec --locked -- node scripts/console-integration.mts 180

# 明示実行だけの90分継続確認（先にunity-build。通常チェック・公開からは呼ばない）
experiment-soak:
    mise exec --locked -- node scripts/console-integration.mts 5400

# 署名鍵の初回作成（既存の鍵は上書きしない）
android-signing-init:
    mise exec --locked -- node scripts/android-signing.mts

# 任意のUnity–Codex接続（Ctrl+Cで終了）
unity-mcp:
    mise exec --locked -- node scripts/unity-mcp.mts
