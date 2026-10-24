# 会場回線なしの実施者用ブラウザー操作

調査日：2026-10-20–21。研究効果の文献ではなく、機器運用の技術資料である。採用する役割分担は[操作設計](../../docs/experiment-protocol.md)、現地の準備は[ネットワーク手順](../../docs/development/experiment-console.md)で管理する。

## 公式資料から分かること

| 出典 | 確認した内容 | この研究で未確認のこと |
|---|---|---|
| [Ubuntu：Create a wireless hotspot](https://help.ubuntu.com/stable/ubuntu-help/net-wireless-adhoc.html.en) | PCを無線アクセスポイントにし、別のネットワークなしで端末をつなげる。既存Wi-Fiとの切替が必要になる場合がある | このPCとFocus Visionの実際の接続・到達範囲 |
| [NetworkManager：Wi-Fi device capabilities](https://www.networkmanager.dev/docs/api/latest/nm-dbus-types.html) | `NM_WIFI_DEVICE_CAP_AP`はアクセスポイント対応の能力を表す | 対応フラグだけから90分の安定性・5 GHz動作を保証しない |
| [NetworkManager：nmcli](https://networkmanager.pages.freedesktop.org/NetworkManager/NetworkManager/nmcli.html) | `device wifi hotspot`は対応機器で接続を作成・有効化する | 読取り確認と接続変更を区別する。現段階で自動切替はしていない |
| [Android：Local network permission](https://developer.android.com/privacy-and-security/local-network-permission) | ローカルネットワークの入出力にもOS・target SDKに応じた権限制御がある。Android 16のopt-inとAndroid 17/target 37以降の規則を区別する | Focus Visionの実際のOSとアプリtarget SDKに照らした動作。実装では対象SDKを36へ固定する。現在の機器がAndroid 17であるとは仮定しない |
| [Unity 6000.6：Android permissions](https://docs.unity3d.com/6000.6/Documentation/Manual/android-permissions-in-unity.html) | Manifestと実行時権限を機能に合わせて扱う | UnityのHTTPクライアントとManifestをAndroidビルド・実機で検証すること |

インターネットは外部回線、Wi-Fiはこの場合の端末間の無線接続であり、両者を同一視しない。会場の回線がなくてもPCが作ったLANで通信する案は成立するが、個別機器での接続確認は必要である。

## 本研究への設計判断

被験者へHMD内の設定を求めると、実施者が状態を見にくいだけでなく、メニュー習熟・数値入力・操作中の注意を研究課題へ余計に加える。実施者のブラウザーへ設定・進行・診断を分け、HMDには必要な提示だけを出す。これは本研究の操作設計上の判断で、上記技術資料が学習効果を実証したという意味ではない。

計測・速度推定・連続FB・元データ保存はゴーグル内で行う。ブラウザーは開始・停止・状態確認に使い、無線往復遅延を連続FBの更新経路へ入れない。通信断でFBを続けて対照条件のように扱うこと、自動再開や二重開始を許すことは避け、異常と介入逸脱を記録する。

PCのAP対応は環境の読取りで確認済み。ブラウザーからの手動操作とゴーグル内の計測・保存を実装し、ソフトの試験を行う。ネットワークの有効化、Focus Visionとの実疎通・現地性能は未確認である。メーカーの最大5台対応から、PCのWi-Fiと同時使用した現地性能まで推論しない。

## 実装経路を選ぶための追加資料

| 一次資料 | 用途と限界 |
|---|---|
| [Unity 6000.6：UnityWebRequest](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/Networking.UnityWebRequest.html) | Unity側をHTTPクライアントにする。AndroidでサーバーAPIの互換性を仮定せず、PCへ操作とページ配信を置く |
| [Unity：insecureHttpOption](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/PlayerSettings-insecureHttpOption.html) | 平文HTTPを許可するPlayer設定。許可しただけで相手の認証や接続先制限が成立するわけではない |
| [Android：LinkProperties.getRoutes](https://developer.android.com/reference/android/net/LinkProperties#getRoutes())・[接続状態](https://developer.android.com/develop/connectivity/network-ops/reading-network-state) | Wi-Fiの経路からゲートウェイを求める。古いgetDhcpInfoや固定のAPアドレスへ依存しない。実機で経路取得を確認する |
| [Android：Network security configuration](https://developer.android.com/privacy-and-security/security-config) | cleartextの扱いをManifest・対象SDKと合わせる。実際に生成されたManifestとAPKを検査する |
| [Node.js 24：HTTP](https://nodejs.org/docs/latest-v24.x/api/http.html) | PCの標準HTTPサーバー、要求・応答とストリーム。アプリの認証・古い命令の扱いは本研究の実装で定める |
| [Chrome：Headless mode](https://developer.chrome.com/docs/automation-and-testing/headless) | 配布するWeb画面の実描画を撮影。ゴーグルの光学系や無線通信を再現する機能ではない |

PC側の専用コマンド1回起動はユーザー確認済みの方針。実装はPC内だけの操作ページ、APのアドレスだけの機器用待受け、装着前の接続承認、命令識別と状態照合、500 ms確認・初期3秒有効期限を採用する。これらは技術資料を基にした本研究の設計で、メーカーの保証値ではない。

ブラウザーの有効期限とPC–HMD間の有効期限を順に足して停止が遅れないよう、残り有効時間を伝え、往復時間を保守的に差し引く。実時間の合成入力試験と、実際のWi-Fi・本体で測る停止時間は別の証拠として残す。

## PCの正式時計と開始合図

[Performance.now](https://developer.mozilla.org/en-US/docs/Web/API/Performance/now)は時刻調整の影響を受けない単調時計。[Web Audioのスケジュール](https://www.w3.org/TR/webaudio/#dom-audiocontext-currenttime)で準備音・開始音をPCで予約する。HMDへの通信応答から正式時間を算出しない。音声コンテキストの予定時刻と、実際にスピーカーから出た時刻は別である。ブラウザー再起動をまたぐ時計を結合せず、開始予約だけで終了がない試行は未完了として確認する。確認日：2026-10-21。
