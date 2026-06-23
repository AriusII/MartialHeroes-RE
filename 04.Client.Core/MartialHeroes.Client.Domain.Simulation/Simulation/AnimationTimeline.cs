namespace MartialHeroes.Client.Domain.Simulation.Simulation;

public readonly record struct AnimTranslation(float X, float Y, float Z)
{
    public static AnimTranslation Lerp(in AnimTranslation a, in AnimTranslation b, float alpha)
    {
        return new AnimTranslation(
            a.X + (b.X - a.X) * alpha,
            a.Y + (b.Y - a.Y) * alpha,
            a.Z + (b.Z - a.Z) * alpha);
    }
}

public readonly record struct AnimRotation(float X, float Y, float Z, float W)
{
    public static AnimRotation Identity => new(0f, 0f, 0f, 1f);

    public static float Dot(in AnimRotation a, in AnimRotation b)
    {
        return a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
    }

    public AnimRotation Negated()
    {
        return new AnimRotation(-X, -Y, -Z, -W);
    }

    public AnimRotation Normalized()
    {
        var lengthSquared = X * X + Y * Y + Z * Z + W * W;
        if (lengthSquared <= 0f) return Identity;

        var inv = 1.0f / MathF.Sqrt(lengthSquared);
        return new AnimRotation(X * inv, Y * inv, Z * inv, W * inv);
    }

    public static AnimRotation Slerp(in AnimRotation a, in AnimRotation b, float alpha)
    {
        var end = b;
        var dot = Dot(a, b);

        if (dot < 0f)
        {
            end = b.Negated();
            dot = -dot;
        }

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

public readonly record struct AnimPose(AnimTranslation Translation, AnimRotation Rotation);

public static class AnimationTimeline
{
    public const float FramesPerSecond = 10.0f;

    public const float SecondsPerFrame = 1.0f / 10.0f;

    public static (int Lower, int Upper, float Alpha) ResolveFrame(float timeSeconds, int keyCount)
    {
        if (keyCount < 1)
            throw new ArgumentOutOfRangeException(nameof(keyCount), "A track needs at least one keyframe.");

        if (timeSeconds < 0f) timeSeconds = 0f;

        var sampleIndex = (int)MathF.Floor(timeSeconds * FramesPerSecond);

        if (sampleIndex >= keyCount)
            return (keyCount - 1, keyCount - 1, 0f);

        var lower = sampleIndex;
        var upper = lower + 1 < keyCount ? lower + 1 : keyCount - 1;

        var alpha = timeSeconds - lower * SecondsPerFrame;
        return (lower, upper, alpha);
    }

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