set shell := ["bash", "-eu", "-o", "pipefail", "-c"]

# 利用できる操作を表示
default:
    @just --list

# ロックされた開発ツールを導入
setup:
    mise install --locked just node python uv typst pypi:mcpforunityserver

# 補助スクリプトの構文と失敗時の動作を確認（Editor未実行）
check:
    mise exec -- bash -n scripts/unity.sh
    mise exec -- node --test scripts/*.test.mts

# Unityを開く（Vulkan、終了まで端末を使用）
unity-open:
    mise exec -- bash scripts/unity.sh open

# Unityを閉じてから、バッチで依存解決とコンパイルを確認
unity-check:
    mise exec -- bash scripts/unity.sh check

# UnityとCodexをつなぐHTTPサーバーを起動（Ctrl+Cで終了）
unity-mcp:
    mise exec -- node scripts/unity-mcp.mts

# latest/ltsを再解決してロック情報と導入済みツールを更新
tools-update:
    mise lock --bump --platform linux-x64
    just setup
