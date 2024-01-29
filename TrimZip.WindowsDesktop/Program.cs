using System;
using System.Text;
using Palmtree.Application;
using Palmtree.IO;

namespace TrimZip.WindowsDesktop
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Environment.CurrentDirectory = typeof(Program).Assembly.GetBaseDirectory().FullName;
            var launcher = new ConsoleApplicationLauncher("ziptrim", Encoding.UTF8);
            launcher.Launch(args);
        }
    }
}
