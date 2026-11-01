[unix]
set shell := ["bash", "-eu", "-o", "pipefail", "-c"]
[windows]
set shell := ["cmd.exe", "/D", "/C"]
set default-list := true

# 開発・検証用。just --list devで一覧を表示
mod dev 'tasks/dev.just'

# ロックされたツールとHTC SDKを準備
setup:
    mise install --locked just node python uv typst pypi:mcpforunityserver npm:skills
    just dev unity-prepare

# ツールとプロジェクトのスキルを更新し、環境を準備
update:
    mise lock --bump --platform linux-x64
    just setup
    mise exec --locked node npm:skills -- skills update yomiyasu --project --yes

# 実験用PC画面を起動（Wi-Fi設定は変更しない）
start:
    mise exec --locked node -- node scripts/experiment-console.mts
