using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace WheelchairSonification.Runtime
{
    public interface IExportDestination { string Write(string zipPath, string name); }

    public static class SessionExporter
    {
        public static string CreateArchive(string root, string outputDirectory, string activeDirectory = null)
        {
            if (!Directory.Exists(root)) throw new InvalidOperationException("取り出せる記録がありません。");
            var sessions = Directory.GetDirectories(root).Where(d => File.Exists(Path.Combine(d, "session.json")) &&
                (activeDirectory == null || Path.GetFullPath(d) != Path.GetFullPath(activeDirectory))).OrderBy(d => d, StringComparer.Ordinal).ToArray();
            if (sessions.Length == 0) throw new InvalidOperationException("終了・中断済みの記録がありません。");
            Directory.CreateDirectory(outputDirectory);
            string final = Path.Combine(outputDirectory, "WheelchairSessions-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".zip");
            string temporary = final + ".partial";
            try
            {
                using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
                {
                    var hashes = new StringBuilder();
                    foreach (var directory in sessions)
                    {
                        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("リンクを含む記録は書き出せません。");
                        foreach (var file in Directory.GetFiles(directory).OrderBy(p => p, StringComparer.Ordinal))
                        {
                            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("リンクを含む記録は書き出せません。");
                            string entry = Path.GetFileName(directory) + "/" + Path.GetFileName(file);
                            using (var sha = SHA256.Create()) using (var input = File.OpenRead(file))
                                hashes.Append(BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant()).Append("  ").Append(entry).Append('\n');
                            zip.CreateEntryFromFile(file, entry, CompressionLevel.Fastest);
                        }
                    }
                    using var writer = new StreamWriter(zip.CreateEntry("SHA256SUMS.txt").Open(), new UTF8Encoding(false));
                    writer.Write(hashes.ToString());
                }
                File.Move(temporary, final);
                return final;
            }
            catch { if (File.Exists(temporary)) File.Delete(temporary); throw; }
        }
    }
}
