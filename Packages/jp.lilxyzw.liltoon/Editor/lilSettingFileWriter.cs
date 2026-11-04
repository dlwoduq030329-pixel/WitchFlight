#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;

namespace lilToon
{
    // Local compatibility fix: Windows rejects truncating or replacing a mapped
    // settings file (ERROR_USER_MAPPED_FILE / 1224) during build postprocessing.
    internal static class lilSettingFileWriter
    {
        internal static void WriteJson(string path, string json)
        {
            if(json == null) throw new ArgumentNullException(nameof(json));
            byte[] content = new UTF8Encoding(false, true).GetBytes(json);

            using(var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
            {
                // Never shrink a mapped file. JSON permits trailing whitespace;
                // padding erases every byte of the previous, possibly longer JSON.
                // Work in bytes, not characters, so non-ASCII settings are safe too.
                int length = checked((int)Math.Max(stream.Length, content.Length));
                byte[] output = new byte[length];
                Buffer.BlockCopy(content, 0, output, 0, content.Length);
                for(int i = content.Length; i < length; i++) output[i] = (byte)' ';

                bool unchanged = stream.Length == length;
                if(unchanged)
                {
                    for(int i = 0; i < length; i++)
                    {
                        if(stream.ReadByte() == output[i]) continue;
                        unchanged = false;
                        break;
                    }
                }
                if(unchanged) return;

                stream.Position = 0;
                stream.Write(output, 0, output.Length);
                stream.Flush();
                // No SetLength / FileMode.Create / File.Replace here: all can fail
                // while mapped. Let genuine access/write failures reach the caller.
            }
        }
    }
}
#endif
