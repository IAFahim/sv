namespace Sv.Columns
{
    internal static unsafe class Kernels
    {
        public static bool Supported => false;

        public static void ResolveChunk(PackTables* pack, TemplateTable* template, int* lanes, SlotGate* gates, ulong alive, Scratch area)
        {
        }
    }
}
