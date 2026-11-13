# StateSmith study notes (for the stats/intrinsics/events rules compiler)

Repo studied: D:\GitHub\_work\research\StateSmith (HEAD 0c32be37, 2026-05-11), plus D:\GitHub\_work\research\StateSmith-examples.
Method: read-only (Read/Grep/shell listing only). Nothing was built or run. All "what generated code looks like" quotes come from generated files
already checked into the repo (listed in section 2). Section 9 lists what I did NOT verify.

Abbreviations: SS = StateSmith, `src` = D:\GitHub\_work\research\StateSmith\src\StateSmith (the library), `ST` = ...\src\StateSmithTest.

---------------------------------------------------------------------------------------------------
## 0. One-paragraph model of SS

SS reads a diagram (draw.io / yEd graphml / PlantUML), turns it into a tree of `Vertex` objects (states, pseudo-states) that own ordered `Behavior`s
(trigger + guard + action + optional transition target), runs a fixed, named list of graph-to-graph "transformation steps" that lower exotic features into
plain behaviors, then an "algorithm" (Balanced1 or Balanced2) walks the lowered graph and prints straight-line code for each (state, event) pair into an
intermediate language called GIL (a restricted C# file). A per-language "transpiler" (Roslyn syntax walker) turns GIL text into C99 / C++ / C# / Java /
JS / TS / Python. Guards and actions are NOT parsed by SS; they are opaque target-language text (with identifier expansion), smuggled through GIL as string
literals. There is no runtime library; the output is self-contained.

---------------------------------------------------------------------------------------------------
## 1. Pipeline and where each stage lives

Top-level orchestration
- `src\Runner\SmRunner.cs` public entry; builds a DI container (`src\Runner\DiServiceProvider.cs`), reads settings, `Run()` -> `SmRunnerInternal.Run()`.
- `src\Runner\SmRunnerInternal.cs` `Run()` order: (a) `SetupAndFindStateMachine` (parse diagram), (b) `smDesignDescriber.DescribeBeforeTransformations`, JSON export
  "before", (c) `inputSmBuilder.FinishRunning()` = run transformer pipeline, (d) describe/JSON "after", (e) `codeGenRunner.Run()`, (f) optional simulator HTML.
- `src\Runner\InputSmBuilder.cs`: Step 1 diagram file -> vertices (picks parser by extension), Step 2 choose the StateMachine, Step 3 `FinishRunning()` ->
  `transformer.RunTransformationPipeline(sm)`.

Parse (stage 1)
- Diagram readers: `src\Input\DrawIo\*` (MxCellParser, MxCellsToSmDiagramConverter), `src\Input\Yed\*`, `src\Input\PlantUML\*` (ANTLR grammar
  `D:\GitHub\_work\research\StateSmith\antlr\PlantUML.g4`, generated parser in `src\Input\PlantUML\antlr4`). All produce neutral `DiagramNode`/`DiagramEdge`
  (`src\Input\DiagramNode.cs`, `DiagramEdge.cs`).
- Label grammar for node and edge text: `antlr\StateSmithLabelGrammar.g4` (generated: `src\Input\antlr4\StateSmithLabelGrammar*.cs`, ~5k-line parser, plus
  hand visitor `StateSmithLabelGrammarVisitor.cs`). A behavior line is `order? ( triggers guard action? vias? | ... )`, e.g. `1.1. EV1 [guard] / action`. `order` is a decimal
  number followed by `.`; `else` is a keyword; `(EV1, EV2)` is a trigger list; `enter`/`exit`/`do` are special triggers.
- `src\SmGraph\DiagramToSmConverter.cs`: builds `Vertex` tree from nodes (`ProcessNode`, `ParseNode`) and attaches `Behavior`s from edges (`ProcessEdgeInner`).
  Edge with empty label = blank transition (becomes an implicit `do` behavior later).

Graph model (in `src\SmGraph`)
- `Vertex.cs` (parent, depth, children, `_behaviors` ordered list, `_incomingTransitions`; `FindTransitionPathTo` computes LCA + toExit/toEnter lists via depth;
  `FirstAncestorThatHandlesEvent(eventName)`), `Behavior.cs` (`order` double, `guardCode`/`actionCode` strings, `_triggers`, `viaEntry`/`viaExit`),
  `State.cs`, `StateMachine.cs` (event set `_events`, `historyStates`, `variables`), pseudo-states: `InitialState`, `ChoicePoint`, `EntryPoint`, `ExitPoint`,
  `HistoryVertex`, `HistoryContinueVertex`, `NotesVertex`, `ConfigOptionVertex`, `OrthoState` (parsed but REJECTED later: `SpecificVertexValidator.Visit(OrthoState)`
  throws NotImplemented; no orthogonal regions).
- Visitors: `src\SmGraph\Visitors\*` (IVertexVisitor, RecursiveVertexVisitor, LambdaVertexWalker...). Compiler-checked exhaustiveness via the visitor pattern.

Transformer passes (stage 2): `src\Runner\StandardSmTransformer.cs`, runner class `SmTransformer.cs` (plain `List<TransformationStep>` with string/enum ids and
`InsertBeforeFirstMatch / InsertAfterFirstMatch / Swap / Remove`; users extend it from a .csx script). Fixed order (enum `TransformationId`):

| # | Id | File | What it does |
|---|----|------|--------------|
| 1 | Standard_RemoveNotesVertices | SmGraph\NotesProcessor.cs | delete `$NOTES` nodes |
| 2 | Standard_TomlConfig | SmGraph\TomlConfigVerticesProcessor.cs | settings embedded in diagram |
| 3 | Standard_SupportRenderConfigVerticesAndRemove | SmGraph\RenderConfigVerticesProcessor.cs | per-language render config nodes |
| 4 | Standard_SupportParentAlias | SmGraph\ParentAliasStateProcessor.cs | `$parent_alias` sugar |
| 5 | **Standard_SupportEntryExit** | SmGraph\EntryExitProcessor.cs | see below |
| 6 | Standard_SupportPrefixingModder | Runner\PrefixingModder.cs | `$prefix` name mangling |
| 7 | Standard_NameConflictResolution | Runner\StateNameConflictResolver.cs | unique state names |
| 8 | **Standard_SupportHistory** | SmGraph\HistoryProcessor.cs | see below |
| 9 | Standard_SupportElseGuard | SmGraph\ElseGuardProcessor.cs | `[else]` guard -> `order = ELSE_ORDER` |
| 10 | **Standard_SupportOrderAndElse** | SmGraph\OrderAndElseProcessor.cs | see below |
| 11 | Standard_SupportAlternateTriggers | SmGraph\SupportAlternateTriggersProcessor.cs | `entry` -> `enter` |
| 12 | Standard_Validation1 | StandardSmTransformer.Validate | SpecificVertexValidator + VertexValidator + StateNameValidator |
| 13 | Standard_DefaultUnspecifiedEventsAsDoEvent | SmGraph\DefaultToDoEventVisitor.cs | behaviors with no trigger get `do` |
| 14 | Standard_TriggerMapping | SmGraph\TriggerMap\* | user-defined trigger renaming/aliasing |
| 15 | Standard_AddUsedEventsToSm | SmGraph\AddUsedEventsToSmClass.cs | collect event set -> `StateMachine._events` |
| 16 | Standard_FinalValidation | same Validate() | validate again after lowering |

Entry/exit points (EntryExitProcessor): for an `EntryPoint` child, every transition into the parent that says `via entry X` is `RetargetTo(entryPoint)`; for an
`ExitPoint`, every parent behavior with `via exit X` is moved with `RetargetOwner(exitPoint)`. After this the points are ordinary pseudo-states (no special
codegen). Validators: `SmGraph\Validation\EntryPointValidator.cs`, `ExitPointValidator.cs`.

History (HistoryProcessor): fully lowered to ordinary behaviors, no special runtime. For each state tracked by `$H`: adds an `enter` behavior to that state
whose action (marked as GIL via `GilCreationHelper.MarkAsGilExpansionCode`) is `this.vars.<hist_var> = <HistEnum>.<STATE>;`, and adds a guarded transition on the
history vertex `[this.vars.<hist_var> == <HistEnum>.<STATE>]`; the default transition is forced to `order = Behavior.ELSE_ORDER`. `EnumBuilder.OutputHistoryIdCode`
emits the extra enum. Deep history = `$HC` vertices adding the tracking behaviors to ancestors' history vars. Lesson: "feature => generated variable + guarded
behaviors" instead of backend support.

Order and else (OrderAndElseProcessor + Behavior.cs):
```
public const double DEFAULT_ORDER = 1e6;
public const double ELSE_ORDER = DEFAULT_ORDER * 10;
...
v._behaviors = v.Behaviors.OrderBy((b) => b.order).ToList();     // stable LINQ OrderBy
```
Validation: at most one `else` per vertex, must be a transition, no guard, no explicit order. Ties keep diagram/edge order (stable sort), so the final order for equal
priority depends on input file order. Perf wart: `SortBehaviorsByOrder(v)` is called INSIDE the `foreach (var b in v.Behaviors)` loop, so it re-sorts once per behavior:
O(n^2 log n) per vertex. Irrelevant at SS scale (a handful of behaviors per state) but a 10,000-rule vertex would hit it. (Inferred from code, not measured.)

Validation details: `VertexValidator` (via-entry/exit only on transitions; a transition cannot have enter/exit trigger; detects yEd hidden self edges),
`PseudoLoopDetector.cs` (DFS through choice/initial chains, throws on a cycle of pseudo-states, i.e. the chain graph must be a DAG), `StateNameValidator`,
`SpecificVertexValidator` (exactly one initial state; no nested state machines; no OrthoState; HistoryContinue must be gone).

Codegen (stage 3)
- `src\Output\GilAlgoCodeGen.cs` (`ICodeGenRunner`): `expansionsPrep.PrepForCodeGen()` -> `gilAlgo.GenerateGil(sm)` -> `transpiler.TranspileAndOutputCode(gilCode)`.
- Algorithm selection: `src\Runner\AlgoTranspilerCustomizer.cs` (DI swaps `IGilAlgo`/`EventHandlerBuilder`; default is Balanced2) and `AlgorithmId.cs`.
- Balanced1: `src\Output\Algos\Balanced1\AlgoBalanced1.cs` + `EventHandlerBuilder.cs` (689 lines, the heart) + `EnumBuilder.cs` + `PseudoStateHandlerBuilder.cs` + `NameMangler.cs`
  (+ Camel/Pascal variants) + `SubtreeIdMapper.cs`.
- Balanced2: `src\Output\Algos\Balanced2\AlgoBalanced2.cs` (derives from AlgoBalanced1; overrides struct fields, dispatch, exit-up-to) + `EventHandlerBuilder2.cs`
  (derives from EventHandlerBuilder; overrides enter/exit/consume/ancestor behaviors).
- Text builder: `src\Output\OutputFile.cs` (indentation-aware StringBuilder wrapper; "RequestNewLineBeforeMoreCode").

---------------------------------------------------------------------------------------------------
## 2. What the generated C# looks like

Files used (all checked in, no need to run anything):
- Balanced1 C#: `ST\Output\Algos\out\Balanced1_CSharp\RocketSm.cs` (283 lines; design: `[*]->group{[*]->g1; g1->g2; g2->g1 : [x > 50]}`, one `do` event).
- Balanced2 C# (single event): `ST\Output\Algos\out\Balanced2_CSharp\RocketSm.cs`.
- Balanced2 C# (multi event, ancestors, guard): `ST\Output\Algos\out2\Balanced2_CSharp\RocketSm.cs` (design in `ST\Output\Algos\AlgoTests.cs`: `g1 --> g2: EV1 [a > 20]`, `g2 --> g1: EV2`, `group --> s1: EV1`).
- Big realistic Balanced2 C#: `src\test-misc\csharp-spec2\Spec2Sm.cs` (13,126 lines, 678 KB, 174 states, 15 events, 505 handler functions; test design with trace guards) and its GIL `Spec2Sm.gil.cs.txt`.
- Balanced2 C99 and C++: `ST\Output\Algos\out2\Balanced2_C99\RocketSm.c/.h`, `StateSmith-examples\cpp-inheritance-1\LightSm.h/.cpp`. Old Balanced1 C: `StateSmith-examples\multi-lang-1\LightSm.h`.
Note: StateSmith-examples\csharp-space-menu-1\Ui\UiSm.cs is an OLD (0.9.2, Balanced1) output; prefer the repo outputs above.

### 2.1 State id enum, event id enum (both algorithms)
```
public enum EventId { EV1 = 0, EV2 = 1 }        // 'do' is forced to 0 when present, others sorted alphabetically
public const int EventIdCount = 2;
public enum StateId { ROOT = 0, GROUP = 1, G1 = 2, G2 = 3, S1 = 4 }
public const int ROOT_SubtreeEndId = 4;  public const int GROUP_SubtreeEndId = 3; ...
```
Ids = DFS pre-order, children visited sorted by NAME (`SubtreeIdMapper.cs`, "to minimize git diffs on user diagram refactoring"). Because ids are
pre-order, a state's subtree is the contiguous id range `[id, SubtreeEndId]` (so `isDescendant` is a range compare; emitted since 0.22.1, issue #538). Ids are a function of the
design (rename or add a state and other ids shift), they are not a stable external contract.

### 2.2 Current state variable
Balanced2: exactly one field, `public StateId stateId;` ("Feel free to inspect, but don't modify"). Balanced1 adds three pointer fields (section 3).

### 2.3 Dispatch (Balanced2: nested switch, no tables)
From `out2\Balanced2_CSharp\RocketSm.cs`:
```
public void DispatchEvent(EventId eventId)
{
    switch (this.stateId)
    {
        // STATE: g2
        case StateId.G2:
            switch (eventId)
            {
                case EventId.EV2: G2_ev2(); break;
                case EventId.EV1: GROUP_ev1(); break; // First ancestor handler for this event
            }
            break;
        ...
```
Codegen: `AlgoBalanced2.OutputFuncDispatchEvent/OutputStateEventHandlerCode`. For EVERY state it emits a case for events the state handles itself and, for events it does not
handle, a case that calls the FIRST ANCESTOR that does (`NamedVertex.FirstAncestorThatHandlesEvent`, computed at compile time). So inheritance is FLATTENED in the dispatch table and
resolved at gen time. A state with no handler anywhere gets `// No events handled by this state (or its ancestors).`. Single-event designs drop the inner switch
(issue #388): `OutputStateSingleEventHandlerCode`.

### 2.4 How an event reaches the parent when the child doesn't consume it
Two different mechanisms:
(a) Child doesn't handle the event at all: direct call to the ancestor handler from dispatch (above).
(b) Child handles it but may decline: handler falls through to the ancestor at the bottom (Balanced2, `EventHandlerBuilder2.OutputStateEventHandlerFunctionBottom`):
```
private void G1_ev1()
{
    bool consume_event = false;
    // g1 behavior
    // uml: EV1 [a > 20] TransitionTo(g2)
    if (a > 20)
    {
        G1_exit(); G2_enter();
        // Step 4: complete transition. Ends event dispatch. No other behaviors are checked.
        return;
    }
    // Check if event has been consumed before calling ancestor handler.
    if (!consume_event)
    {
        GROUP_ev1();
    }
}
```
Semantics (verified in `EventHandlerBuilder.OutputNonTransitionCode` and `EventHandlerBuilder2.PreActionCode`): a transition whose guard passes ends dispatch (`return`). A NON-transition
behavior (guard passes) executes its action and sets `consume_event = true` (except for the special `do` event, "not normally consumed"; user action code may assign
`consume_event` itself and is detected by `ActionCodeUsesConsumeEventVar`). At the end, if not consumed and an ancestor handles the event, the ancestor handler is CALLED (nested call;
stack depth = nesting depth, bounded at compile time).
Balanced1 does it with a trampoline instead (no recursion):
```
public void DispatchEvent(EventId eventId)
{
    Func? behaviorFunc = this.currentEventHandlers[(int)eventId];
    while (behaviorFunc != null)
    {
        this.ancestorEventHandler = null;
        behaviorFunc(this);
        behaviorFunc = this.ancestorEventHandler;
    }
}
```
each handler first does `this.ancestorEventHandler = ptr_GROUP_ev1;` ("Setup handler for next ancestor"), and consuming sets it to null.

### 2.5 Guards and the ORDER number (priority)
Within one (state, trigger) handler, `TriggerHelper.GetBehaviorsWithTrigger` returns that state's behaviors already sorted by `order` ascending (lowest number evaluated FIRST).
Each becomes sequential straight-line code:
- guard -> `if (guard) { ... }`; no guard -> bare `{ ... }` block (python needs `if (true)`).
- If a behavior is a transition with no guard, every later behavior is unreachable and is emitted only as a comment (`OutputReachableStateBehaviors`, issue #394):
  `// unreachable behavior: ... due to unconditional transition above` (visible in `Spec2Sm.cs` line ~7031).
- `else` = `order 1e7`, always last, so it is just the final unguarded block.
- Non-transition behaviors all run in order (several may fire); the first passing TRANSITION stops everything.
From `Spec2Sm.cs` (`Test3_BehaviorOrdering`): behaviors `1. EV1`, `1.1. EV1`, `2. EV1 ... TransitionTo(S3)` become three consecutive `if (trace_guard(...))` blocks in
that order (decimal order lets you insert between), the third doing `TEST3_S2_exit(); ... TEST3_S3_enter(); return;`.
Priority conflicts between a CHILD and its PARENT are not numeric: child state always before ancestor (child decides, then optionally falls through if not consumed). A numeric "wins
by higher number" scheme would have to be added by us (SS has lowest-number-first and no duplicate detection).

### 2.6 Exit/enter sequences
`EventHandlerBuilder.OutputTransitionCode` emits fixed 4 steps: (1) exit up to LCA, (2) transition action, (3) enter towards target, (4) complete (set state, return).
- Exit: if the active leaf is statically known and exactly one state must be exited -> direct call `G2_exit();`. Otherwise a loop call `ExitUpToStateHandler(StateId.ROOT);`
  (`CanUseSingleDirectExit`).
- Balanced2 `ExitUpToStateHandler(desired)`: `while (stateId != desired) switch (stateId) { case StateId.G1: G1_exit(); break; ... default: return; }` (the default is a
  corruption guard: prevents infinite loop, issue #391). Each `X_exit()` ends with `this.stateId = <parent>` so the loop walks up. Each `X_enter()` starts with `this.stateId = X`
  (Balanced2 tracks state on enter so pass-through states can be exited again).
- Enter: toEnter list computed top-down by `Vertex.FindTransitionPathTo` (LCA algorithm using depth); each is `X_enter();`; enter behaviors run inside `X_enter`. Initial pseudo-states
  inline their transition, so entering `group` immediately emits `G1_enter()`.
- Balanced1 stores exit handlers as function pointers: `currentStateExitHandler = ptr_G1_exit;` and `ExitUpToStateHandler(Func desired)` loops `currentStateExitHandler!(this)`.

### 2.7 "Tail-chain optimization": not found by that name
I grepped the repo (cs, md, changelog) for tail/tail-chain/tail-call: no such feature. The nearest real optimizations (all in the changelog and code):
1. Pseudo-state de-duplication (`PseudoStateHandlerBuilder.ShouldCreateFunctionHandler`): a choice/initial/history point with >1 incoming transitions (initial counts parent's incoming too) is emitted ONCE as a
   `X_transition()` function and called; with exactly one incoming it is INLINED as nested ifs (see the `TEST10_G_ChoicePoint__transition` nesting in `Spec2Sm.cs`, lines 4457-4540).
   This is how "chains of chains" are lowered: DAG of choice points -> nested if/else, shared sub-DAGs -> shared functions.
2. Transition completes with `return` after the last `enter` (the ancestor call in Balanced2 is the last statement of the function, so it is a call in tail position that a JIT may or may not turn
   into a jump; nothing in SS forces that).
3. Direct-exit vs exit-up-to, single-event dispatch collapse, unreachable-behavior commenting, no root exit handler (0.13.2), "No ancestor handles event. Can skip nulling ancestorEventHandler" (Balanced1).

---------------------------------------------------------------------------------------------------
## 3. Instance storage and runtime allocation

Balanced2 (default): one field per instance.
- C99: `struct RocketSm { RocketSm_StateId state_id; };` (`ST\Output\Algos\out2\Balanced2_C99\RocketSm.h`, enum is `int`-sized unless the user packs it).
- C++ (`cpp-inheritance-1\LightSm.h`): `enum class StateId: uint8_t` -> 1 byte of SS state per instance.
- C#: class with `public StateId stateId;` (4 bytes + object header; `new RocketSm()` is the only allocation; the ctor is empty).
- User variables (`vars` struct/class) are extra; guards/actions reference them (`this.vars.count`, expansion `{VarsPath}`).
- No lookup table, no delegates, no heap use at runtime. The only "table" is the compiler's own jump table for `switch(stateId)` / `switch(eventId)`. Optional `StateIdToString`, `EventIdToString`,
  `GetParentId` are `static` switch functions and can be switched off (`outputStateIdToStringFunction` etc., `AlgoBalanced1Settings.cs`).
Balanced1: per instance (`ST\Output\Algos\out\Balanced1_CSharp\RocketSm.cs`, `StateSmith-examples\multi-lang-1\LightSm.h`): `stateId`, `ancestorEventHandler` (pointer), `currentEventHandlers[EventIdCount]`
(pointer array, one pointer per event; in C# a per-instance heap array `new Func[EventIdCount]`), `currentStateExitHandler` (pointer). So 4 + 8 + 8*E + 8 bytes (+array header in C#), and enter/exit
rewrites the pointer array every transition. Static delegates (`ptr_G1_exit`) avoid per-call allocation. This runtime dispatch table is exactly what the owner wants to avoid.
Handlers are instance methods / take `RocketSm* sm`: state lives inside the instance object, not in an external SoA array, so million-entity use needs a different calling convention (see section 6).

---------------------------------------------------------------------------------------------------
## 4. Templates / expansion system, GIL, adding languages

Expansions (Input\Expansions, Output\UserConfig)
- Identifiers in user guard/action text are expanded textually: `Expander.cs` holds `variableExpansions` (name -> code string) and `functionExpansions` (name -> reflected C# method that
  returns a string). `ExpandingVisitor.ParseAndExpandCode(expander, code)` tokenizes the user's code with a small ANTLR grammar and swaps identifiers. Users define them in a render-config class
  or `UserExpansionScriptBase` (`Output\UserConfig\*`: `IRenderConfig`, `RenderConfig*Vars`, `AutoVars\AutoExpandedVarsProcessor.cs` which turns `count` into `sm->vars.count`).
  `DefaultExpander.cs` is the catch-all template (`{AutoVarName()}`, `{VarsPath}`).
- Expansion functions can use script context (`CurrentNamedVertex`, `CurrentTrigger`, `CurrentBehavior`: see `trace_meta()` in `ST\spec\2\Spec2Fixture.cs`).
- `WrappingExpander.cs`: wraps expanded text so GIL stays valid C#: guard -> `____GilData_VarArgsToBool(____GilData_EchoStringBool("<raw text>"))`, action ->
  `____GilData_EchoStringVoid("<raw line>");`. `$gil(...)` lets the user write real GIL that the transpiler will translate (e.g. a C# enum member that becomes `Enum_Member` in C).
So SS has no code templates in the T4 sense: the algorithm builds text with `OutputFile.AppendIndentedLine` calls; "template-like" parts are expansion strings only for user-visible identifiers.

GIL (Generic Intermediate Language) = a restricted subset of C# source text (see `Spec2Sm.gil.cs.txt`):
```
public class ____GilData_FileTop { }
public class Spec2Sm { public enum EventId {...}  private void TEST3_S2_ev1() {... if (____GilData_VarArgsToBool(____GilData_EchoStringBool("trace_guard(...)"))) {...} } }
public static bool ____GilData_EchoStringBool(string toEcho) { return true; }
```
Helpers in `Output\Gil\GilCreationHelper.cs`, `GilTranspilerHelper.cs`; compiled with Roslyn (`RoslynCompiler.cs`: `CSharpSyntaxTree.ParseText` + `CSharpCompilation` semantic model with system references from
`InMemoryMetaDataProvider`/`FileMetadataProvider`) so that transpilers can ask the semantic model "is this member an enum", "is this a method group" (`MethodPtrFinder.cs` finds `this.X` method pointers to
turn them into function pointers). Constraint on algorithms: they must only emit constructs every transpiler handles (enums, fields, methods, if/while/switch, a method call, a delegate type for function pointers).

Adding a target language (pattern from `Output\Gil\<Lang>\`):
1. `GilToX : IGilTranspiler` (writes files; e.g. `CSharp\GilToCSharp.cs` is 30 lines of glue; `C99\GilToC99.cs` writes .h and .c).
2. `XGilVisitor : CSharpSyntaxWalker` that re-prints GIL syntax in X (C99: 852 lines, C++: 572, Python: 629, JS: 450, TS: 342, Java: 345, C#: 333).
3. `RenderConfigXVars` / `IRenderConfigX` (user config knobs) and `IExpansionVarsPathProvider` (how `vars` is spelled: `sm->vars.` vs `this.vars.`), `IAutoVarsParser` if variable declaration syntax differs.
4. Register in `TranspilerId.cs`, `AlgoTranspilerCustomizer.cs` (sets flags such as `skipClassIndentation`, `useIfTrueIfNoGuard`, `allowSingleLineSwitchCase`, `varsStructAsClass`), `DiServiceProvider`.
5. Add the language to the spec test matrix (section 7).
Cost evidence: Python/Java/TS/C++ currently only support Balanced2 (AlgoTests.cs lines ~60-75). The README says adding languages is "tricky".

---------------------------------------------------------------------------------------------------
## 5. Would this approach scale to ~10,000 rules? What breaks

Data points from the repo (not benchmarks): Spec2Sm design = 174 states x 15 events -> 13,126 lines / 678 KB C# / 505 functions; Roslyn + formatter handled it. Output is ~75 lines per
state, mostly comments (each behavior prints 4 "Step" comment blocks plus `// uml:` text) and the dispatch table (S x events-in-scope entries).

Structural fit
- SS = "which discrete state am I in" -> a rule graph over continuous stats is different: there the guard is data (stat/intrinsic predicate), not current state. SS can be bent: treat
  each event as a "rule chain" and ancestors as rule layers, but state machine concepts (LCA, enter/exit, history, one active leaf) would be dead weight.
- Good fit: ordered guarded behaviors per (scope, trigger), consume/else semantics, DAG of shared sub-chains lowered to nested ifs or shared functions, flattening of inheritance in dispatch.

What breaks at 10k rules (inference from code, nothing measured)
1. Single huge method: if thousands of rules attach to one event handler you get thousands of sequential `if` blocks in one function. .NET JIT/Burst have size limits/heuristics (I recall the CLR JIT switches
   to minimal optimization for methods above roughly 60 KB IL / 2000 basic blocks; NOT verified here) -> must partition handlers (by event x layer x intrinsic-bucket), emit decision structure
   (switch on intrinsic bits first), cap function size, chain partitions by explicit calls/gotos.
2. Source size: with SS's comment-heavy printer 10k transitions ~ hundreds of thousands of lines; C# compiler/Unity domain reload time grows. Emit terse code + a side-car map (rule id -> source location) instead of comments.
3. Generator complexity hot spots: `OrderAndElseProcessor.SortBehaviorsByOrder` called inside the foreach (O(n^2 log n) per vertex); `FirstAncestorThatHandlesEvent` re-walks ancestors and re-scans each ancestor's
   behaviors (`TriggerHelper.HasBehaviorsWithTrigger`) for every (state,event) pair in dispatch gen (O(S*E*depth*B)); `List.Remove`/`IndexOf` based graph edits; string-keyed `HashSet<string>` events. Needs indexes.
4. The GIL/Roslyn round trip: whole output re-parsed with semantic model, then `Formatter.Format` over the full text (C# transpiler). Memory/time super-linear for ~100 MB source. A direct emitter (strings or
   `SyntaxFactory`) for the single target avoids this. SS needs GIL only because it targets 7 languages.
5. Stack depth: Balanced2 recursion depth = hierarchy depth (compile-time known). "Chains with chains" nest as nested ifs (inline) so depth is code nesting, not runtime stack; unbounded chain depth
   would make deeply nested C# (compiler limits on nesting). Use flat early-exit style (`if (blocked) goto end;` / `return`) instead of nesting; share sub-chains as functions when in-degree > 1 (as SS does).
6. Duplicate code through inlining: SS inlines single-incoming pseudo-states but duplicates when the same chain is reachable from several handlers inside different states (it only dedups pseudo-states, not whole
   behavior bodies). With 10k rules and card-added checks you need common-subexpression and shared-chain extraction by hash of the lowered rule body.
7. Stable ids: SS ids depend on the design (sorted by name). Offsets-as-contract require explicit, stored ids.
8. Semantics gap: SS stops at the first passing transition; our rules need "all matching modifiers apply unless blocked". SS's non-transition behaviors (all run in order, `consume_event` gates ancestors) is the right primitive;
   a "blocked" rule = early `return`.

---------------------------------------------------------------------------------------------------
## 6. Ideas worth copying for a rules compiler over stats / intrinsics / events

Architecture
1. Named, orderable lowering pipeline: `SmTransformer`-style list of `(id, Action<Graph>)` with InsertBefore/After, so designers' mods (cards) are passes. Run validation twice: before lowering (user-facing errors) and after (compiler self-check). Copy `StandardSmTransformer.cs`'s structure.
2. Lower exotic features to the plain core before emission (history -> var + guarded behaviors; entry/exit points -> retargeting; `else` -> sentinel order; implicit trigger -> `do`). Keep emission dumb. Our analogs: "blocked while cursed" = guard on a `Blocks` rule class lowered to an `if` at the top of the chain; "heals twice while jumping, higher priority wins" = sorted rule list per event.
3. Keep the IR typed: SS passes guards as opaque strings (cannot analyze, reorder, or dedup them; only regex/ANTLR identifier expansion). For 10k rules store conditions as expression trees over `StatId/IntrinsicId/EventId` so we can constant-fold, merge into bitmask tests, hash for dedup, and prove shadowing.
4. Algorithm class + builder split: `AlgoBalanced1/2` (what to emit) vs `EventHandlerBuilder(2)` (per-handler printing, with virtual hooks `PreActionCode`, `OutputCompleteTransition`, `OutputNextAncestorHandler`). Two algorithm generations sit side by side via DI (`AlgoTranspilerCustomizer`), each with spec tests. We can keep an "interpretable reference backend" and a "fast backend" the same way.
5. GIL is only worth it with multiple targets. We target one (C# / Burst). Skip it; emit C# text directly through an indentation-aware `OutputFile` clone.
6. Generated code carries the source rule text in comments (`// uml: ...`) for traceability. Replace with rule ids + a map.

Priority handling
7. Order as `double` with big default (1e6) and else = 10x: lets designers insert between 1 and 2 with `1.5` without renumbering; stable sort; `else` sentinel. For "higher number wins" invert at compile (`sortKey = -priority`). Add what SS lacks: detect equal priorities among rules that can both fire and fail (or make tie-break explicit, e.g. by rule id), because SS silently uses file order.
8. Shadow detection: SS comments out behaviors after an unconditional transition (`// unreachable behavior`). Do the same as a build warning/error for rules dominated by a higher-priority unconditional blocker.
9. Consume semantic: a per-handler local `consume_event` flag + "call parent if not consumed" gives layered rule sets (base game rules, card rules). Flatten the inheritance at generation time like Balanced2's dispatch ("First ancestor handler for this event") and keep the fall-through only where a rule may decline.

Flattening / codegen shape
10. Nested switch (state, then event) with direct calls and NO runtime table is Balanced2; the exact analog is `switch(eventId)` -> `Event_Damage(ref entity)` where the body is a straight priority-sorted chain. Entities need no per-instance dispatch state at all (only the data).
11. Pre-order ids with contiguous subtree ranges give O(1) `isDescendant`; useful for layers/tags/status hierarchies (`StateIdCount`, `X_SubtreeEndId`), emitted as `const int`.
12. Shared pseudo-state functions when in-degree > 1, inline otherwise: use for shared sub-chains; compute in-degree after all lowering passes (SS fixed a bug where initial-state parent incoming counts were ignored).
13. Optional runtime reflection kept to static switch functions (`StateIdToString`, `GetParentId`) behind settings flags; safe pattern for debug names without strings on the hot path.
14. Safety net `default: return;` in loops that walk by id (prevents infinite loop on corrupted memory); and a build-time check that every visited state had its exit adjustment (`FinalCheck`, issue #391).
15. Use `do` = tick: SS's special event that every ancestor receives and that is not consumed unless a transition happens maps to the timeline tick event.

Testing by simulating
16. SS's `ST\spec` suite: one diagram (`Spec2Sm.graphml`), generated for every language/algorithm, built and RUN as a separate process with a script of events; every behavior/guard/action calls a `trace` hook; the test compares the text trace with expected (`SpecTester.cs`, `Spec2Tests.cs`: e.g. `Test3_BehaviorOrdering` asserts "1 woot!, 2 woot!, 3 woot!" order). Copy it as differential testing: slow reference interpreter over the rule graph vs compiled code on random entity states, comparing final stat/intrinsic arrays + a rule-firing trace.
17. Cheap golden/text tests (`ST\Output\Algos\AlgoTests.cs`: assert generated C contains/excludes `switch (sm->state_id)`), JSON export of the graph before and after transformations (`Output\SmGraphJsonExporter.cs`) for snapshot tests of passes in isolation.
18. In-browser simulator (`Output\Sim\SimWebGenerator.cs`) is the same pipeline with the JS transpiler plus a `TrackingExpander` that records which behaviors/edges fired: a model for a designer-facing "why did this rule not fire" viewer.

Dead ends / things NOT to copy
- Balanced1 runtime function-pointer tables (per-instance pointer array rewritten on each transition) conflict with the "no runtime lookup" goal; only Balanced2 fits.
- Instance-bound handler methods / `sm->` access pattern: for a million entities use static methods taking `ref EntityData` or SoA lanes (offset contract).
- Opaque string guards, name-sorted ids, tie-breaking by file order, comment-heavy output, the sort-in-loop.

---------------------------------------------------------------------------------------------------
## 7. Test suite pointers (for reuse ideas)
- `ST\spec\SpecTester.cs`, `SpecFixture.cs`, `spec\2\Spec2Tests.cs` (abstract; per-language subclasses in `spec\2\CSharp|Cpp|Java|JavaScript|Python|TypeScript|c`), process runner `ST\Processes\SimpleProcess` (e.g. `dotnet bin/Debug/net9.0/Spec2Test.dll EV1 EV2`).
- README says 730+ tests; every language runs the same spec through one trace format. Generated reference outputs are committed (`spec\2\c\Spec2Sm.c`, `src\test-misc\csharp-spec2`).
- `ST\Output\Algos\AlgoTests.cs` generates the full algorithm x transpiler matrix into `out\` and `out2\` (these committed outputs are the best quick-look files).

---------------------------------------------------------------------------------------------------
## 8. Quick file index
- Pipeline list: `src\Runner\StandardSmTransformer.cs`, `SmTransformer.cs`, `InputSmBuilder.cs`, `SmRunnerInternal.cs`
- Priority: `src\SmGraph\Behavior.cs`, `OrderAndElseProcessor.cs`, `ElseGuardProcessor.cs`; docs: `docs\diagram-features.md` ("behavior ordering")
- History: `src\SmGraph\HistoryProcessor.cs`; docs `docs\history-vertex.md`
- Entry/exit: `src\SmGraph\EntryExitProcessor.cs`
- Emission core: `src\Output\Algos\Balanced1\EventHandlerBuilder.cs`, `Balanced2\EventHandlerBuilder2.cs`, `AlgoBalanced1.cs`, `AlgoBalanced2.cs`
- Ids: `src\Output\Algos\Balanced1\EnumBuilder.cs`, `SubtreeIdMapper.cs`; pseudo-state funcs `PseudoStateHandlerBuilder.cs`
- GIL: `src\Output\Gil\*`; transpilers `src\Output\Gil\<Lang>\*`
- Expansions: `src\Input\Expansions\*`, `src\Output\UserConfig\*`
- Validation: `src\SmGraph\Validation\*`, `PseudoLoopDetector.cs`

---------------------------------------------------------------------------------------------------
## 9. What I did NOT verify (be skeptical of these)
- Did not build or run SS or any generated code; every scaling statement in section 5 is inference from reading code and the size of committed outputs, no timing or memory measurements.
- Did not read: the ANTLR-generated parsers, DrawIo/Yed/PlantUML parsing internals, `ExpandingVisitor`, TriggerMap internals, the non-C# visitors (C99/C++/Python/Java/JS/TS bodies), the CLI (`src\StateSmith.Cli`), `SmDesignDescriber`, or most of `HtmlRenderer`.
- The ".NET JIT ~60 KB IL / 2000 basic blocks MinOpts" claim is from my memory of the CLR, not from this repo; test on the actual Unity/Burst version.
- "Tail-chain optimization" is not a name used in this repo; I mapped it to the nearest real optimizations (section 2.7). If you meant something from a different tool or wiki page I did not see it.
- I did not check whether C# method calls in `Balanced2` ancestor fall-through are actually tail-called by the JIT.
- The examples repo outputs are mostly from older SS versions (0.9.x to 0.17); I used the library repo's committed outputs (current HEAD behavior) for all quoted code except struct layouts of Balanced1 in C.
- Doc claims about "wiki" pages (Algorithms, Multiple-Language-Support) were not read (not in the repo).
