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

# 署名付きAndroid APKを作成（Windows/Linux、初回設定はdocs/development/android-build.md）
unity-build-release:
    node scripts/unity-release.mts

alias unity-build-apk := unity-build-release

# UnityとCodexをつなぐHTTPサーバーを起動（Ctrl+Cで終了）
unity-mcp:
    mise exec -- node scripts/unity-mcp.mts

# latest/ltsを再解決してロック情報と導入済みツールを更新
tools-update:
    mise lock --bump --platform linux-x64
    just setup
