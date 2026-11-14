using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Sv.Web;

[assembly: SupportedOSPlatform("browser")]

namespace Sv.Wasm
{
    public static partial class Program
    {
        public static async Task Main()
        {
            await Task.Delay(-1);
        }

        [JSExport]
        public static string Check(string values, string rules, string cards, string templates) => SvWeb.Check(values, rules, cards, templates);

        [JSExport]
        public static string Canonical(string values, string rules, string cards, string templates) => SvWeb.Canonical(values, rules, cards, templates);

        [JSExport]
        public static string Simulate(string values, string rules, string cards, string templates, string scenario) => SvWeb.Simulate(values, rules, cards, templates, scenario);
    }
}
