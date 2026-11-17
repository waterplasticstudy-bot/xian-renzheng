using System;
using System.IO;
using System.Text;

namespace XianAuth
{
    public static class DailyLog
    {
        static readonly object Gate = new object();
        public static AppPaths Paths { get; set; }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Error(string message)
        {
            Write("ERROR", message);
        }

        static void Write(string level, string message)
        {
            try
            {
                AppPaths paths = Paths ?? AppPaths.ForBase();
                Directory.CreateDirectory(paths.Logs);
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + level + " "
                    + SecretRedactor.Redact(message ?? "") + Environment.NewLine;
                string file = Path.Combine(paths.Logs, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                lock (Gate)
                {
                    var info = new FileInfo(file);
                    if (info.Exists && info.Length > 512 * 1024)
                    {
                        string rotated = Path.Combine(paths.Logs, DateTime.Now.ToString("yyyy-MM-dd") + "." + DateTime.Now.ToString("HHmmss") + ".log");
                        File.Move(file, rotated);
                    }
                    File.AppendAllText(file, line, new UTF8Encoding(false));
                    string[] files = Directory.GetFiles(paths.Logs, "*.log");
                    DateTime cutoff = DateTime.Now.Date.AddDays(-14);
                    for (int i = 0; i < files.Length; i++)
                    {
                        if (File.GetLastWriteTime(files[i]) < cutoff)
                        {
                            try { File.Delete(files[i]); } catch (IOException) { }
                        }
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
