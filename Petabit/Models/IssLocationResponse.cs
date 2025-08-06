namespace Petabit.Models
{
    public class IssLocationResponse
    {
        public string Message { get; set; }
        public string Timestamp { get; set; }

        public IssPosition IssPosition { get; set; }
    }

    public class IssPosition
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }

    }
}
