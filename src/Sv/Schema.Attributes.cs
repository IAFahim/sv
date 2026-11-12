using System;

namespace Sv
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class SvSchemaAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class SvStatAttribute(string name, float @base) : Attribute
    {
        public string Name { get; } = name;

        public float Base { get; } = @base;
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class SvDerivedAttribute(string name, float @base, string[] sources, float[] coefficients) : Attribute
    {
        public string Name { get; } = name;

        public float Base { get; } = @base;

        public string[] Sources { get; } = sources;

        public float[] Coefficients { get; } = coefficients;
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class SvIntrinsicAttribute(string name) : Attribute
    {
        public string Name { get; } = name;
    }
}
