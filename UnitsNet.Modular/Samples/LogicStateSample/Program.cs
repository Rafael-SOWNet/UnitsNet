// Licensed under MIT No Attribution, see LICENSE file at the root.

using System.Globalization;
using System.Reflection;
using UnitsNet.Modular;

namespace Probe.Logic;

[QuantitySpec("Probe.Logic.LogicState")]
public interface LogicStateSpec
{
}

[UnitsNetModule]
internal interface LogicUnits : IInclude<LogicStateSpec>
{
}

internal static class Program
{
    private static int _checks;
    private static int _failures;

    public static void Main()
    {
        Section("1. Generated unit catalogue");
        foreach (LogicStateUnit unit in Enum.GetValues<LogicStateUnit>())
        {
            Console.WriteLine($"   {(int)unit,3}  {unit}");
        }

        Section("2. Polarity round-trip, all units");
        // Every unit must survive base -> unit -> base for both logical values.
        foreach (LogicStateUnit unit in Enum.GetValues<LogicStateUnit>())
        {
            foreach (double v in new[] { 0d, 1d })
            {
                LogicState canonical = LogicState.FromLogic(v);
                double there = canonical.As(unit);
                double back = LogicState.From(there, unit).As(LogicStateUnit.Logic);
                Check($"{unit,-12} {v} -> {there} -> {back}", back == v);
            }
        }

        Section("3. The KNX case: DPT 1.009 vs DPT 1.019");
        // KNX standardises the same physical condition in both polarities:
        //   DPT_OpenClose   (1.009): 0 = open,   1 = close
        //   DPT_Window_Door (1.019): 0 = closed, 1 = open
        // A door that is physically open is 1 in Open and 0 in Closed.
        LogicState doorOpen = LogicState.FromOpen(1);
        Check($"door open  -> Closed sense = {doorOpen.As(LogicStateUnit.Closed)}",
            doorOpen.As(LogicStateUnit.Closed) == 0);
        Check($"door open  -> Logic  sense = {doorOpen.As(LogicStateUnit.Logic)}",
            doorOpen.As(LogicStateUnit.Logic) == 1);

        LogicState doorShut = LogicState.FromClosed(1);
        Check($"door shut  -> Open   sense = {doorShut.As(LogicStateUnit.Open)}",
            doorShut.As(LogicStateUnit.Open) == 0);

        Section("4. Cross-sense conversion (same polarity is identity)");
        LogicState alarm = LogicState.FromAlarms(1);
        Console.WriteLine($"   Alarm(1) as Active   = {alarm.As(LogicStateUnit.Active)}");
        Console.WriteLine($"   Alarm(1) as NoAlarm  = {alarm.As(LogicStateUnit.NoAlarm)}");
        Console.WriteLine($"   Alarm(1) as Occupied = {alarm.As(LogicStateUnit.Occupied)}   <-- numerically fine, semantically meaningless");

        Section("5. Parsing and formatting");
        TryParse("1 on");
        TryParse("0 off");
        TryParse("1 open");
        TryParse("1 closed");
        TryParse("1 alm");
        Console.WriteLine($"   ToString(FromOn(1))     = {LogicState.FromOn(1)}");
        Console.WriteLine($"   ToString(FromClosed(1)) = {LogicState.FromClosed(1)}");

        Section("6. Values outside {0,1} -- probability / fuzzy truth");
        // 1 - x is simultaneously:
        //   * boolean NOT on {0,1}
        //   * the probability complement P(not A) = 1 - P(A)
        //   * Zadeh fuzzy NOT
        // so the polarity conversion stays correct across the whole interval.
        foreach (double p in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
        {
            LogicState s = LogicState.FromLogic(p);
            double complement = s.As(LogicStateUnit.Off);
            Check($"P(true)={p:0.00}  complement={complement:0.00}",
                Math.Abs(complement - (1 - p)) < 1e-12);
        }

        Console.WriteLine();
        Console.WriteLine("   Out-of-domain values are NOT rejected:");
        foreach (double bad in new[] { -1d, 2d, double.NaN })
        {
            LogicState s = LogicState.FromLogic(bad);
            Console.WriteLine($"     FromLogic({bad}) accepted, as Off = {s.As(LogicStateUnit.Off)}");
        }

        Section("7. Collapse to a definite value");
        // Collapse is deliberately explicit: a measurement, not an implicit cast.
        Console.WriteLine($"   Collapse(0.75, threshold 0.5) = {Collapse(LogicState.FromLogic(0.75))}");
        Console.WriteLine($"   Collapse(0.25, threshold 0.5) = {Collapse(LogicState.FromLogic(0.25))}");

        Section("8. What operators did the generator actually emit?");
        ReportOperators(typeof(LogicState));

        Section("Summary");
        Console.WriteLine($"   {_checks - _failures}/{_checks} checks passed");
        if (_failures > 0)
        {
            Console.WriteLine($"   {_failures} FAILED");
        }
    }

    /// <summary>Threshold collapse. Explicit by design; there is no implicit bool conversion.</summary>
    private static bool Collapse(LogicState state, double threshold = 0.5)
        => state.As(LogicStateUnit.Logic) >= threshold;

    private static void ReportOperators(Type t)
    {
        MethodInfo[] ops = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.IsSpecialName && m.Name.StartsWith("op_", StringComparison.Ordinal))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToArray();

        if (ops.Length == 0)
        {
            Console.WriteLine("   (none)");
            return;
        }

        foreach (IGrouping<string, MethodInfo> g in ops.GroupBy(m => m.Name))
        {
            Console.WriteLine($"   {g.Key} x{g.Count()}");
        }

        string[] arithmetic = ["op_Addition", "op_Subtraction", "op_Multiply", "op_Division", "op_UnaryNegation"];
        string[] found = [.. ops.Select(o => o.Name).Distinct().Intersect(arithmetic)];
        Console.WriteLine();
        Console.WriteLine(found.Length > 0
            ? $"   ARITHMETIC PRESENT on a logic quantity: {string.Join(", ", found)}"
            : "   No arithmetic operators generated.");

        string[] logical = ["op_BitwiseAnd", "op_BitwiseOr", "op_ExclusiveOr", "op_LogicalNot", "op_OnesComplement"];
        string[] logicalFound = [.. ops.Select(o => o.Name).Distinct().Intersect(logical)];
        Console.WriteLine(logicalFound.Length > 0
            ? $"   Logical operators: {string.Join(", ", logicalFound)}"
            : "   No logical operators generated (AND/OR/XOR/NOT absent).");
    }

    private static void TryParse(string text)
    {
        try
        {
            LogicState parsed = LogicState.Parse(text, CultureInfo.InvariantCulture);
            Console.WriteLine($"   Parse(\"{text}\") = {parsed}  (Logic = {parsed.As(LogicStateUnit.Logic)})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Parse(\"{text}\") FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Check(string label, bool ok)
    {
        _checks++;
        if (!ok)
        {
            _failures++;
        }

        Console.WriteLine($"   [{(ok ? "ok" : "FAIL")}] {label}");
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"== {title} ==");
    }
}
