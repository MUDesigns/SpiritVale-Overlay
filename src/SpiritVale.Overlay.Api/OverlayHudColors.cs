namespace SpiritVale.Overlay.Api;

/// <summary>
/// Shared Plugin Manager palette (matches spiritvale-mod-manager navy / gold).
/// RGBA floats in 0–1 range for <see cref="IOverlayUi.TextColored"/> / <see cref="IOverlayUi.PushStyleColor"/>.
/// </summary>
public static class OverlayHudColors
{
    public static readonly (float R, float G, float B, float A) Bg0 = (18 / 255f, 21 / 255f, 31 / 255f, 1f);
    public static readonly (float R, float G, float B, float A) Bg1 = (26 / 255f, 31 / 255f, 44 / 255f, 1f);
    public static readonly (float R, float G, float B, float A) Bg2 = (35 / 255f, 41 / 255f, 56 / 255f, 1f);
    public static readonly (float R, float G, float B, float A) Bg3 = (44 / 255f, 51 / 255f, 69 / 255f, 1f);
    public static readonly (float R, float G, float B, float A) Text = (244 / 255f, 247 / 255f, 251 / 255f, 1f);
    public static readonly (float R, float G, float B, float A) Muted = (154 / 255f, 163 / 255f, 184 / 255f, 1f);
    public static readonly (float R, float G, float B, float A) Gold = (240 / 255f, 193 / 255f, 74 / 255f, 1f);
    public static readonly (float R, float G, float B, float A) Orange = (212 / 255f, 137 / 255f, 58 / 255f, 1f);
    public static readonly (float R, float G, float B, float A) Blue = (85 / 255f, 183 / 255f, 234 / 255f, 1f);
    public static readonly (float R, float G, float B, float A) Ok = (110 / 255f, 214 / 255f, 160 / 255f, 1f);
    public static readonly (float R, float G, float B, float A) Danger = (224 / 255f, 122 / 255f, 109 / 255f, 1f);
    public static readonly (float R, float G, float B, float A) ButtonBg = (44 / 255f, 51 / 255f, 69 / 255f, 1f);
    public static readonly (float R, float G, float B, float A) ButtonHi = (55 / 255f, 62 / 255f, 82 / 255f, 1f);
}
