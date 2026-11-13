using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sv;
using Sv.Host;
using Sv.Pack;
using Sv.Rules;

namespace TwinFixtures
{
    public static class Journal
    {
        public const int Version = 1;

        static string Hex(ulong value) => value.ToString("X16", CultureInfo.InvariantCulture);

        static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

        public static string Record<TKernels>(string pack, ulong packHash, PackPlan plan, Scenario scenario, HostOptions options)
            where TKernels : struct, IPackKernels
        {
            SlotPlanner.Plan(scenario);
            var hostPack = HostPack.From(plan);
            options.EntityCapacity = Math.Max(options.EntityCapacity, scenario.Capacity);
            var text = new StringBuilder();
            text.Append("twin ").Append(Version).Append('\n');
            text.Append("pack ").Append(pack).Append(' ').Append(Hex(packHash)).Append('\n');
            text.Append("scenario ").Append(scenario.Name).Append('\n');
            text.Append("capacity ").Append(options.EntityCapacity).Append('\n');
            text.Append("frames ").Append(Number(scenario.Frames)).Append('\n');
            using var host = new FrameLoop<TKernels>(hostPack, options);
            var kindLane = plan.Schema.KindIntrinsic;
            for (var frame = 0L; frame < scenario.Frames; frame++)
            {
                text.Append("frame ").Append(Number(frame)).Append('\n');
                foreach (var entity in scenario.Entities)
                {
                    if (entity.Frame != frame)
                    {
                        continue;
                    }

                    var slot = host.Spawn(entity.TemplateIndex);
                    text.Append("spawn ").Append(entity.TemplateIndex).Append(' ').Append(slot).Append('\n');
                }

                foreach (var kill in scenario.Kills)
                {
                    if (kill.Frame != frame)
                    {
                        continue;
                    }

                    var slot = scenario.SlotOf(kill.Entity);
                    host.Kill(slot);
                    text.Append("kill ").Append(slot).Append('\n');
                }

                foreach (var modifier in scenario.Modifiers)
                {
                    if (modifier.Frame != frame)
                    {
                        continue;
                    }

                    var slot = scenario.SlotOf(modifier.Entity);
                    if (modifier.Remove)
                    {
                        host.Modify(StatOp.RemoveSource(slot, modifier.Source));
                        text.Append("remove ").Append(slot).Append(' ').Append(modifier.Source).Append('\n');
                    }
                    else
                    {
                        host.Modify(StatOp.Add(slot, modifier.Stat, modifier.Shape, modifier.Value, modifier.Source, long.MaxValue, modifier.Layer));
                        text.Append("add ").Append(slot).Append(' ').Append(modifier.Stat).Append(' ').Append((int)modifier.Shape).Append(' ')
                            .Append(modifier.Value).Append(' ').Append(modifier.Source).Append(' ').Append((int)modifier.Layer).Append('\n');
                    }
                }

                foreach (var send in scenario.Sends)
                {
                    if (send.Frame != frame)
                    {
                        continue;
                    }

                    var source = send.Source == Scenario.Unset ? EffectKey.NoSource : scenario.SlotOf(send.Source);
                    var kind = send.Kind;
                    if (kind == Scenario.Unset)
                    {
                        var raw = 0;
                        if (send.Source != Scenario.Unset && kindLane >= 0)
                        {
                            var origin = scenario.Entities[send.Source];
                            raw = origin.Frame <= frame ? host.Read(source, kindLane) : hostPack.TemplateValues[origin.TemplateIndex][kindLane];
                        }

                        kind = raw < 0 ? 0 : raw > 255 ? 255 : raw;
                    }

                    var target = scenario.SlotOf(send.Target);
                    host.Send(target, source, kind, send.Channel, send.Amount, send.Hits, send.Origin);
                    text.Append("send ").Append(target).Append(' ').Append(source).Append(' ').Append(kind).Append(' ').Append(send.Channel).Append(' ')
                        .Append(send.Amount).Append(' ').Append(send.Hits).Append(' ').Append((int)send.Origin).Append('\n');
                }

                host.Step();
                text.Append("digest ").Append(Hex(host.LastDigest)).Append('\n');
            }

            text.Append("final ").Append(Hex(host.Digest())).Append('\n');
            return text.ToString();
        }
    }
}
