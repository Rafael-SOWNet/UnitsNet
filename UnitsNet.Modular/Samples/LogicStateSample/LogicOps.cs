// Licensed under MIT No Attribution, see LICENSE file at the root.

// Probe: can a consumer add the logical operators the generator does not emit?
//
// The generator writes `public readonly partial struct LogicState`, so this file
// declares the other half of the same struct and supplies &, |, ^, !, ~ and an
// explicit collapse to bool. No upstream change and no generator flag involved.
//
// Semantics chosen here, stated rather than assumed:
//   - operands are read in the canonical Logic sense, so opposite-polarity senses
//     combine correctly instead of by raw stored value;
//   - the result is returned in the LEFT operand's sense, which is what an
//     assignment back into that channel wants;
//   - min / max / 1-x (Zadeh) are used, which agree with boolean AND/OR/NOT on
//     {0,1} and stay meaningful over [0,1]. Probabilistic (x*y) would differ off
//     the corners; picking between them is exactly the choice a library should not
//     make silently for its users;
//   - going to bool is a call with a stated threshold, never an implicit cast, and
//     an unknown (NaN) reading is refused rather than collapsed. See below.

namespace Probe.Logic;

public readonly partial struct LogicState
{
    private static LogicState InLeftSense(double canonical, LogicState left) =>
        new LogicState(canonical, LogicStateUnit.Logic).ToUnit(left.Unit);

    public static LogicState operator &(LogicState left, LogicState right) =>
        InLeftSense(System.Math.Min(left.Logic, right.Logic), left);

    public static LogicState operator |(LogicState left, LogicState right) =>
        InLeftSense(System.Math.Max(left.Logic, right.Logic), left);

    public static LogicState operator ^(LogicState left, LogicState right) =>
        InLeftSense(System.Math.Abs(left.Logic - right.Logic), left);

    public static LogicState operator ~(LogicState value) =>
        InLeftSense(1 - value.Logic, value);

    public static LogicState operator !(LogicState value) => ~value;

    // There is deliberately no `operator true` / `operator false` here, so `if (channel)` does
    // not compile. Going to bool is a collapse of the whole [0,1] interval onto two points, and
    // the collapse point is a decision: 0.5 suits a fuzzy reading, 0.9 a probability you are
    // willing to act on, 1.0 a signal you require to be fully asserted. An implicit conversion
    // picks it for the caller and hides it. This design says collapse is an explicit operation,
    // never an implicit cast, so the threshold is a required argument with no default.
    //
    // It also removes the trap the pair had: bool has no NaN, and `Logic >= 0.5` and
    // `Logic < 0.5` are BOTH false for one, so `if (channel)` and `if (~channel)` took the same
    // branch and an unknown reading reached the caller as a definite false one.

    /// <summary>True when the canonical value reaches <paramref name="threshold"/>.</summary>
    /// <exception cref="InvalidOperationException">The value is unknown (NaN).</exception>
    public bool IsAsserted(double threshold) => Readable().Logic >= RequireThreshold(threshold);

    /// <summary>
    ///     True when the value reaches <paramref name="threshold"/> read in
    ///     <paramref name="sense"/> -- which for an opposite-polarity sense is the opposite
    ///     question to the canonical one.
    /// </summary>
    /// <exception cref="InvalidOperationException">The value is unknown (NaN).</exception>
    public bool IsAsserted(LogicStateUnit sense, double threshold) => Readable().As(sense) >= RequireThreshold(threshold);

    /// <summary>
    ///     Whether the value is known at all. The only question that is safe to ask of an
    ///     unknown reading, and the guard a caller uses before collapsing one.
    /// </summary>
    public bool IsKnown => !double.IsNaN(Logic);

    private LogicState Readable()
        => IsKnown
            ? this
            : throw new InvalidOperationException(
                "An unknown (NaN) LogicState cannot be collapsed to true or false. Unknown is "
                + "not false: check IsKnown first, or carry the unknown on its own channel.");

    // Named RequireThreshold, not Valid: the generated half already has a Valid member, from
    // the Valid/Invalid sense pair, and a partial struct shares one namespace of member names
    // with whatever the generator wrote. A downstream half has to work around the catalogue.
    private static double RequireThreshold(double threshold)
        => double.IsNaN(threshold)
            ? throw new ArgumentOutOfRangeException(nameof(threshold), "The threshold cannot be NaN.")
            : threshold;
}
