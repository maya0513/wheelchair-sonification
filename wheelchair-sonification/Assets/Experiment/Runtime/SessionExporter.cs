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
        public static string CreateResearchBundle(string sessionsRoot, string visitsRoot, string outputDirectory)
        {
            var roots = new[] { (path: sessionsRoot, name: File.Exists(Path.Combine(sessionsRoot, "session.json")) ? "ResearchSessions/" + Path.GetFileName(sessionsRoot) : "ResearchSessions"), (path: visitsRoot, name: "ResearchVisits") };
            if (!roots.Any(r => Directory.Exists(r.path) && Directory.GetFiles(r.path, "*", SearchOption.AllDirectories).Length > 0))
                throw new InvalidOperationException("取り出せる記録がありません。");
            Directory.CreateDirectory(outputDirectory);
            string final = Path.Combine(outputDirectory, "WheelchairResearch-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".zip");
            string temporary = final + ".partial";
            try
            {
                using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
                {
                    var hashes = new StringBuilder();
                    foreach (var root in roots)
                    {
                        if (!Directory.Exists(root.path)) continue;
                        var dirs = new System.Collections.Generic.Stack<string>(); dirs.Push(root.path);
                        while (dirs.Count > 0)
                        {
                            string directory = dirs.Pop();
                            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("リンクを含む記録は回収できません。");
                            foreach (var sub in Directory.GetDirectories(directory)) dirs.Push(sub);
                            foreach (var file in Directory.GetFiles(directory).OrderBy(p => p, StringComparer.Ordinal))
                            {
                                if (file.EndsWith(".partial", StringComparison.Ordinal)) continue;
                                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("リンクを含む記録は回収できません。");
                                string entry = root.name + "/" + Path.GetRelativePath(root.path, file).Replace('\\', '/');
                                using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                                using var output = zip.CreateEntry(entry, CompressionLevel.Fastest).Open();
                                using var sha = SHA256.Create();
                                var buffer = new byte[65536]; int count;
                                while ((count = input.Read(buffer, 0, buffer.Length)) > 0) { output.Write(buffer, 0, count); sha.TransformBlock(buffer, 0, count, null, 0); }
                                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                                hashes.Append(BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant()).Append("  ").Append(entry).Append('\n');
                            }
                        }
                    }
                    using var writer = new StreamWriter(zip.CreateEntry("SHA256SUMS.txt").Open(), new UTF8Encoding(false)); writer.Write(hashes.ToString());
                }
                File.Move(temporary, final); return final;
            }
            catch { if (File.Exists(temporary)) File.Delete(temporary); throw; }
        }

        public static string CreateTrialArchive(string trialDirectory, string outputDirectory)
        {
            if (!File.Exists(Path.Combine(trialDirectory, "session.json"))) throw new InvalidOperationException("試行記録がありません。");
            return CreateResearchBundle(trialDirectory, Path.Combine(outputDirectory, "nonexistent-visits"), outputDirectory);
        }

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
