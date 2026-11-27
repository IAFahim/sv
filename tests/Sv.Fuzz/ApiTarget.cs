using Sv.Columns;
using Sv.Columns.Tests;

namespace Sv.Fuzz;

public static class ApiTarget
{
    public static string Run(CompiledPack pack, ulong seed)
    {
        var dice = new Dice(seed ^ 0xA91ul);
        var frames = dice.Range(4, 40);
        var capacity = new[] { 64, 128, 256, 100 }[dice.Next(4)];
        var writers = dice.Range(1, 3);
        var parts = dice.Range(1, 4);
        var kernel = (KernelChoice)dice.Next(3);
        var history = dice.Chance(70) ? frames + 2 : dice.Range(1, Math.Max(1, frames));
        var fused = dice.Chance(30);
        using var world = new ColumnWorld(pack, capacity, writers, parts, kernel, history);
        var lanes = pack.LaneNames.Count;
        var templates = pack.TemplateNames.Count;
        var channels = pack.ChannelNames.Count;
        var resimulations = 0;

        List<int> Alive()
        {
            var slots = new List<int>();
            for (var s = 0; s < world.Capacity; s++)
            {
                if (world.IsAlive(s))
                {
                    slots.Add(s);
                }
            }

            return slots;
        }

        int AnySlot(List<int> live) => dice.Next(6) switch
        {
            0 => -1 - dice.Next(5),
            1 => world.Capacity + dice.Next(70),
            2 => int.MaxValue - dice.Next(3),
            3 => int.MinValue + dice.Next(3),
            _ => live.Count > 0 ? live[dice.Next(live.Count)] : dice.Next(world.Capacity),
        };

        void Refused(string what, Action call, params Type[] allowed)
        {
            var before = world.Digest();
            var frameBefore = world.Frame;
            try
            {
                call();
            }
            catch (Exception error) when (allowed.Any(t => t.IsInstanceOfType(error)))
            {
                if (world.Digest() != before || world.Frame != frameBefore)
                {
                    throw new Finding("refusal-changed-state", what + " was refused (" + error.GetType().Name + ") but changed the world");
                }

                return;
            }
            catch (Finding)
            {
                throw;
            }
            catch (Exception error)
            {
                throw new Finding("wrong-exception", what + " threw " + error.GetType().Name + ": " + error.Message);
            }

            throw new Finding("accepted-misuse", what + " returned normally");
        }

        var argument = new[] { typeof(ArgumentException) };
        var state = new[] { typeof(InvalidOperationException) };

        void Misuse(List<int> live)
        {
            switch (dice.Next(16))
            {
                case 0:
                    var deadSlot = Enumerable.Range(0, world.Capacity).FirstOrDefault(s => !world.IsAlive(s), -1);
                    var bad = dice.Chance(50) ? deadSlot : AnySlot(new List<int>());
                    if (bad >= 0 && bad < world.Capacity && world.IsAlive(bad))
                    {
                        return;
                    }

                    Refused("Despawn(" + bad + ")", () => world.Despawn(bad), argument);
                    break;
                case 1:
                    var t = dice.Chance(50) ? -1 - dice.Next(3) : templates + dice.Next(3);
                    Refused("Spawn(" + t + ")", () => world.Spawn(t), argument);
                    break;
                case 2:
                    if (live.Count == 0)
                    {
                        return;
                    }

                    var slot = live[dice.Next(live.Count)];
                    var lane = dice.Next(4) switch { 0 => -1, 1 => lanes - 2, 2 => lanes - 1, _ => lanes + dice.Next(5) };
                    Refused("Set(" + slot + ", lane " + lane + ")", () => world.Set(slot, lane, 7), argument);
                    break;
                case 3:
                    if (live.Count == 0)
                    {
                        return;
                    }

                    var getSlot = live[dice.Next(live.Count)];
                    var getLane = dice.Chance(50) ? -1 : lanes + dice.Next(3);
                    Refused("Get(" + getSlot + ", lane " + getLane + ")", () => world.Get(getSlot, getLane), argument);
                    break;
                case 4:
                    var chunk = dice.Chance(50) ? -1 : world.Chunks + dice.Next(3);
                    Refused("ChunkAlive(" + chunk + ")", () => world.ChunkAlive(chunk), argument);
                    break;
                case 5:
                    Refused("EndFrame() outside a frame", () => world.EndFrame(), state);
                    break;
                case 6:
                    Refused("Resolve(0) outside a frame", () => world.Resolve(0), state);
                    break;
                case 7:
                    Refused("Reduce(0) outside a frame", () => world.Reduce(0), state);
                    break;
                case 8:
                    var oldest = world.OldestFrame;
                    var outside = dice.Chance(50) ? oldest - 1 - dice.Next(3) : world.Frame + 1 + dice.Next(3);
                    if (outside < 0 && oldest == 0 && dice.Chance(50))
                    {
                        outside = int.MinValue;
                    }

                    Refused("Rewind(" + outside + ") outside the window " + oldest + ".." + world.Frame, () => world.Rewind(outside), argument);
                    break;
                case 9:
                    var late = dice.Chance(50) ? world.Frame : world.OldestFrame - 1;
                    Refused("Inputs(" + late + ") outside the window", () => world.Inputs(late), argument);
                    break;
                case 10:
                    if (world.Frame - 1 < world.OldestFrame)
                    {
                        return;
                    }

                    var inputs = world.Inputs(dice.Range(world.OldestFrame, world.Frame - 1));
                    var index = dice.Chance(50) ? -1 : inputs.RecordCount;
                    Refused("ReplaceRecord(" + index + ")", () => inputs.ReplaceRecord(index, 0), argument);
                    break;
                case 11:
                    if (world.Frame - 1 < world.OldestFrame)
                    {
                        return;
                    }

                    var log = world.Inputs(dice.Range(world.OldestFrame, world.Frame - 1));
                    var at = dice.Chance(50) ? -1 : log.ActionCount + 1;
                    Refused("InsertAction(" + at + ")", () => log.InsertAction(at, ColumnAction.Spawn(0)), argument);
                    break;
                case 12:
                    if (live.Count == 0)
                    {
                        return;
                    }

                    var eventSlot = live[dice.Next(live.Count)];
                    var eventIndex = dice.Chance(50) ? -1 : pack.EventNames.Count + dice.Next(3);
                    Refused("Event(" + eventSlot + ", " + eventIndex + ")", () => world.Event(eventSlot, eventIndex), argument);
                    break;
                case 13:
                    Refused("Writer(" + writers + ")", () => world.Writer(writers), new[] { typeof(IndexOutOfRangeException), typeof(ArgumentException) });
                    break;
                case 14:
                    var get = AnySlot(new List<int>());
                    if (get >= 0 && get < world.Capacity && world.IsAlive(get))
                    {
                        return;
                    }

                    Refused("TemplateOf(" + get + ")", () => world.TemplateOf(get), argument);
                    break;
                default:
                    InFrameMisuse();
                    break;
            }
        }

        void InFrameMisuse()
        {
            world.BeginFrame();
            Refused("BeginFrame() twice", () => world.BeginFrame(), state);
            Refused("Spawn inside a frame", () => world.Spawn(0), state);
            MidFrameSend(state);
            if (world.History > 0 && world.Frame - 1 >= world.OldestFrame)
            {
                Refused("AddRecord to the running frame", () => world.Inputs(world.Frame - 1).AddRecord(0), state);
                Refused("AddAction to an earlier frame inside a frame", () => world.Inputs(world.OldestFrame).AddAction(ColumnAction.Spawn(0)), state);
            }

            for (var w = 0; w < writers; w++)
            {
                world.Reduce(w);
                MidFrameSend(state);
            }

            var badPart = dice.Next(4) switch { 0 => -1, 1 => parts, 2 => int.MaxValue, _ => int.MinValue };
            Refused("Resolve(" + badPart + ") with " + parts + " parts", () => world.Resolve(badPart), new[] { typeof(ArgumentException) });
            var badWriter = dice.Chance(50) ? -1 : writers;
            Refused("Reduce(" + badWriter + ") with " + writers + " writers", () => world.Reduce(badWriter), new[] { typeof(ArgumentException), typeof(IndexOutOfRangeException) });
            for (var p = 0; p < parts; p++)
            {
                world.Resolve(p);
            }

            world.EndFrame();
            if (dice.Chance(50))
            {
                var view = world.OpenFrame(dice.Chance(50) ? FrameKernel.Rows : FrameKernel.Scalar);
                MidFrameSend(state);
                for (var w = 0; w < view.Writers; w++)
                {
                    view.BucketWriter(w);
                }

                MidFrameSend(state);
                unsafe
                {
                    var buckets = stackalloc RecordBucket[view.Writers];
                    for (var w = 0; w < view.Writers; w++)
                    {
                        buckets[w] = view.WriterBucket(w);
                    }

                    for (var p = 0; p < view.Parts; p++)
                    {
                        view.Resolve(p, buckets, view.Writers);
                    }
                }

                world.CloseFrame();
            }
        }

        void MidFrameSend(Type[] state)
        {
            var w = dice.Next(writers);
            var many = dice.Chance(20) ? dice.Range(100, 5000) : 1;
            Refused("Send x" + many + " to writer " + w + " inside a frame", () =>
            {
                var writer = world.Writer(w);
                for (var i = 0; i < many; i++)
                {
                    switch (dice.Next(4))
                    {
                        case 0:
                            writer.SendRecord((ulong)dice.Next(1 << 20));
                            break;
                        case 1:
                            writer.Send(dice.Range(-5, world.Capacity + 5), dice.Range(-2, 18), dice.Range(-9, 9));
                            break;
                        default:
                            writer.SendFrom(dice.Range(-5, world.Capacity), dice.Range(-5, world.Capacity + 5), dice.Range(-2, 18), dice.Range(-9, 9));
                            break;
                    }
                }
            }, state);
        }

        void Traffic(List<int> live)
        {
            for (var n = dice.Range(0, 6); n > 0; n--)
            {
                var roll = dice.Next(10);
                if (roll < 4 || live.Count == 0)
                {
                    try
                    {
                        live.Add(world.Spawn(dice.Next(templates)));
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
                else if (roll < 6)
                {
                    var slot = live[dice.Next(live.Count)];
                    world.Despawn(slot);
                    live.Remove(slot);
                }
                else
                {
                    world.Set(live[dice.Next(live.Count)], dice.Next(lanes - 2), dice.Chance(90) ? dice.Range(-50, 500) : int.MinValue);
                }
            }

            for (var n = dice.Range(0, 30); n > 0; n--)
            {
                var writer = world.Writer(dice.Next(writers));
                var target = AnySlot(live);
                var amount = dice.Chance(90) ? dice.Range(-40, 90) : long.MinValue;
                switch (dice.Next(10))
                {
                    case 0:
                        writer.SendRecord(RawRecord(world.Capacity));
                        break;
                    case < 4:
                        writer.SendFrom(AnySlot(live), target, dice.Chance(95) ? dice.Next(channels) : 16 + dice.Next(5), amount, dice.Range(-1, 3));
                        break;
                    default:
                        writer.Send(target, dice.Chance(95) ? dice.Next(channels) : -1, amount, dice.Range(-1, 3));
                        break;
                }
            }
        }

        ulong RawRecord(int cap)
        {
            var target = dice.Chance(80) ? dice.Next(cap) : dice.Next(Record.SlotLimit);
            var raw = ((ulong)(uint)dice.Next(1 << 30) << 34) ^ ((ulong)(uint)dice.Next(1 << 30) << 4) ^ (uint)dice.Next(16);
            return (raw & ((1ul << Record.TargetShift) - 1)) | ((ulong)target << Record.TargetShift);
        }

        ColumnAction AnyAction(List<int> live) => dice.Next(7) switch
        {
            0 => ColumnAction.Spawn(dice.Chance(80) ? dice.Next(templates) : templates + dice.Next(3) - (dice.Chance(50) ? templates + 2 : 0)),
            1 => ColumnAction.Despawn(AnySlot(live)),
            2 => ColumnAction.Set(AnySlot(live), dice.Range(-1, lanes), dice.Range(-50, 500)),
            3 => ColumnAction.SendFrom(AnySlot(live), AnySlot(live), dice.Range(-1, 17), dice.Range(-40, 90), dice.Range(-1, 3)),
            _ => ColumnAction.Send(AnySlot(live), dice.Range(-1, 17), dice.Chance(90) ? dice.Range(-40, 90) : long.MaxValue, dice.Range(-1, 3)),
        };

        var pending = int.MaxValue;

        void Edit(List<int> live)
        {
            if (world.Frame - 1 < world.OldestFrame)
            {
                return;
            }

            for (var n = dice.Range(1, 5); n > 0; n--)
            {
                var at = dice.Range(world.OldestFrame, world.Frame - 1);
                var inputs = world.Inputs(at);
                var actionsBefore = inputs.ActionCount;
                var recordsBefore = inputs.RecordCount;
                var touched = dice.Next(6);
                if (touched != 2 && touched != 4 || (touched == 2 ? actionsBefore : recordsBefore) > 0)
                {
                    pending = Math.Min(pending, at);
                }

                switch (touched)
                {
                    case 0:
                        inputs.AddAction(AnyAction(live));
                        break;
                    case 1:
                        inputs.InsertAction(dice.Next(inputs.ActionCount + 1), AnyAction(live));
                        break;
                    case 2:
                        if (inputs.ActionCount > 0)
                        {
                            if (dice.Chance(50))
                            {
                                inputs.RemoveAction(dice.Next(inputs.ActionCount));
                            }
                            else
                            {
                                inputs.ReplaceAction(dice.Next(inputs.ActionCount), AnyAction(live));
                            }
                        }

                        break;
                    case 3:
                        inputs.AddRecord(RawRecord(world.Capacity));
                        break;
                    case 4:
                        if (inputs.RecordCount > 0)
                        {
                            if (dice.Chance(50))
                            {
                                inputs.RemoveRecord(dice.Next(inputs.RecordCount));
                            }
                            else
                            {
                                inputs.ReplaceRecord(dice.Next(inputs.RecordCount), RawRecord(world.Capacity));
                            }
                        }

                        break;
                    default:
                        var target = live.Count > 0 ? live[dice.Next(live.Count)] : 0;
                        inputs.AddRecord(Record.Pack(target, dice.Next(channels), dice.Next(4), (uint)dice.Next(256), dice.Range(-100, 100)));
                        break;
                }
            }
        }

        void Step()
        {
            if (fused)
            {
                world.StepFused((FrameKernel)dice.Next(2));
            }
            else
            {
                world.Step();
            }
        }

        void Forget(int to)
        {
            if (pending >= to)
            {
                pending = int.MaxValue;
            }
        }

        void Resimulate(bool useFused)
        {
            if (world.EditedFrame != (pending == int.MaxValue ? -1 : pending))
            {
                throw new Finding("history", "EditedFrame is " + world.EditedFrame + " but the earliest edit not yet resimulated is " + pending);
            }

            if (pending != int.MaxValue && pending < world.OldestFrame)
            {
                Refused("Resimulate with frame " + pending + " edited and gone from the window", () => world.Resimulate(), state);
                if (dice.Chance(50))
                {
                    return;
                }

                var lost = world.DiscardLostEdits();
                if (lost != pending || (world.EditedFrame != -1 && world.EditedFrame != world.OldestFrame))
                {
                    throw new Finding("history", "DiscardLostEdits returned " + lost + " for a lost edit at " + pending + " and left EditedFrame " + world.EditedFrame + " with the oldest frame " + world.OldestFrame);
                }

                pending = world.EditedFrame == -1 ? int.MaxValue : world.EditedFrame;
            }

            if (useFused)
            {
                world.ResimulateFused((FrameKernel)dice.Next(2));
            }
            else
            {
                world.Resimulate();
            }

            pending = int.MaxValue;
        }

        void Compare(string after)
        {
            if (world.OldestFrame != 0 || pending != int.MaxValue)
            {
                return;
            }

            var script = new FrameScript();
            for (var f = 0; f < world.Frame; f++)
            {
                script.Capture(world, f);
            }

            var fresh = script.Fresh(pack, capacity, world.Frame, kernel);
            if (fresh[world.Frame] != world.Digest())
            {

                var end = world.Frame;
                var first = -1;
                for (var back = end - 1; back >= 0 && first < 0; back--)
                {
                    world.Rewind(back);
                    first = world.Digest() == fresh[back] ? back : -1;
                }

                throw new Finding("history", after + " at frame " + end + " differs from a fresh run of the same log (first bad frame " + first + ", writers " + writers + ", parts " + parts + ", " + kernel + (fused ? ", fused" : string.Empty) + ")");
            }
        }

        for (var f = 0; f < frames; f++)
        {
            var live = Alive();
            Traffic(live);
            if (dice.Chance(35))
            {
                Misuse(Alive());
            }

            Step();
            if (world.Frame > 1 && dice.Chance(20))
            {
                var action = dice.Next(3);
                if (action == 0)
                {
                    Edit(Alive());
                    Resimulate(fused);
                    resimulations++;
                    Compare("Resimulate after edits");
                }
                else if (action == 1)
                {
                    var to = dice.Range(world.OldestFrame, world.Frame);
                    world.Rewind(to);
                    Forget(to);
                    Compare("Rewind(" + to + ")");
                }
                else
                {
                    Edit(Alive());
                    var to = dice.Range(world.OldestFrame, world.Frame);
                    world.Rewind(to);
                    Forget(to);
                    if (dice.Chance(60))
                    {
                        Resimulate(false);
                    }

                    Compare("edits then Rewind(" + to + ")");
                }
            }
        }

        Compare("the run");
        return resimulations > 0 ? "resimulated" : "plain";
    }
}
