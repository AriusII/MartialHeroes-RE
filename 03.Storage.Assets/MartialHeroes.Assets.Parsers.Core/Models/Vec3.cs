namespace MartialHeroes.Assets.Parsers.Core.Models;

/// <summary>
///     Neutral three-component float vector.
///     No engine or rendering dependency.
/// </summary>
public readonly record struct Vec3(float X, float Y, float Z);