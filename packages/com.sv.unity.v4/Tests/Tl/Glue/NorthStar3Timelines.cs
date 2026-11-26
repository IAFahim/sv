using System;
using System.Runtime.InteropServices;
using Sv.Host;
using Tl;

namespace Sv.Kernels.NorthStar3.Glue
{
    [TimelineSystem]
    public ref partial struct TlPlainGeneratorRunsOnlyWhereATimelineSystemIsDeclared
    {
    }

    public sealed unsafe class NorthStar3Timelines : IDisposable
    {
        readonly TimelineWorld timelines;
        readonly PlaybackTable players;
        readonly KindStore kinds;
        readonly TimelineRequests requestsFromSvEvents;
        readonly TlWorkers? tlWorkers;
        readonly PoisonOut[] poisonOutbox;
        readonly BleedingOut[] bleedingOutbox;

        public NorthStar3Timelines(byte[] bakedLibrary, int rows, int workers)
        {
            timelines = new TimelineWorld(bakedLibrary);
            players = new PlaybackTable(rows);
            kinds = new KindStore(timelines, players);
            requestsFromSvEvents = new TimelineRequests(timelines);
            tlWorkers = workers > 1 ? new TlWorkers(workers) : null;
            poisonOutbox = new PoisonOut[rows];
            bleedingOutbox = new BleedingOut[rows];
        }

        public ushort Find(string timeline) => timelines.Find(timeline);

        public void Start(int row, ushort timeline) => players.Start(row, timeline);

        public void TickAndFillOutboxes()
        {
            timelines.Tick(players, true);
            var outboxColumns = new Tl.Generated.TlAuto_Sv_NorthStar3 { C0_PoisonOut = poisonOutbox, C1_BleedingOut = bleedingOutbox };
            if (tlWorkers != null)
            {
                outboxColumns.Run(timelines, players, true, tlWorkers);
            }
            else
            {
                outboxColumns.Run(timelines, players, true);
            }
        }

        public Span<int> PoisonCells => MemoryMarshal.Cast<PoisonOut, int>(poisonOutbox.AsSpan());

        public Span<int> BleedingCells => MemoryMarshal.Cast<BleedingOut, int>(bleedingOutbox.AsSpan());

        public void StartAndStopFromSvEvents(HostEvent* events, int count) => requestsFromSvEvents.Apply(players, events, count);

        public void Dispose()
        {
            tlWorkers?.Dispose();
            kinds.Dispose();
            players.Dispose();
            timelines.Dispose();
        }
    }
}
