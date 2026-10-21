using System;
using System.IO;
using UnityEngine;

namespace WheelchairSonification.Runtime
{
    public static class FileStorage
    {
        // Engineering reserve, not a measurement acceptance criterion.
        public const long ReserveBytes = 64L * 1024 * 1024;
        public static void EnsureReserve(string path)
        {
            Directory.CreateDirectory(path);
            long? available = Available(path);
            if (available.HasValue && available.Value < ReserveBytes) throw new IOException("保存容量が64 MiB未満です。回収・空き容量を確認してください。");
        }
        public static long? Available(string path)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var stat = new AndroidJavaObject("android.os.StatFs", path);
            return stat.Call<long>("getAvailableBytes");
#else
            try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))).AvailableFreeSpace; }
            catch (ArgumentException) { return null; }
#endif
        }
    }
}
