using MCPForUnity.Editor.Services;
using UnityEditor;

// MCP接続先はリポジトリで管理し、起動時の共有クライアント設定更新を行わない。
[InitializeOnLoad]
internal static class McpStartupPolicy
{
    static McpStartupPolicy()
    {
        // MCP for Unity 10.2.0の遅延処理が参照する、Editorセッション限定のガード。
        SessionState.SetBool(StartupConfigRewrite.SESSION_GUARD_KEY, true);
    }
}
