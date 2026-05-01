namespace CoreDesk.Abstractions.Models;

public sealed record DisplayMetrics(
    int PixelWidth,
    int PixelHeight,
    double DpiX,
    double DpiY,
    double? PhysicalWidthCentimeters,
    double? PhysicalHeightCentimeters)
{
    public double DiagonalInches
    {
        get
        {
            if (PhysicalWidthCentimeters is null || PhysicalHeightCentimeters is null)
            {
                return 0;
            }

            var widthInches = PhysicalWidthCentimeters.Value / 2.54;
            var heightInches = PhysicalHeightCentimeters.Value / 2.54;
            return Math.Sqrt((widthInches * widthInches) + (heightInches * heightInches));
        }
    }
}
