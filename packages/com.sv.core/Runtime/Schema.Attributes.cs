using System;

namespace Sv
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class SvSchemaAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class SvStatAttribute : Attribute
    {
        public SvStatAttribute(string name, float @base)
        {
            Name = name;
            Base = @base;
        }

        public string Name { get; }

        public float Base { get; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class SvDerivedAttribute : Attribute
    {
        public SvDerivedAttribute(string name, float @base, string[] sources, float[] coefficients)
        {
            Name = name;
            Base = @base;
            Sources = sources;
            Coefficients = coefficients;
        }

        public string Name { get; }

        public float Base { get; }

        public string[] Sources { get; }

        public float[] Coefficients { get; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class SvIntrinsicAttribute : Attribute
    {
        public SvIntrinsicAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }
}
