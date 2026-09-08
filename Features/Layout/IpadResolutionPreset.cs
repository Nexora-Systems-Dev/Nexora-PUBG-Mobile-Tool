namespace Nexora.Features.Layout;

public sealed record IpadResolutionPreset(
    string Label,
    int Width,
    int Height,
    string Guidance)
{
    public string DisplayName => $"{Label}  •  {Width} × {Height}";
    public string Details => $"{Width} × {Height}  •  {Guidance}";
}
