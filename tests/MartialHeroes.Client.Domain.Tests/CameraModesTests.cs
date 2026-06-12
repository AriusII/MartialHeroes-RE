using MartialHeroes.Client.Domain.Simulation;
using MartialHeroes.Shared.Kernel.Numerics;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

public sealed class CameraModesTests
{
    [Fact]
    public void OrbitsPlayer_TrueForThirdFirstGamble()
    {
        Assert.True(CameraModes.OrbitsPlayer(CameraMode.Third));
        Assert.True(CameraModes.OrbitsPlayer(CameraMode.First));
        Assert.True(CameraModes.OrbitsPlayer(CameraMode.Gamble));
        Assert.False(CameraModes.OrbitsPlayer(CameraMode.Static));
        Assert.False(CameraModes.OrbitsPlayer(CameraMode.Event));
    }

    [Fact]
    public void RunsTerrainCollision_OnlyThird()
    {
        Assert.True(CameraModes.RunsTerrainCollision(CameraMode.Third));
        Assert.False(CameraModes.RunsTerrainCollision(CameraMode.First));
        Assert.False(CameraModes.RunsTerrainCollision(CameraMode.Static));
    }

    [Fact]
    public void Compute_FirstPerson_EyeAtFocus()
    {
        var focus = Vector3Fixed.FromWholeUnits(5, 2, 8);
        CameraTransform t = CameraModes.Compute(focus, CameraMode.First, CameraParameters.ThirdDefault);

        Assert.Equal(focus, t.Eye); // first-person eye sits at the focus.
    }

    [Fact]
    public void Compute_Third_YawZero_PitchZero_PlacesEyeBehindOnZ()
    {
        // yaw 0, pitch 0: horizontal = distance, offsetX = -d*sin(0) = 0, offsetZ = -d*cos(0) = -d.
        var focus = Vector3Fixed.Zero;
        var p = new CameraParameters { FollowDistance = 10f, Yaw = 0f, Pitch = 0f };

        CameraTransform t = CameraModes.Compute(focus, CameraMode.Third, p);
        var (x, y, z) = t.Eye.ToVector3Float();

        Assert.Equal(0f, x, 3);
        Assert.Equal(0f, y, 3);
        Assert.Equal(-10f, z, 2); // eye trails behind on -Z.
    }

    [Fact]
    public void ClampEyeAboveTerrain_RaisesEye_WhenBelowClamp()
    {
        var eye = Vector3Fixed.FromFloat(0f, 1f, 0f);
        // terrain 5 + offset 3.8 = 8.8 minimum.
        Vector3Fixed clamped = CameraModes.ClampEyeAboveTerrain(eye, terrainHeight: 5f);
        var (_, y, _) = clamped.ToVector3Float();

        Assert.Equal(8.8f, y, 2);
    }

    [Fact]
    public void ClampEyeAboveTerrain_LeavesEye_WhenAboveClamp()
    {
        var eye = Vector3Fixed.FromFloat(0f, 100f, 0f);
        Vector3Fixed clamped = CameraModes.ClampEyeAboveTerrain(eye, terrainHeight: 5f);
        var (_, y, _) = clamped.ToVector3Float();

        Assert.Equal(100f, y, 2);
    }

    [Fact]
    public void ClampSymmetric_BoundsValue()
    {
        Assert.Equal(0.1f, CameraModes.ClampSymmetric(0.5f, 0.1f), 4);
        Assert.Equal(-0.1f, CameraModes.ClampSymmetric(-0.5f, 0.1f), 4);
        Assert.Equal(0.05f, CameraModes.ClampSymmetric(0.05f, 0.1f), 4);
    }

    [Fact]
    public void IntegrateRate_AppliesFriction_WhenNoInput()
    {
        float r = CameraModes.IntegrateRate(
            rate: 1.0f, inputStep: 0f,
            gain: CameraModes.RecoveredDefaults.KeyboardGain,
            friction: CameraModes.RecoveredDefaults.NoInputFriction,
            deadZone: CameraModes.RecoveredDefaults.RateDeadZone,
            clamp: CameraModes.RecoveredDefaults.PitchClamp);

        Assert.Equal(0.6f, r, 4); // 1.0 * 0.6 friction.
    }

    [Fact]
    public void IntegrateRate_AddsGain_OnInput_AndClamps()
    {
        float r = CameraModes.IntegrateRate(
            rate: 0.09f, inputStep: 1f,
            gain: CameraModes.RecoveredDefaults.KeyboardGain,
            friction: CameraModes.RecoveredDefaults.NoInputFriction,
            deadZone: CameraModes.RecoveredDefaults.RateDeadZone,
            clamp: CameraModes.RecoveredDefaults.YawRateClamp);

        // 0.09 + 0.3 = 0.39, clamped to yaw-rate 0.1.
        Assert.Equal(0.1f, r, 4);
    }

    [Fact]
    public void IntegrateRate_SnapsBelowDeadZone_ToZero()
    {
        float r = CameraModes.IntegrateRate(
            rate: 0.0005f, inputStep: 0f,
            gain: 0.3f, friction: 0.6f,
            deadZone: CameraModes.RecoveredDefaults.RateDeadZone,
            clamp: 4f);

        Assert.Equal(0f, r);
    }
}