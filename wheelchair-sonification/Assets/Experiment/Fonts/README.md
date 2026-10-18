# 同梱フォント

日本語と英数字の表示にNoto Sans CJK JP Regularを使用する。

- 配布元：[Noto CJK公式リポジトリ](https://github.com/notofonts/noto-cjk/tree/main/Sans/OTF/Japanese)
- 同梱ファイル：`../Resources/DemoJapanese.otf`（元の`NotoSansCJKjp-Regular.otf`をファイル名だけ変更）
- ライセンス：SIL Open Font License。本文と著作権表示は[Noto-OFL.txt](../../StreamingAssets/Noto-OFL.txt)に保持する。

フォントは`Resources/`、ライセンス本文は`StreamingAssets/`から各ビルドへ含め、OSに日本語フォントがない環境でも表示できるようにする。APK内では`assets/Noto-OFL.txt`として本文を取り出せる。Linuxデモでは`ThirdPartyNotices/Noto-OFL.txt`にも配置する。
