[unix]
set shell := ["bash", "-eu", "-o", "pipefail", "-c"]
[windows]
set shell := ["cmd.exe", "/D", "/C"]

# 利用できる操作を表示
default:
    @just --list

# ロックされた開発ツールを導入
setup:
    mise install --locked just node python uv typst pypi:mcpforunityserver

# 補助スクリプトの構文と失敗時の動作を確認（Editor未実行）
check:
    bash -n scripts/unity.sh
    mise exec node python -- node --test scripts/*.test.mts

# HTC公式SDKを取得し、固定チェックサムを確認
unity-prepare:
    node scripts/unity-xr-sdk.mts

# Unityを開く（Vulkan、終了まで端末を使用）
unity-open:
    bash scripts/unity.sh open

# Unityを閉じてから、バッチで依存解決とコンパイルを確認
unity-check:
    bash scripts/unity.sh check

# 判定とUnity統合のテストを順に実行（Editorを閉じてから）
unity-test: unity-test-edit unity-test-play

unity-test-edit:
    mise exec python -- bash scripts/unity.sh test-edit

unity-test-play:
    mise exec python -- bash scripts/unity.sh test-play

# Linux用の技術検証デモを.local/builds/へ作成
unity-build:
    bash scripts/unity.sh build

# 実際のXR UIから画面一覧・切り出しPNG・HTMLを生成
unity-preview:
    node scripts/unity-preview.mts capture

# EditorでUnityのXR Interaction Simulatorを明示的に起動
unity-preview-open:
    node scripts/unity-preview.mts open

# 元の位置姿勢・時刻・校正で推定と判定を再解析
unity-replay session_directory:
    node scripts/unity-preview.mts replay {{quote(session_directory)}}

# 署名付きAndroid APKを作成（Windows/Linux、初回設定はdocs/development/android-build.md）
unity-build-release:
    node scripts/unity-release.mts

alias unity-build-apk := unity-build-release

# このPCに配布用の署名鍵を作成（既存の鍵は上書きしない）
android-signing-init:
    node scripts/android-signing.mts

# 指定版をビルドし、GitHubのプレリリースとして公開
release-apk tag version_code:
    node scripts/github-apk-release.mts {{quote(tag)}} {{quote(version_code)}}

# 実施者用ブラウザーを起動（Wi-Fi設定は変更しない）
experiment-console:
    node scripts/experiment-console.mts

# 実機なしで操作画面を試す（確認番号123456、UIデモ）
experiment-console-demo:
    node scripts/experiment-console.mts --demo

# 任意：Unity合成入力とPCの継続稼働（先にunity-build）
experiment-soak:
    node scripts/console-integration.mts 5400

# 実際のブラウザー画面・切り出し画像を生成
experiment-console-preview:
    node scripts/console-preview.mts

# UnityとCodexをつなぐHTTPサーバーを起動（Ctrl+Cで終了）
unity-mcp:
    mise exec -- node scripts/unity-mcp.mts

# latest/ltsを再解決してロック情報と導入済みツールを更新
tools-update:
    mise lock --bump --platform linux-x64
    just setup

# 短時間のUnity＋PC統合確認（実際の20分待機なし）
experiment-integration:
    node scripts/console-integration.mts 180

alias console-preview := experiment-console-preview
