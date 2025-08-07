namespace Petabit.Models
{
    public class AstronautsResponse
    {
        public string Message { get; set; }
        public int? Number { get; set; }
        public List<Astronaut> People { get; set; }
    }

    public class Astronaut
    {
        public string? Name { get; set; }
        public string? Craft { get; set; }
    }
}

