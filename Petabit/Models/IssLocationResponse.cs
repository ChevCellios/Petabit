namespace Petabit.Models;

public class IssLocationResponse
{
    public double Latitude { get; set; } = double.NaN;
    public double Longitude { get; set; } = double.NaN;
    public double Velocity { get; set; } = double.NaN;
    public long Timestamp { get; set; }
}
