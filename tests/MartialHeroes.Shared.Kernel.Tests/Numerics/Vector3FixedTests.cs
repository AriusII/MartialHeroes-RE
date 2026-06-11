using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Shared.Kernel.Tests.Numerics;

/// <summary>
/// Deterministic, headless tests for <see cref="Vector3Fixed"/> (Q16.16 fixed-point vector).
///
/// All expected raw values are computed symbolically from the definition
///   One = 1 &lt;&lt; 16 = 65 536
/// and verified by hand; no floating-point tolerance is used in the deterministic path.
/// The float-bridge tests use a tolerance of 1/65536 (one Q16.16 ULP) because
/// FromFloat truncates via (int)(x * One), so the round-trip error is in [0, 1/One).
/// </summary>
public sealed class Vector3FixedTests
{
    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    /// <summary>
    /// One real unit in Q16.16 raw representation.
    /// spec: CLAUDE.md — "One = 1 &lt;&lt; 16 = 65536"
    /// </summary>
    private const int One = Vector3Fixed.One; // must equal 65536

    // -------------------------------------------------------------------------
    // 1. Whole-unit construction round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public void FromWholeUnits_PositiveComponents_RawEqualsValueTimesOne()
    {
        // spec: Vector3Fixed.FromWholeUnits(x,y,z) => new(x*One, y*One, z*One)
        var v = Vector3Fixed.FromWholeUnits(3, 5, 7);
        Assert.Equal(3 * One, v.RawX);
        Assert.Equal(5 * One, v.RawY);
        Assert.Equal(7 * One, v.RawZ);
    }

    [Fact]
    public void FromWholeUnits_NegativeComponents_RawEqualsValueTimesOne()
    {
        var v = Vector3Fixed.FromWholeUnits(-2, -4, -6);
        Assert.Equal(-2 * One, v.RawX);
        Assert.Equal(-4 * One, v.RawY);
        Assert.Equal(-6 * One, v.RawZ);
    }

    [Fact]
    public void Constructor_RawComponents_StoredExactly()
    {
        // Direct raw construction: raw integers stored without modification.
        var v = new Vector3Fixed(1, 2, 3);
        Assert.Equal(1, v.RawX);
        Assert.Equal(2, v.RawY);
        Assert.Equal(3, v.RawZ);
    }

    // -------------------------------------------------------------------------
    // 2. Zero identity
    // -------------------------------------------------------------------------

    [Fact]
    public void Zero_AllRawComponentsAreZero()
    {
        Assert.Equal(0, Vector3Fixed.Zero.RawX);
        Assert.Equal(0, Vector3Fixed.Zero.RawY);
        Assert.Equal(0, Vector3Fixed.Zero.RawZ);
    }

    [Fact]
    public void Add_ZeroIdentity_ReturnsSameVector()
    {
        var v = Vector3Fixed.FromWholeUnits(1, 2, 3);
        var result = v + Vector3Fixed.Zero;
        Assert.Equal(v, result);
    }

    [Fact]
    public void Add_VectorPlusNegation_EqualsZero()
    {
        var v = Vector3Fixed.FromWholeUnits(5, -3, 7);
        Assert.Equal(Vector3Fixed.Zero, v + (-v));
    }

    // -------------------------------------------------------------------------
    // 3. Addition correctness
    // -------------------------------------------------------------------------

    [Fact]
    public void Add_TwoPositiveWholeUnitVectors_SumsComponentWise()
    {
        // (1,2,3) + (4,5,6) = (5,7,9) in whole units => raw = (5*One, 7*One, 9*One)
        var a = Vector3Fixed.FromWholeUnits(1, 2, 3);
        var b = Vector3Fixed.FromWholeUnits(4, 5, 6);
        var result = a + b;
        Assert.Equal(5 * One, result.RawX);
        Assert.Equal(7 * One, result.RawY);
        Assert.Equal(9 * One, result.RawZ);
    }

    [Fact]
    public void Add_Commutativity_AAddBEqualsBAaddA()
    {
        var a = Vector3Fixed.FromWholeUnits(3, -1, 7);
        var b = Vector3Fixed.FromWholeUnits(-5, 4, 2);
        Assert.Equal(a + b, b + a);
    }

    [Fact]
    public void Add_MixedSignComponents_CorrectRawResult()
    {
        // raw values: a=( One, -One, 0), b=(-One, One, 2*One)
        // result:     (0, 0, 2*One)
        var a = new Vector3Fixed(One, -One, 0);
        var b = new Vector3Fixed(-One, One, 2 * One);
        var result = a + b;
        Assert.Equal(0, result.RawX);
        Assert.Equal(0, result.RawY);
        Assert.Equal(2 * One, result.RawZ);
    }

    // -------------------------------------------------------------------------
    // 4. Subtraction correctness
    // -------------------------------------------------------------------------

    [Fact]
    public void Subtract_TwoWholeUnitVectors_DifferencesComponentWise()
    {
        // (5,7,9) - (1,2,3) = (4,5,6) in whole units
        var a = Vector3Fixed.FromWholeUnits(5, 7, 9);
        var b = Vector3Fixed.FromWholeUnits(1, 2, 3);
        var result = a - b;
        Assert.Equal(4 * One, result.RawX);
        Assert.Equal(5 * One, result.RawY);
        Assert.Equal(6 * One, result.RawZ);
    }

    [Fact]
    public void Subtract_SameVector_EqualsZero()
    {
        var v = Vector3Fixed.FromWholeUnits(3, 3, 3);
        Assert.Equal(Vector3Fixed.Zero, v - v);
    }

    // -------------------------------------------------------------------------
    // 5. Unary negation
    // -------------------------------------------------------------------------

    [Fact]
    public void UnaryNegate_NegatesAllComponents()
    {
        // v = (1*One, -2*One, 3*One)
        // -v = (-1*One, 2*One, -3*One)
        var v = Vector3Fixed.FromWholeUnits(1, -2, 3);
        var neg = -v;
        Assert.Equal(-One, neg.RawX);
        Assert.Equal(2 * One, neg.RawY);
        Assert.Equal(-3 * One, neg.RawZ);
    }

    [Fact]
    public void UnaryNegate_DoubleNegation_ReturnsSameVector()
    {
        var v = Vector3Fixed.FromWholeUnits(4, -5, 6);
        Assert.Equal(v, -(-v));
    }

    // -------------------------------------------------------------------------
    // 6. Scalar multiplication — Q16.16 semantics
    // -------------------------------------------------------------------------

    [Fact]
    public void Multiply_ByOne_ReturnsSameVector()
    {
        // scalar = 1.0 in Q16.16 = One = 65536
        // (a * One) >> 16 = a   (exact, no rounding for whole-unit values)
        var v = Vector3Fixed.FromWholeUnits(3, 5, 7);
        var result = v * One; // multiply by Q16.16 representation of 1.0
        Assert.Equal(v, result);
    }

    [Fact]
    public void Multiply_ByTwo_DoublesRawComponents()
    {
        // scalar = 2.0 in Q16.16 = 2 * One
        // result.RawX = (v.RawX * 2*One) >> 16 = v.RawX * 2
        var v = Vector3Fixed.FromWholeUnits(3, 5, 7);
        var result = v * (2 * One);
        Assert.Equal(6 * One, result.RawX);
        Assert.Equal(10 * One, result.RawY);
        Assert.Equal(14 * One, result.RawZ);
    }

    [Fact]
    public void Multiply_HalfByHalf_YieldsQuarter()
    {
        // 0.5 in Q16.16 = One/2 = 32768
        // 0.5 * 0.5 in Q16.16: ((long)32768 * 32768) >> 16
        //   = 1073741824 >> 16 = 16384 = One/4  =>  0.25 in real units
        int half = One / 2;      // 32768 — Q16.16 representation of 0.5
        int quarter = One / 4;   // 16384 — Q16.16 representation of 0.25

        var v = new Vector3Fixed(half, half, half);
        var result = v * half;   // 0.5 * 0.5

        Assert.Equal(quarter, result.RawX);
        Assert.Equal(quarter, result.RawY);
        Assert.Equal(quarter, result.RawZ);
    }

    [Fact]
    public void Multiply_LargeMagnitude_LongIntermediatePreventsOverflow()
    {
        // Verify the long intermediate prevents int32 overflow.
        // v.RawX = int.MaxValue / 2 = 1073741823
        // scalar  = 2 * One = 131072
        // Without long: 1073741823 * 131072 would overflow int32.
        // With long:  ((long)1073741823 * 131072) >> 16
        //           = 140737488158720 >> 16
        //           = 2147483646
        int rawX = int.MaxValue / 2; // 1073741823
        int scalar = 2 * One;        // 131072

        long expectedLong = ((long)rawX * scalar) >> 16; // 2147483646
        int expected = (int)expectedLong;

        var v = new Vector3Fixed(rawX, 0, 0);
        var result = v * scalar;

        Assert.Equal(expected, result.RawX);
        Assert.Equal(0, result.RawY);
        Assert.Equal(0, result.RawZ);
    }

    [Fact]
    public void Multiply_WholeUnitValues_ScalesCorrectly()
    {
        // 1000.0 * 3.0 = 3000.0 in Q16.16
        // raw: (1000*One) * (3*One) >> 16 = 3000*One
        var v = Vector3Fixed.FromWholeUnits(1000, 0, 0);
        var result = v * (3 * One);
        Assert.Equal(3000 * One, result.RawX);
    }

    [Fact]
    public void Multiply_Commutativity_ScalarTimesVectorEqualsVectorTimesScalar()
    {
        var v = Vector3Fixed.FromWholeUnits(2, 3, 5);
        int scalar = 3 * One;
        Assert.Equal(v * scalar, scalar * v);
    }

    [Fact]
    public void Multiply_ByZero_YieldsZeroVector()
    {
        var v = Vector3Fixed.FromWholeUnits(10, 20, 30);
        Assert.Equal(Vector3Fixed.Zero, v * 0);
    }

    // -------------------------------------------------------------------------
    // 7. Equality and inequality
    // -------------------------------------------------------------------------

    [Fact]
    public void Equality_SameRawComponents_AreEqual()
    {
        var a = new Vector3Fixed(1, 2, 3);
        var b = new Vector3Fixed(1, 2, 3);
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void Equality_DifferentRawX_AreNotEqual()
    {
        var a = new Vector3Fixed(1, 2, 3);
        var b = new Vector3Fixed(9, 2, 3);
        Assert.NotEqual(a, b);
        Assert.False(a == b);
        Assert.True(a != b);
    }

    [Fact]
    public void Equality_DifferentRawY_AreNotEqual()
    {
        var a = new Vector3Fixed(1, 2, 3);
        var b = new Vector3Fixed(1, 9, 3);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equality_DifferentRawZ_AreNotEqual()
    {
        var a = new Vector3Fixed(1, 2, 3);
        var b = new Vector3Fixed(1, 2, 9);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equality_ViaObject_WorksCorrectly()
    {
        object a = new Vector3Fixed(5, 6, 7);
        object b = new Vector3Fixed(5, 6, 7);
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void GetHashCode_EqualVectors_SameHash()
    {
        var a = new Vector3Fixed(10, 20, 30);
        var b = new Vector3Fixed(10, 20, 30);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    // -------------------------------------------------------------------------
    // 8. Float bridge — quantisation tolerance
    // -------------------------------------------------------------------------
    // The Q16.16 quantisation step is 1/65536 ≈ 1.526e-5 real units.
    // FromFloat uses (int)(x * One) which truncates toward zero, so the
    // round-trip error |ToVector3Float() - original| is in [0, 1/One).
    // We assert each component is within 1/One (exclusive) of the original.

    private const float QuantisationTolerance = 1.0f / One; // ≈ 1.526e-5

    [Theory]
    [InlineData(0.0f, 0.0f, 0.0f)]
    [InlineData(1.0f, 0.0f, 0.0f)]
    [InlineData(0.0f, 1.0f, 0.0f)]
    [InlineData(0.0f, 0.0f, 1.0f)]
    [InlineData(1.5f, 2.5f, 3.5f)]
    [InlineData(-1.5f, -2.5f, -3.5f)]
    [InlineData(3.14159f, 2.71828f, 1.41421f)]
    [InlineData(100.0f, 200.0f, 300.0f)]
    [InlineData(-50.125f, 75.0625f, -0.0001f)]
    public void FloatBridge_RoundTrip_WithinOneQULP(float x, float y, float z)
    {
        // Quantisation error: |roundtrip - original| < 1/One for each component.
        // This is ONE Q16.16 ULP = 1/65536 ≈ 1.526e-5.
        var v = Vector3Fixed.FromFloat(x, y, z);
        var (rx, ry, rz) = v.ToVector3Float();

        Assert.True(MathF.Abs(rx - x) < QuantisationTolerance,
            $"X round-trip error {MathF.Abs(rx - x)} >= tolerance {QuantisationTolerance}");
        Assert.True(MathF.Abs(ry - y) < QuantisationTolerance,
            $"Y round-trip error {MathF.Abs(ry - y)} >= tolerance {QuantisationTolerance}");
        Assert.True(MathF.Abs(rz - z) < QuantisationTolerance,
            $"Z round-trip error {MathF.Abs(rz - z)} >= tolerance {QuantisationTolerance}");
    }

    [Fact]
    public void FloatBridge_FromWholeUnits_ExactRoundTrip()
    {
        // Whole-integer values convert exactly through the float bridge
        // because they are multiples of One: no fractional quantisation error.
        var v = Vector3Fixed.FromWholeUnits(5, -3, 10);
        var (x, y, z) = v.ToVector3Float();
        Assert.Equal(5.0f, x);
        Assert.Equal(-3.0f, y);
        Assert.Equal(10.0f, z);
    }
}
