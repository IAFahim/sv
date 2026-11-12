using System.Globalization;

namespace Sv.Rules
{
    public enum Severity : byte
    {
        Error = 0,
        Warning = 1,
    }

    public enum DiagnosticCode : ushort
    {
        JsonSyntax = 1,
        SchemaVersion = 2,
        UnknownKey = 3,
        MissingKey = 4,
        WrongType = 5,
        DuplicateId = 6,
        DuplicateName = 7,
        UnknownName = 8,
        IdOutOfRange = 9,
        LaneCollision = 10,
        LaneCounter = 11,
        TypeMismatch = 12,
        ScopeViolation = 13,
        CyclicChain = 14,
        SelfRuleCycle = 15,
        AdmitTie = 16,
        ShapeGroupTie = 17,
        Shadowed = 18,
        DepthLimit = 19,
        EventReadsOtherRow = 20,
        FloatAcrossRows = 21,
        FlowReadsLaterStratum = 22,
        DuplicateFold = 23,
        DuplicateEventRule = 24,
        TooManyChannels = 25,
        TooManyKinds = 26,
        ChannelsAboveWord = 27,
        RoleConflict = 28,
        BadEmit = 29,
        BadValue = 30,
        LaneReused = 31,
        LaneMoved = 32,
    }

    public readonly struct Diagnostic
    {
        public DiagnosticCode Code { get; }

        public Severity Severity { get; }

        public string File { get; }

        public int Offset { get; }

        public int RuleId { get; }

        public int OtherRuleId { get; }

        public string Message { get; }

        public Diagnostic(DiagnosticCode code, Severity severity, string file, int offset, int ruleId, int otherRuleId, string message)
        {
            Code = code;
            Severity = severity;
            File = file;
            Offset = offset;
            RuleId = ruleId;
            OtherRuleId = otherRuleId;
            Message = message;
        }

        public override string ToString() =>
            File + "(" + Offset.ToString(CultureInfo.InvariantCulture) + "): " + (Severity == Severity.Error ? "error" : "warning")
            + " SV" + ((int)Code).ToString("D4", CultureInfo.InvariantCulture) + ": " + Message;
    }

    public sealed class DiagnosticList
    {
        readonly Rows<Diagnostic> rows = new Rows<Diagnostic>();
        int errors;

        public int Count => rows.Count;

        public int Errors => errors;

        public Diagnostic this[int index] => rows[index];

        public void Error(DiagnosticCode code, string file, int offset, int ruleId, int otherRuleId, string message)
        {
            rows.Add(new Diagnostic(code, Severity.Error, file, offset, ruleId, otherRuleId, message));
            errors++;
        }

        public void Warning(DiagnosticCode code, string file, int offset, int ruleId, int otherRuleId, string message) =>
            rows.Add(new Diagnostic(code, Severity.Warning, file, offset, ruleId, otherRuleId, message));

        public Diagnostic[] ToArray() => rows.ToArray();
    }

    public readonly struct Checked<T> where T : class
    {
        public T? Value { get; }

        public Diagnostic[] Diagnostics { get; }

        public Checked(T? value, Diagnostic[] diagnostics)
        {
            Value = value;
            Diagnostics = diagnostics;
        }

        public bool Ok => Value != null;
    }
}
