using System;
using System.Collections.Generic;

namespace Sv.Rules.Passes
{
    public abstract class CompilerPass
    {
        public abstract string Id { get; }

        public abstract void Run(CompileUnit unit);
    }

    public sealed class PassPipeline
    {
        readonly List<CompilerPass> passes = new List<CompilerPass>();

        public int Count => passes.Count;

        public CompilerPass this[int index] => passes[index];

        public static PassPipeline Standard()
        {
            var pipeline = new PassPipeline();
            pipeline.Add(new BindPass());
            pipeline.Add(new Validate1Pass());
            pipeline.Add(new LowerPass());
            pipeline.Add(new StratifyPass());
            pipeline.Add(new OrderPass());
            pipeline.Add(new SplitPass());
            pipeline.Add(new OptimisePass());
            pipeline.Add(new PartitionPass());
            pipeline.Add(new EmitPass());
            pipeline.Add(new Validate2Pass());
            return pipeline;
        }

        public void Add(CompilerPass pass)
        {
            if (IndexOf(pass.Id) >= 0)
            {
                throw new ArgumentException("sv: a pass named " + pass.Id + " is already in the pipeline", nameof(pass));
            }

            passes.Add(pass);
        }

        public int IndexOf(string id)
        {
            for (var i = 0; i < passes.Count; i++)
            {
                if (string.CompareOrdinal(passes[i].Id, id) == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        int Require(string id)
        {
            var at = IndexOf(id);
            if (at < 0)
            {
                throw new ArgumentException("sv: no pass named " + id, nameof(id));
            }

            return at;
        }

        public void InsertBefore(string id, CompilerPass pass)
        {
            var at = Require(id);
            if (IndexOf(pass.Id) >= 0)
            {
                throw new ArgumentException("sv: a pass named " + pass.Id + " is already in the pipeline", nameof(pass));
            }

            passes.Insert(at, pass);
        }

        public void InsertAfter(string id, CompilerPass pass)
        {
            var at = Require(id);
            if (IndexOf(pass.Id) >= 0)
            {
                throw new ArgumentException("sv: a pass named " + pass.Id + " is already in the pipeline", nameof(pass));
            }

            passes.Insert(at + 1, pass);
        }

        public void Replace(string id, CompilerPass pass) => passes[Require(id)] = pass;

        public void Remove(string id) => passes.RemoveAt(Require(id));

        public CompileUnit Run(CompileUnit unit)
        {
            for (var i = 0; i < passes.Count; i++)
            {
                passes[i].Run(unit);
                if (unit.Diagnostics.Errors > 0)
                {
                    break;
                }
            }

            return unit;
        }
    }
}
