using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using RainWorldDesktopPet.Desktop;
using RainWorldDesktopPet.RainWorld;
using RainWorldDesktopPet.UI;
using RainWorldDesktopPet.Creature;

namespace RainWorldDesktopPet
{
    internal static class Program
    {
        private static readonly string ErrorLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SlugcatInMyMonitor", "errors.log");

        [STAThread]
        private static void Main(string[] args)
        {
            bool ownsMutex;
            using (Mutex mutex = new Mutex(true, "Local\\SlugcatInMyMonitor", out ownsMutex))
            {
                if (!ownsMutex) return;
                NativeMethods.EnableDpiAwareness();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs eventArgs)
                {
                    LogException(eventArgs.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs eventArgs)
                {
                    LogException(eventArgs.ExceptionObject as Exception ?? new Exception("Unknown unhandled exception"));
                };

                string explicitPath = ReadOption(args, "--rain-world");
                RainWorldLocator locator = new RainWorldLocator();
                RainWorldInstallation installation = locator.Locate(explicitPath);
                if (installation == null)
                {
                    installation = AskForInstallation(locator);
                    if (installation == null) return;
                }

                bool debug = HasFlag(args, "--debug");
                SlugcatId selectedSlugcat = ReadSlugcat(ReadOption(args, "--slugcat"));
                Application.Run(new LayeredOverlayWindow(installation, debug, selectedSlugcat));
            }
        }

        private static RainWorldInstallation AskForInstallation(RainWorldLocator locator)
        {
            MessageBox.Show(
                "Rain World 설치 경로를 자동으로 찾지 못했습니다. 다음 창에서 RainWorld.exe가 있는 폴더를 선택해 주세요.",
                "Slugcat in My Monitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Rain World installation folder";
                dialog.ShowNewFolderButton = false;
                if (dialog.ShowDialog() != DialogResult.OK) return null;
                if (!locator.IsValid(dialog.SelectedPath))
                {
                    MessageBox.Show("선택한 폴더에서 RainWorld.exe와 RainWorld_Data를 찾지 못했습니다.",
                        "Invalid Rain World folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return null;
                }
                return locator.Locate(dialog.SelectedPath);
            }
        }

        private static string ReadOption(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                    return args[i].Substring(name.Length + 1).Trim('"');
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    return args[i + 1];
            }
            return null;
        }

        private static bool HasFlag(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static SlugcatId ReadSlugcat(string value)
        {
            SlugcatId result;
            return !string.IsNullOrWhiteSpace(value) && SlugcatProfiles.TryParse(value, out result)
                ? result : SlugcatId.Default;
        }

        public static void LogException(Exception exception)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ErrorLogPath));
                File.AppendAllText(ErrorLogPath, DateTime.Now.ToString("u") + Environment.NewLine + exception + Environment.NewLine + Environment.NewLine);
            }
            catch (Exception)
            {
            }
        }
    }
}
