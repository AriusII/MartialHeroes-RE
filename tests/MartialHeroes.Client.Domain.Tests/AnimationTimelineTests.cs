using MartialHeroes.Client.Domain.Simulation;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

/// <summary>
/// Tests for the deterministic animation clip timeline (10 fps, linear translation, SLERP rotation,
/// clamp-to-last). spec: Docs/RE/formats/animation.md §Timing / §interpolation.
/// </summary>
public sealed class AnimationTimelineTests
{
    private const float Tol = 1e-5f;

    // -------------------------------------------------------------------------
    // Timing constants and frame resolution.
    // -------------------------------------------------------------------------

    [Fact]
    public void Constants_MatchSpec()
    {
        Assert.Equal(10.0f, AnimationTimeline.FramesPerSecond);
        Assert.Equal(0.1f, AnimationTimeline.SecondsPerFrame, precision: 6);
    }

    [Fact]
    public void ResolveFrame_FrameIndexIsFloorOfTimeTimesTen()
    {
        // t = 0.25 -> floor(2.5) = frame 2, alpha = 0.25 - 0.2 = 0.05 (raw seconds, NOT normalised).
        (int lower, int upper, float alpha) = AnimationTimeline.ResolveFrame(0.25f, keyCount: 10);
        Assert.Equal(2, lower);
        Assert.Equal(3, upper);
        Assert.Equal(0.05f, alpha, Tol);
    }

    [Fact]
    public void ResolveFrame_AlphaIsRawSeconds_NotNormalised()
    {
        // spec: animation.md §Timing — alpha is in raw seconds [0, 0.1], not [0,1].
        (_, _, float alpha) = AnimationTimeline.ResolveFrame(0.34f, keyCount: 10);
        // frame 3 (0.3), alpha = 0.04.
        Assert.Equal(0.04f, alpha, Tol);
        Assert.True(alpha <= 0.1f);
    }

    [Fact]
    public void ResolveFrame_NegativeTime_ClampsToZero()
    {
        (int lower, int upper, float alpha) = AnimationTimeline.ResolveFrame(-5f, keyCount: 4);
        Assert.Equal(0, lower);
        Assert.Equal(1, upper);
        Assert.Equal(0f, alpha, Tol);
    }

    [Fact]
    public void ResolveFrame_PastEnd_ClampsToLast()
    {
        // keyCount 4 -> last index 3, covers up to t < 0.4. t = 10 is past end.
        (int lower, int upper, float alpha) = AnimationTimeline.ResolveFrame(10f, keyCount: 4);
        Assert.Equal(3, lower);
        Assert.Equal(3, upper);
        Assert.Equal(0f, alpha, Tol);
    }

    // -------------------------------------------------------------------------
    // Translation: mid-frame linear interpolation.
    // -------------------------------------------------------------------------

    [Fact]
    public void SampleAt_MidFrame_LinearTranslation()
    {
        // Two keyframes 0.1s apart. At t = 0.05 (half a frame), alpha = 0.05 raw seconds.
        // Linear lerp uses alpha directly: result = a + (b - a) * 0.05.
        var trans = new[]
        {
            new AnimTranslation(0f, 0f, 0f),
            new AnimTranslation(10f, 20f, -40f),
        };
        var rots = new[] { AnimRotation.Identity, AnimRotation.Identity };

        AnimPose pose = AnimationTimeline.SampleAt(0.05f, trans, rots);

        // alpha = 0.05 -> 5% of the way (matches legacy raw-seconds blend).
        Assert.Equal(0.5f, pose.Translation.X, Tol);
        Assert.Equal(1.0f, pose.Translation.Y, Tol);
        Assert.Equal(-2.0f, pose.Translation.Z, Tol);
    }

    [Fact]
    public void SampleAt_ExactFrameStart_ReturnsKeyframeExactly()
    {
        var trans = new[]
        {
            new AnimTranslation(1f, 2f, 3f),
            new AnimTranslation(9f, 9f, 9f),
        };
        var rots = new[] { AnimRotation.Identity, AnimRotation.Identity };

        // t = 0.0 -> frame 0, alpha 0 -> exactly keyframe 0.
        AnimPose pose = AnimationTimeline.SampleAt(0f, trans, rots);
        Assert.Equal(1f, pose.Translation.X, Tol);
        Assert.Equal(2f, pose.Translation.Y, Tol);
        Assert.Equal(3f, pose.Translation.Z, Tol);
    }

    [Fact]
    public void SampleAt_PastClipEnd_ClampsToLastKeyframe()
    {
        var trans = new[]
        {
            new AnimTranslation(0f, 0f, 0f),
            new AnimTranslation(5f, 5f, 5f),
        };
        var rots = new[] { AnimRotation.Identity, AnimRotation.Identity };

        // t well past the 0.2s clip end -> clamp to last keyframe (5,5,5).
        AnimPose pose = AnimationTimeline.SampleAt(3f, trans, rots);
        Assert.Equal(5f, pose.Translation.X, Tol);
        Assert.Equal(5f, pose.Translation.Y, Tol);
        Assert.Equal(5f, pose.Translation.Z, Tol);
    }

    [Fact]
    public void SampleAt_SingleKeyframe_AlwaysReturnsIt()
    {
        var trans = new[] { new AnimTranslation(7f, 8f, 9f) };
        var rots = new[] { AnimRotation.Identity };
        AnimPose pose = AnimationTimeline.SampleAt(0.5f, trans, rots);
        Assert.Equal(7f, pose.Translation.X, Tol);
    }

    // -------------------------------------------------------------------------
    // Rotation: shortest-arc SLERP.
    // -------------------------------------------------------------------------

    [Fact]
    public void Slerp_Halfway_BetweenIdentityAndYaw90_IsYaw45()
    {
        // Identity and a 90° rotation about Y. Halfway (alpha = 0.5) should give 45° about Y.
        var q0 = AnimRotation.Identity;
        float half90 = MathF.PI / 4f; // 45°
        var q90 = new AnimRotation(0f, MathF.Sin(half90), 0f, MathF.Cos(half90)); // 90° about Y

        AnimRotation mid = AnimRotation.Slerp(q0, q90, 0.5f);

        // Expected 45° about Y: angle 22.5°.
        float half45 = MathF.PI / 8f;
        Assert.Equal(0f, mid.X, 1e-4f);
        Assert.Equal(MathF.Sin(half45), mid.Y, 1e-4f);
        Assert.Equal(0f, mid.Z, 1e-4f);
        Assert.Equal(MathF.Cos(half45), mid.W, 1e-4f);
    }

    [Fact]
    public void Slerp_ShortArc_NegatesTargetWhenDotNegative()
    {
        // q and -q represent the same orientation. Interpolating from q to (-q rotated slightly)
        // must take the short arc: the sign flip means the result stays near q, not the long way.
        var q0 = AnimRotation.Identity; // (0,0,0,1)
        // A quaternion whose W is negative but represents a small rotation (dot with identity < 0).
        var qNeg = new AnimRotation(0f, 0f, 0f, -1f); // antipodal to identity, same orientation.

        AnimRotation result = AnimRotation.Slerp(q0, qNeg, 0.5f);

        // After short-arc flip, target becomes (0,0,0,1) again -> result is identity-ish.
        Assert.Equal(0f, result.X, 1e-4f);
        Assert.Equal(0f, result.Y, 1e-4f);
        Assert.Equal(0f, result.Z, 1e-4f);
        Assert.Equal(1f, MathF.Abs(result.W), 1e-4f);
    }

    [Fact]
    public void Slerp_NearlyIdentical_FallsBackToNormalizedLerp()
    {
        var q0 = AnimRotation.Identity;
        var q1 = new AnimRotation(0.0001f, 0f, 0f, 1f); // almost parallel
        AnimRotation r = AnimRotation.Slerp(q0, q1, 0.5f);

        // Result must be unit length (normalized fallback).
        float len = MathF.Sqrt(r.X * r.X + r.Y * r.Y + r.Z * r.Z + r.W * r.W);
        Assert.Equal(1f, len, 1e-4f);
    }

    [Fact]
    public void SampleAt_IsDeterministic()
    {
        var trans = new[]
        {
            new AnimTranslation(0f, 0f, 0f),
            new AnimTranslation(3f, -7f, 11f),
        };
        float h = MathF.PI / 4f;
        var rots = new[]
        {
            AnimRotation.Identity,
            new AnimRotation(0f, MathF.Sin(h), 0f, MathF.Cos(h)),
        };

        AnimPose first = AnimationTimeline.SampleAt(0.037f, trans, rots);
        for (int i = 0; i < 1000; i++)
        {
            AnimPose again = AnimationTimeline.SampleAt(0.037f, trans, rots);
            Assert.Equal(first, again);
        }
    }

    [Fact]
    public void SampleAt_MismatchedArrayLengths_Throws()
    {
        var trans = new[] { new AnimTranslation(0f, 0f, 0f) };
        var rots = new[] { AnimRotation.Identity, AnimRotation.Identity };
        Assert.Throws<ArgumentException>(() => AnimationTimeline.SampleAt(0f, trans, rots));
    }
}