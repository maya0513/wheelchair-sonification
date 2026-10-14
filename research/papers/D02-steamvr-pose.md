# D02 SteamVR Unity Plugin: Behaviour Pose

提供元：Valve。閲覧日：2026-10-14。公式技術資料であり、介入効果を証明する研究論文ではない。

- [公式資料1](https://valvesoftware.github.io/steamvr_unity_plugin/api/Valve.VR.SteamVR_Behaviour_Pose.html)

## 確認した内容

位置・姿勢、isValid、接続・tracking変更イベント、GetDeviceIndex等の公式APIを確認。

## 本研究での利用

配列の一時的なdeviceIndexだけで左右を決めず、起動時にserial/役割を対応づける。tracking lossを0座標の観測として扱わない。

参照：[装置案](../../docs/feedback-specification.md)、[EEG案](../../docs/archive/exploratory-eeg-on-hold.md)。
