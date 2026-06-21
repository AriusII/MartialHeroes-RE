namespace MartialHeroes.Client.Domain.Simulation.Simulation;

/// <summary>
///     A single animation translation sample (local translation, no scale channel).
///     Neutral Domain value type so the timing logic does not depend on Assets.Parsers.
///     spec: Docs/RE/formats/animation.md §Keyframe record (translation X/Y/Z).
/// </summary>
/// <remarks>
///     Animation is a presentation concern; the rendered pose is non-deterministic in Godot. What we
///     model here is the deterministic <em>timing</em> (frame indexing and interpolation parameter) so
///     the same clip data sampled at the same time yields the same pose values. The clip DATA is
///     injected — the Domain never parses <c>.mot</c> files.
/// </remarks>
public readonly record struct AnimTranslation(float X, float Y, float Z)
{
    /// <summary>Component-wise linear interpolation. spec: animation.md §Translation interpolation.</summary>
    public static AnimTranslation Lerp(in AnimTranslation a, in AnimTranslation b, float alpha)
    {
        return new AnimTranslation(
            a.X + (b.X - a.X) * alpha,
            a.Y + (b.Y - a.Y) * alpha,
            a.Z + (b.Z - a.Z) * alpha);
    }
}

/// <summary>
///     A single animation rotation sample: a quaternion in (X, Y, Z, W) order with scalar W last.
///     Neutral Domain value type. spec: Docs/RE/formats/animation.md §Keyframe record
///     (rotation X/Y/Z/W, "scalar W last").
/// </summary>
public readonly record struct AnimRotation(float X, float Y, float Z, float W)
{
    /// <summary>The identity rotation (0, 0, 0, 1).</summary>
    public static AnimRotation Identity => new(0f, 0f, 0f, 1f);

    /// <summary>Dot product of two quaternions.</summary>
    public static float Dot(in AnimRotation a, in AnimRotation b)
    {
        return a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
    }

    /// <summary>Component-wise negation (the antipodal quaternion, same orientation).</summary>
    public AnimRotation Negated()
    {
        return new AnimRotation(-X, -Y, -Z, -W);
    }

    /// <summary>Returns this quaternion normalised to unit length, or identity if degenerate.</summary>
    public AnimRotation Normalized()
    {
        var lengthSquared = X * X + Y * Y + Z * Z + W * W;
        if (lengthSquared <= 0f) return Identity;

        var inv = 1.0f / MathF.Sqrt(lengthSquared);
        return new AnimRotation(X * inv, Y * inv, Z * inv, W * inv);
    }

    /// <summary>
    ///     Shortest-arc spherical linear interpolation between two quaternions. If the dot product is
    ///     negative the second quaternion is negated first so the interpolation takes the short arc
    ///     (spec: animation.md §Rotation interpolation). Near-parallel inputs fall back to normalized
    ///     linear interpolation to avoid a divide-by-near-zero (spec: animation.md "Degenerate cases").
    /// </summary>
    public static AnimRotation Slerp(in AnimRotation a, in AnimRotation b, float alpha)
    {
        var end = b;
        var dot = Dot(a, b);

        // Short-arc sign flip: if the quaternions are more than 90° apart, negate the target.
        // spec: animation.md §Rotation interpolation ("if dot < 0: negate before slerp").
        if (dot < 0f)
        {
            end = b.Negated();
            dot = -dot;
        }

        // Degenerate (nearly identical) case: fall back to normalized lerp.
        // spec: animation.md "nearly-identical quaternions ... fall back to normalized linear".
        const float parallelThreshold = 0.9995f;
        if (dot > parallelThreshold)
        {
            var lin = new AnimRotation(
                a.X + (end.X - a.X) * alpha,
                a.Y + (end.Y - a.Y) * alpha,
                a.Z + (end.Z - a.Z) * alpha,
                a.W + (end.W - a.W) * alpha);
            return lin.Normalized();
        }

        // dot is in [0, parallelThreshold]; acos is well-defined.
        var theta0 = MathF.Acos(dot);
        var theta = theta0 * alpha;
        var sinTheta0 = MathF.Sin(theta0);
        var sinTheta = MathF.Sin(theta);

        var s0 = MathF.Cos(theta) - dot * sinTheta / sinTheta0;
        var s1 = sinTheta / sinTheta0;

        return new AnimRotation(
            a.X * s0 + end.X * s1,
            a.Y * s0 + end.Y * s1,
            a.Z * s0 + end.Z * s1,
            a.W * s0 + end.W * s1);
    }
}

/// <summary>
///     One sampled pose of a single bone track: a translation plus a rotation.
/// </summary>
public readonly record struct AnimPose(AnimTranslation Translation, AnimRotation Rotation);

/// <summary>
///     Deterministic sampling of an injected animation clip's keyframe stream. The clip data
///     (translation + rotation per keyframe) is supplied by the caller; the Domain never parses
///     <c>.mot</c> files (spec: Docs/RE/formats/animation.md — parsing belongs to Assets.Parsers).
/// </summary>
/// <remarks>
///     <para>
///         Timing model (spec: Docs/RE/formats/animation.md §Timing):
///         <list type="bullet">
///             <item>Fixed 10 fps. Frame index = <c>floor(time × 10)</c>.</item>
///             <item>
///                 The next index is <c>frame + 1</c>, clamped to the last keyframe (clamp-to-last at clip
///                 end; looping is a higher-layer concern and is not modelled here).
///             </item>
///             <item>
///                 The interpolation parameter <c>alpha = time − frame / 10</c> is passed in <b>raw seconds</b>
///                 in the range [0, 0.1], exactly as the legacy client does — it is NOT renormalised to [0, 1].
///                 This is documented as an observed deviation, not standard practice (spec: animation.md §Timing,
///                 "It is not re-normalized to [0, 1]").
///             </item>
///         </list>
///     </para>
///     <para>
///         We choose <see cref="float" /> structs here (not <see cref="Shared.Kernel.Numerics.Vector3Fixed" />)
///         because animation is a presentation/timing concern and the rendered pose is already
///         non-deterministic in Godot; the determinism we guarantee is the frame-index and alpha selection,
///         which is integer/float arithmetic with no hidden state.
///     </para>
/// </remarks>
public static class AnimationTimeline
{
    /// <summary>
    ///     Fixed playback rate in frames per second. spec: Docs/RE/formats/animation.md §Timing
    ///     ("Fixed frame rate: 10 fps").
    /// </summary>
    public const float FramesPerSecond = 10.0f; // spec: Docs/RE/formats/animation.md §Timing

    /// <summary>
    ///     Seconds per frame at the fixed rate (0.1). spec: Docs/RE/formats/animation.md §Timing
    ///     ("Clip duration = frame_count × 0.1").
    /// </summary>
    public const float SecondsPerFrame = 1.0f / 10.0f; // spec: Docs/RE/formats/animation.md §Timing

    /// <summary>
    ///     Resolves the lower keyframe index and the raw-seconds interpolation parameter for a playback
    ///     time. The lower index is <c>floor(time × 10)</c> clamped into <c>[0, keyCount − 1]</c>; the
    ///     upper index is <c>lower + 1</c> clamped to the last keyframe. spec: animation.md §Timing.
    /// </summary>
    /// <param name="timeSeconds">Playback time in seconds (negative is clamped to 0).</param>
    /// <param name="keyCount">Number of keyframes in the track; must be &gt;= 1.</param>
    /// <returns>The lower/upper indices and the raw-seconds alpha in [0, 0.1].</returns>
    public static (int Lower, int Upper, float Alpha) ResolveFrame(float timeSeconds, int keyCount)
    {
        if (keyCount < 1)
            throw new ArgumentOutOfRangeException(nameof(keyCount), "A track needs at least one keyframe.");

        if (timeSeconds < 0f) timeSeconds = 0f;

        var sampleIndex = (int)MathF.Floor(timeSeconds * FramesPerSecond);

        // Clamp the lower index into range (clamp-to-last at clip end). spec: animation.md §Timing.
        if (sampleIndex >= keyCount)
            // Past the end: snap entirely to the last keyframe with no interpolation.
            return (keyCount - 1, keyCount - 1, 0f);

        var lower = sampleIndex;
        var upper = lower + 1 < keyCount ? lower + 1 : keyCount - 1;

        // Raw-seconds alpha, NOT normalised. spec: animation.md §Timing.
        var alpha = timeSeconds - lower * SecondsPerFrame;
        return (lower, upper, alpha);
    }

    /// <summary>
    ///     Samples a bone track at a playback time, interpolating translation linearly and rotation by
    ///     shortest-arc SLERP. The keyframe arrays are injected by the caller (translation and rotation
    ///     parallel arrays of equal length). spec: Docs/RE/formats/animation.md §Timing / §interpolation.
    /// </summary>
    /// <param name="timeSeconds">Playback time in seconds.</param>
    /// <param name="translations">Per-keyframe translations; same length as <paramref name="rotations" />.</param>
    /// <param name="rotations">Per-keyframe rotations; same length as <paramref name="translations" />.</param>
    /// <returns>The interpolated pose.</returns>
    public static AnimPose SampleAt(
        float timeSeconds,
        ReadOnlySpan<AnimTranslation> translations,
        ReadOnlySpan<AnimRotation> rotations)
    {
        if (translations.Length != rotations.Length)
            throw new ArgumentException("Translation and rotation key arrays must be the same length.");

        if (translations.Length == 0)
            throw new ArgumentException("A track needs at least one keyframe.", nameof(translations));

        var (lower, upper, alpha) = ResolveFrame(timeSeconds, translations.Length);

        if (lower == upper) return new AnimPose(translations[lower], rotations[lower]);

        var t = AnimTranslation.Lerp(translations[lower], translations[upper], alpha);
        var r = AnimRotation.Slerp(rotations[lower], rotations[upper], alpha);
        return new AnimPose(t, r);
    }
}