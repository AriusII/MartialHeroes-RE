namespace MartialHeroes.Assets.Parsers.Models;

/// <summary>
/// Neutral quaternion (four-component rotation).
/// Component order on disk is XYZW (unverified — see spec).
/// No engine or rendering dependency.
/// </summary>
public readonly record struct Quat(float X, float Y, float Z, float W);