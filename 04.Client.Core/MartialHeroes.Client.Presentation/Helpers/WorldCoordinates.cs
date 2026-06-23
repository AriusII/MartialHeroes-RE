namespace MartialHeroes.Client.Presentation.Helpers;

public static class WorldCoordinates
{
    public static (float X, float Y, float Z) ToGodot(float legacyX, float legacyY, float legacyZ)
    {
        return (legacyX, legacyY, -legacyZ);
    }

    public static (float X, float Y, float Z) ToLegacy(float godotX, float godotY, float godotZ)
    {
        return (godotX, godotY, -godotZ);
    }


    public static (float X, float Y, float Z) SkinToGodot(float x, float y, float z)
    {
        return (x, y, -z);
    }

    public static (float X, float Y, float Z, float W) SkinQuatToGodot(
        float x, float y, float z, float w)
    {
        return (-x, -y, z, w);
    }
}