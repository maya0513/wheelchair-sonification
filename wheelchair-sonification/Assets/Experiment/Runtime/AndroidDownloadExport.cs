using System;
using System.IO;
using UnityEngine;

namespace WheelchairSonification.Runtime
{
    public sealed class AndroidDownloadExport : IExportDestination
    {
        public const string RelativeDirectory = "Download/WheelchairSonificationExports/";
#if !UNITY_ANDROID || UNITY_EDITOR
        private readonly string previewDirectory = Path.Combine(Application.persistentDataPath, "PreviewExports");
#endif
        public string Write(string zipPath, string name)
        {
            if (Path.GetFileName(name) != name) throw new ArgumentException("Invalid export name.");
#if UNITY_ANDROID && !UNITY_EDITOR
            // Called by a worker thread so ZIP copying does not stall head tracking.
            AndroidJNI.AttachCurrentThread();
            AndroidJavaObject resolver = null, uri = null, stream = null;
            try
            {
                using var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unity.GetStatic<AndroidJavaObject>("currentActivity");
                resolver = activity.Call<AndroidJavaObject>("getContentResolver");
                using var values = new AndroidJavaObject("android.content.ContentValues");
                values.Call("put", "_display_name", name);
                values.Call("put", "mime_type", "application/zip");
                values.Call("put", "relative_path", RelativeDirectory);
                using var pending = new AndroidJavaObject("java.lang.Integer", 1);
                values.Call("put", "is_pending", pending);
                using var media = new AndroidJavaClass("android.provider.MediaStore$Downloads");
                using var collection = media.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");
                uri = resolver.Call<AndroidJavaObject>("insert", collection, values);
                if (uri == null) throw new IOException("Downloadに書き出し先を作れませんでした。");
                stream = resolver.Call<AndroidJavaObject>("openOutputStream", uri, "w");
                if (stream == null) throw new IOException("Downloadの書き出し先を開けませんでした。");
                using (var input = File.OpenRead(zipPath))
                {
                    byte[] buffer = new byte[65536]; int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0) stream.Call("write", buffer, 0, count);
                }
                stream.Call("flush"); stream.Call("close"); stream.Dispose(); stream = null;
                using var finished = new AndroidJavaObject("android.content.ContentValues");
                using var zero = new AndroidJavaObject("java.lang.Integer", 0);
                finished.Call("put", "is_pending", zero);
                if (resolver.Call<int>("update", uri, finished, null, null) != 1) throw new IOException("Downloadへの書き出しを確定できませんでした。");
                return RelativeDirectory + name;
            }
            catch
            {
                if (uri != null && resolver != null) try { resolver.Call<int>("delete", uri, null, null); } catch { }
                throw;
            }
            finally
            {
                if (stream != null) { try { stream.Call("close"); } catch { } stream.Dispose(); }
                uri?.Dispose(); resolver?.Dispose(); AndroidJNI.DetachCurrentThread();
            }
#else
            string directory = previewDirectory;
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, name);
            File.Copy(zipPath, destination, false);
            return destination;
#endif
        }
    }
}
