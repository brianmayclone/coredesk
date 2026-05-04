namespace CoreDesk.LiquidGlass;

public sealed record LiquidGlassOptions
{
    public static LiquidGlassOptions Default { get; } = new();

    public double SurfaceWidth { get; init; } = 760;

    public double SurfaceHeight { get; init; } = 260;

    public int WindowWidth { get; init; } = 980;

    public int WindowHeight { get; init; } = 460;

    public float BlurAmount { get; init; } = 34f;

    public float Saturation { get; init; } = 1.55f;

    public float Contrast { get; init; } = 1.18f;

    public float Brightness { get; init; } = 0.08f;

    public float TintOpacity { get; init; } = 0.20f;

    public System.Numerics.Vector4 TintColor { get; init; } = new(0.96f, 0.98f, 1f, 1f);

    public float EdgeLightOpacity { get; init; } = 0.42f;

    public float CornerRadius { get; init; } = 44f;

    public float RefractionStrength { get; init; } = 0.018f;

    public float InnerShadowOpacity { get; init; } = 0.34f;

    public float OuterShadowOpacity { get; init; } = 0.24f;

    public float ShadowBlurRadius { get; init; } = 34f;

    public float ShadowOffsetY { get; init; } = 12f;

    public float SpecularOpacity { get; init; } = 0.30f;
}
