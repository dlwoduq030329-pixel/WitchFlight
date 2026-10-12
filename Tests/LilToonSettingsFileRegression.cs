using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using lilToon;

// Compiled with the production writer by LilToonSettingsFileRegression.ps1.
// This never opens the project's real ProjectSettings/lilToonSetting.json.
internal static class LilToonSettingsFileRegression
{
    private static void Assert(bool condition, string message)
    {
        if(!condition) throw new Exception(message);
    }

    private static void AssertContent(string path, string expected)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Assert(bytes.Length == 0 || bytes[0] != 0xef, "Unexpected UTF-8 BOM.");
        Assert(new UTF8Encoding(false, true).GetString(bytes).TrimEnd(' ') == expected,
            "Invalid content or stale bytes left after JSON.");
    }

    public static int Main()
    {
        string directory = Path.Combine(Path.GetTempPath(), "WitchFlight-lilToon-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        try
        {
            const string initial = "{\"enabled\":false,\"name\":\"initial settings\"}";
            lilSettingFileWriter.WriteJson(path, initial);
            AssertContent(path, initial);
            Console.WriteLine("PASS: new file, UTF-8 without BOM");

            using(var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using(var map = MemoryMappedFile.CreateFromFile(source, null, 0, MemoryMappedFileAccess.Read, null, HandleInheritability.None, true))
            using(var view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
            {
                bool reproduced = false;
                try
                {
                    using(var legacy = new StreamWriter(path, false)) legacy.Write(initial);
                }
                catch(IOException e) { reproduced = (e.HResult & 0xffff) == 1224; }
                Assert(reproduced, "Expected the original writer to reproduce Windows error 1224.");
                AssertContent(path, initial);
                Console.WriteLine("PASS: legacy writer reproduces 1224 on a mapped file");

                string sameSize = initial.Replace("initial", "changed");
                lilSettingFileWriter.WriteJson(path, sameSize);
                AssertContent(path, sameSize);
                Console.WriteLine("PASS: mapped, same-size save");

                long originalLength = new FileInfo(path).Length;
                const string shortJson = "{\"enabled\":true}";
                lilSettingFileWriter.WriteJson(path, shortJson);
                AssertContent(path, shortJson);
                Assert(new FileInfo(path).Length == originalLength, "Mapped file was truncated.");
                Console.WriteLine("PASS: mapped, shorter save with clean whitespace padding");

                string longJson = "{\"name\":\"" + new string('x', 9000) + "\"}";
                lilSettingFileWriter.WriteJson(path, longJson);
                AssertContent(path, longJson);
                Console.WriteLine("PASS: mapped, larger save beyond mapping size");

                const string unicodeJson = "{\"name\":\"마녀의 비행 ✨\",\"enabled\":false}";
                for(int i = 0; i < 10; i++)
                {
                    lilSettingFileWriter.WriteJson(path, unicodeJson);
                    AssertContent(path, unicodeJson);
                }
                Assert(new FileInfo(path).Length == Encoding.UTF8.GetByteCount(longJson), "Repeated saves grew the file.");
                Console.WriteLine("PASS: mapped, Unicode and repeated saves do not accumulate padding");
            }

            const string existingJson = "{\"enabled\":true}";
            File.WriteAllText(path, existingJson, new UTF8Encoding(true));
            lilSettingFileWriter.WriteJson(path, existingJson);
            AssertContent(path, existingJson);
            Console.WriteLine("PASS: legacy BOM removed without leaving stale bytes");

            File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            DateTime timestamp = File.GetLastWriteTimeUtc(path);
            lilSettingFileWriter.WriteJson(path, existingJson);
            Assert(File.GetLastWriteTimeUtc(path) == timestamp, "Unchanged content was rewritten.");
            Console.WriteLine("PASS: unchanged content does not rewrite the file");

            bool denied = false;
            using(var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                try { lilSettingFileWriter.WriteJson(path, "{\"enabled\":false}"); }
                catch(IOException) { denied = true; }
            }
            Assert(denied, "Genuine sharing violation must be reported, not silently ignored.");
            AssertContent(path, existingJson);
            lilSettingFileWriter.WriteJson(path, "{\"enabled\":false}");
            AssertContent(path, "{\"enabled\":false}");
            Console.WriteLine("PASS: lock failure preserves file; a later save succeeds");

            bool rejected = false;
            try { lilSettingFileWriter.WriteJson(path, null); }
            catch(ArgumentNullException) { rejected = true; }
            Assert(rejected, "Null input was not rejected.");
            AssertContent(path, "{\"enabled\":false}");
            Console.WriteLine("PASS: invalid input does not touch existing settings");
            return 0;
        }
        finally
        {
            // Only this test's exact file and then its empty directory are removed.
            File.Delete(path);
            Directory.Delete(directory);
        }
    }
}
