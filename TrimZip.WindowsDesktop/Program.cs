using System;
using System.Text;
using Palmtree;
using Palmtree.IO.Console;

namespace TrimZip.WindowsDesktop
{
    internal static class Program
    {
        private class DesctopApplication
            : WindowsDesktopApplication
        {
            private readonly Func<int> _main;

            public DesctopApplication(Func<int> main)
            {
                _main = main;
            }

            protected override int Main()
            {
                var exitCode = _main();
                TinyConsole.Beep();
                _ = TinyConsole.ReadLine();
                return exitCode;
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            var application = new TrimZipApplication("TrimZip for Desktop", Encoding.UTF8);
            var desktopApplication = new DesctopApplication(() => _ = application.Run(args));
            _ = desktopApplication.Run();
        }
    }
}
