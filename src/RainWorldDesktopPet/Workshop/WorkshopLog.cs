using System;
using System.Diagnostics;
using System.IO;

namespace RainWorldDesktopPet.Workshop
{
    public sealed class WorkshopLog
    {
        private readonly object sync = new object();
        private readonly string path;

        public WorkshopLog(bool verbose)
            : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SlugcatInMyMonitor", "workshop.log"), verbose)
        {
        }

        public WorkshopLog(string path, bool verbose)
        {
            this.path = path;
            VerboseEnabled = verbose;
        }

        public bool VerboseEnabled { get; private set; }

        public void Info(string category, string message)
        {
            Write("INFO", category, message);
        }

        public void Warning(string category, string message)
        {
            Write("WARN", category, message);
        }

        public void Verbose(string category, string message)
        {
            if (VerboseEnabled) Write("DEBUG", category, message);
        }

        private void Write(string level, string category, string message)
        {
            string line = DateTime.Now.ToString("u") + " " + level + " [" + category + "] " + message;
            Debug.WriteLine(line);
            try
            {
                lock (sync)
                {
                    string directory = Path.GetDirectoryName(path);
                    if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                    if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024)
                    {
                        string previous = path + ".previous";
                        if (File.Exists(previous)) File.Delete(previous);
                        File.Move(path, previous);
                    }
                    File.AppendAllText(path, line + Environment.NewLine);
                }
            }
            catch (Exception)
            {
                // Logging must never make an external mod failure fatal.
            }
        }
    }
}
