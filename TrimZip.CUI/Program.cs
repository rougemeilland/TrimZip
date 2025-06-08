using System.Text;
using Palmtree;

namespace TrimZip.CUI
{
    internal sealed class Program
    {
        private static int Main(string[] args)
        {
            var application = new TrimZipApplication(typeof(Program).Assembly.GetAssemblyFileNameWithoutExtension(), Encoding.UTF8);
            return application.Run(args);
        }
    }
}
