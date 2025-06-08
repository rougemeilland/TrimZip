using System;
using System.Text;
using Palmtree.Application;

namespace TrimZip.WindowsDesktop
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            var launcher = new ConsoleApplicationLauncher("ziptrim", Encoding.UTF8);
            launcher.Launch(args);
        }
    }
}
