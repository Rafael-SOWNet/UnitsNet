// Licensed under MIT No Attribution, see LICENSE file at the root.

// The assertions behind the claim of this sample: that a consumer can supply
// the logical operators the generator does not emit, by declaring the other half of the
// generated partial struct. LogicOps.cs is that half; this file holds it to its stated
// semantics.
//
// These lived in a scratch probe once and were not kept, which meant LogicOps.cs compiled
// on every build and was never executed by one. Now the run fails if any of it is wrong.
//
// What is checked, in the order it appears below:
//   1. the AND / OR / XOR truth tables on {0,1}, where Zadeh must agree with boolean logic
//   2. NOT, via both ~ and !
//   3. the result comes back in the LEFT operand's sense, and the right operand's sense
//      does not leak into it
//   4. operands are read in the canonical sense, so opposite polarity combines by meaning
//      rather than by stored value -- the case that motivated the whole design
//   5. Zadeh rather than probabilistic, at the interior point where the two disagree
//   6. De Morgan over a 3x3 grid, as a structural check rather than a table of examples
//   7. NaN propagating through every value operator, from either side, keeping the unit
//   8. collapsing to bool taking a stated threshold, and refusing an unknown reading


namespace Probe.Logic;

internal static class OperatorChecks
{
    private const double Tolerance = 1e-12;

    private static int _checks;
    private static int _failures;

    /// <summary>Runs every check and reports. Returns the number of failures.</summary>
    public static int Run()
    {
        _checks = 0;
        _failures = 0;

        Console.WriteLine();
        Console.WriteLine("════════ The downstream operators, held to their semantics ════════");

        TruthTables();
        Negation();
        LeftOperandSense();
        OppositePolarity();
        ZadehNotProbabilistic();
        DeMorgan();
        NanPropagates();
        ExplicitThreshold();

        Console.WriteLine();
        Console.WriteLine($"  {_checks} checks, {_failures} failures");
        if (_failures == 0)
        {
            Console.WriteLine("  So the partial-struct route works: no generator flag, no upstream change.");
        }

        return _failures;
    }

    /// <summary>Canonical-sense shorthand. Everything without an explicit unit is a Logic value.</summary>
    private static LogicState L(double canonical) => LogicState.FromLogic(canonical);

    // 1. On {0,1} min/max/|a-b| have to reproduce boolean AND/OR/XOR exactly, or the choice
    //    of t-norm would be visible at the corners as well as in the interior.
    private static void TruthTables()
    {
        Console.WriteLine();
        Console.WriteLine("  on {0,1}, min/max/|a-b| must agree with boolean AND/OR/XOR");
        Console.WriteLine("    a  b  |  and  or  xor");

        foreach (double a in new[] { 0d, 1d })
        {
            foreach (double b in new[] { 0d, 1d })
            {
                LogicState and = L(a) & L(b);
                LogicState or = L(a) | L(b);
                LogicState xor = L(a) ^ L(b);

                Canonical(and, System.Math.Min(a, b), $"{a} & {b}");
                Canonical(or, System.Math.Max(a, b), $"{a} | {b}");
                Canonical(xor, System.Math.Abs(a - b), $"{a} ^ {b}");

                Console.WriteLine($"    {a}  {b}  |  {and.Logic}    {or.Logic}   {xor.Logic}");
            }
        }
    }

    // 2. ! is defined as ~, so both have to behave identically; checking only one would let
    //    a later edit to either drift.
    private static void Negation()
    {
        Console.WriteLine();
        Console.WriteLine("  ~ and ! both negate, and agree with each other");

        foreach (double v in new[] { 0d, 1d })
        {
            Canonical(~L(v), 1 - v, $"~{v}");
            Canonical(!L(v), 1 - v, $"!{v}");
        }
    }

    // 3. The result is returned in the left operand's sense, because that is what an
    //    assignment back into that channel wants. The right operand contributes its value,
    //    never its unit.
    private static void LeftOperandSense()
    {
        Console.WriteLine();
        Console.WriteLine("  the result carries the LEFT operand's unit, whichever side is which");

        LogicState closedThenLogic = LogicState.From(1, LogicStateUnit.Closed) | L(1);
        Unit(closedThenLogic, LogicStateUnit.Closed, "Closed(1) | Logic(1)");

        LogicState logicThenClosed = L(1) | LogicState.From(1, LogicStateUnit.Closed);
        Unit(logicThenClosed, LogicStateUnit.Logic, "Logic(1) | Closed(1)");

        // Same operands both ways round: the value agrees, the unit does not, and the two
        // printed forms therefore differ. That asymmetry is the design, not a defect.
        Canonical(closedThenLogic, 1, "Closed(1) | Logic(1), canonical value");
        Canonical(logicThenClosed, 1, "Logic(1) | Closed(1), canonical value");
        Console.WriteLine($"    Closed(1) | Logic(1) = {closedThenLogic.As(LogicStateUnit.Closed)} in Closed");
        Console.WriteLine($"    Logic(1) | Closed(1) = {logicThenClosed.As(LogicStateUnit.Logic)} in Logic");
    }

    // 4. The case the whole thing exists for. Closed is opposite-polarity, so a raw 1 on a
    //    Closed channel is canonically FALSE. Combining by stored value would read it as
    //    true and invert every active-low signal in the plant.
    private static void OppositePolarity()
    {
        Console.WriteLine();
        Console.WriteLine("  operands are read by meaning, not by stored value");

        LogicState closedHigh = LogicState.From(1, LogicStateUnit.Closed);
        Canonical(closedHigh, 0, "Closed(1) is canonically false");

        LogicState and = closedHigh & L(1);
        Canonical(and, 0, "Closed(1) & Logic(1)");
        Check(!Truthy(and), "Closed(1) & Logic(1) collapses to false");
        Check(Truthy(L(1) & L(1)), "Logic(1) & Logic(1) collapses to true");

        Console.WriteLine($"    Closed(1) & Logic(1) -> canonical {and.Logic}, reads {Truthy(and)}");
        Console.WriteLine("    combining by raw value would have made this true, and been wrong.");
    }

    // 5. Where the choice of t-norm becomes visible. Zadeh's min/max and the probabilistic
    //    x*y / x+y-xy coincide on {0,1} and disagree everywhere inside, so 0.5 is the point
    //    that pins down which one LogicOps.cs actually implements.
    private static void ZadehNotProbabilistic()
    {
        Console.WriteLine();
        Console.WriteLine("  at 0.5, Zadeh rather than probabilistic");

        Canonical(L(0.5) & L(0.5), 0.5, "0.5 & 0.5 is min, not the product 0.25");
        Canonical(L(0.5) | L(0.5), 0.5, "0.5 | 0.5 is max, not 0.75");
        Canonical(~L(0.25), 0.75, "~0.25 is 1-x");

        Console.WriteLine("    0.5 & 0.5 = 0.5   (probabilistic would give 0.25)");
        Console.WriteLine("    0.5 | 0.5 = 0.5   (probabilistic would give 0.75)");
    }

    // 6. A property rather than a table: whatever the operators do, De Morgan has to hold
    //    across the interior too, which no finite list of worked examples would establish.
    private static void DeMorgan()
    {
        Console.WriteLine();
        Console.WriteLine("  ~(a & b) == ~a | ~b, over {0, 0.5, 1} squared");

        double[] grid = [0, 0.5, 1];
        foreach (double a in grid)
        {
            foreach (double b in grid)
            {
                double left = (~(L(a) & L(b))).Logic;
                double right = (~L(a) | ~L(b)).Logic;
                Check(System.Math.Abs(left - right) < Tolerance, $"De Morgan at ({a}, {b}): {left} vs {right}");
            }
        }
    }

    // 7. An unknown reading has to stay unknown. min/max/|a-b|/1-x are all NaN-propagating
    //    under IEEE 754, so this holds without any special-casing -- but it holds by accident
    //    unless something says so, and a later switch to a clamping or short-circuiting
    //    implementation would silently turn every unknown into a definite answer.
    private static void NanPropagates()
    {
        Console.WriteLine();
        Console.WriteLine("  NaN propagates through every value operator, from either side");

        LogicState nan = L(double.NaN);

        foreach (double other in new[] { 0d, 0.5, 1d })
        {
            Nan(nan & L(other), $"NaN & {other}");
            Nan(L(other) & nan, $"{other} & NaN");
            Nan(nan | L(other), $"NaN | {other}");
            Nan(L(other) | nan, $"{other} | NaN");
            Nan(nan ^ L(other), $"NaN ^ {other}");
            Nan(L(other) ^ nan, $"{other} ^ NaN");
        }

        Nan(~nan, "~NaN");
        Nan(!nan, "!NaN");
        Nan(nan & nan, "NaN & NaN");

        // Unknown in an opposite-polarity sense is still unknown, and still in that sense:
        // the unit must survive even though the value cannot be converted meaningfully.
        LogicState closedUnknown = LogicState.From(double.NaN, LogicStateUnit.Closed);
        LogicState combined = closedUnknown & L(1);
        Nan(combined, "Closed(NaN) & Logic(1)");
        Unit(combined, LogicStateUnit.Closed, "Closed(NaN) & Logic(1)");

        Console.WriteLine("    an unknown reading stays unknown, in the sense it arrived in");
    }

    // 8. There is no implicit road to bool at all now, so the collapse is a call with a stated
    //    threshold. Two things to hold it to: the threshold has to actually move the answer,
    //    or it would be decoration on a hard-coded 0.5; and an unknown reading has to be
    //    refused rather than collapsed, since bool has no NaN to collapse it into.
    private static void ExplicitThreshold()
    {
        Console.WriteLine();
        Console.WriteLine("  collapsing to bool takes a stated threshold, and refuses an unknown");

        LogicState half = L(0.5);
        LogicState nan = L(double.NaN);

        // The same value, three thresholds, three answers -- which is the whole reason the
        // argument is required rather than defaulted.
        Check(half.IsAsserted(0.4), "0.5 is asserted at a threshold of 0.4");
        Check(half.IsAsserted(0.5), "0.5 is asserted at a threshold of 0.5");
        Check(!half.IsAsserted(0.6), "0.5 is NOT asserted at a threshold of 0.6");
        Check(!half.IsAsserted(1.0), "0.5 is NOT asserted where full assertion is required");

        // Read in a sense rather than canonically: Closed is opposite-polarity, so the same
        // value answers the opposite question.
        LogicState closedHigh = LogicState.From(1, LogicStateUnit.Closed);
        Check(closedHigh.IsAsserted(LogicStateUnit.Closed, 0.5), "Closed(1) is asserted in the Closed sense");
        Check(!closedHigh.IsAsserted(0.5), "Closed(1) is not asserted canonically");

        // Unknown: refused on every route to bool, including after negating it.
        Check(Throws<InvalidOperationException>(() => nan.IsAsserted(0.5)), "NaN.IsAsserted throws");
        Check(Throws<InvalidOperationException>(() => (~nan).IsAsserted(0.5)),
            "~NaN.IsAsserted throws too -- negating an unknown does not resolve it");
        Check(Throws<InvalidOperationException>(() => nan.IsAsserted(LogicStateUnit.Closed, 0.5)),
            "NaN.IsAsserted(sense, t) throws as well");
        Check(!nan.IsKnown, "NaN reports itself unknown, which is the one safe question");
        Check(L(0).IsKnown && L(1).IsKnown, "known values report themselves known");

        // A NaN threshold would compare false against everything and read as 'not asserted',
        // which is the same silent collapse one level up.
        Check(Throws<ArgumentOutOfRangeException>(() => L(1).IsAsserted(double.NaN)),
            "a NaN threshold is rejected rather than silently answering false");

        Console.WriteLine("    0.5 asserted at 0.4 and 0.5, not at 0.6 or 1.0");
        Console.WriteLine("    NaN refused on every route to bool; `if (channel)` no longer compiles");
    }

    private static bool Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private static void Nan(LogicState actual, string what)
        => Check(double.IsNaN(actual.Logic), $"{what}: expected NaN, got {actual.Logic}");

    /// <summary>The collapse, at the conventional midpoint, for the checks that need a bool.</summary>
    private static bool Truthy(LogicState state) => state.IsAsserted(0.5);

    private static void Canonical(LogicState actual, double expected, string what)
        => Check(System.Math.Abs(actual.Logic - expected) < Tolerance,
            $"{what}: expected canonical {expected}, got {actual.Logic}");

    private static void Unit(LogicState actual, LogicStateUnit expected, string what)
        => Check(actual.Unit == expected, $"{what}: expected unit {expected}, got {actual.Unit}");

    private static void Check(bool condition, string what)
    {
        _checks++;
        if (condition)
        {
            return;
        }

        _failures++;
        Console.WriteLine($"    FAIL {what}");
    }
}
