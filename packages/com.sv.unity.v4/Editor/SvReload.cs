using UnityEditor;

namespace Sv.Unity.Editor
{
    [InitializeOnLoad]
    public static class SvReload
    {
        static SvReload()
        {
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReloadHandler;
        }

        public static int BeforeAssemblyReload()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReloadHandler;
            return SvWorlds.DisposeAll();
        }

        static void BeforeAssemblyReloadHandler() => BeforeAssemblyReload();
    }
}
