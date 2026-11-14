using System;

namespace Sv.Host
{
    public static class Outbox
    {
        public static int Send<TKernels>(FrameLoop<TKernels> host, Span<int> cells, int channel, int kindLane, int targetLane)
            where TKernels : struct, IPackKernels
        {
            var sent = 0;
            for (var slot = 0; slot < cells.Length; slot++)
            {
                var amount = cells[slot];
                if (amount == 0)
                {
                    continue;
                }

                var kind = kindLane < 0 ? 0 : host.Read(slot, kindLane);
                var aimed = targetLane < 0 ? 0 : host.Read(slot, targetLane);
                var target = aimed <= 0 ? slot : aimed - 1;

                kind = kind < 0 ? 0 : kind > 255 ? 255 : kind;
                sent += host.Send(target, slot, kind, channel, amount, 1, Origin.Timeline) ? 1 : 0;
                cells[slot] = 0;
            }

            return sent;
        }
    }
}
