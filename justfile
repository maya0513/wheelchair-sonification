[unix]
set shell := ["bash", "-eu", "-o", "pipefail", "-c"]
[windows]
set shell := ["cmd.exe", "/D", "/C"]
set default-list := true

# 開発・検証用。just --list devで一覧を表示
mod dev 'tasks/dev.just'

# ロックされたツールとHTC SDKを準備
setup:
    mise install --locked
    mise exec --locked -- node scripts/unity-mcp.mts sync-package
    just dev unity-prepare

# ツールとプロジェクトのスキルを更新し、環境を準備
update:
    mise lock --bump
    just setup
    mise exec --locked -- skills update --project --yes

# 実験用PC画面を起動（Wi-Fi設定は変更しない）
start:
    mise exec --locked -- node scripts/experiment-console.mts
