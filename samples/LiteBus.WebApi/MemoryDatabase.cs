using System.Collections.Generic;

namespace LiteBus.WebApi
{
    public static class MemoryDatabase
    {
        private static readonly List<string> Colors = new();

        public static void AddColor(string color)
        {
            Colors.Add(color);
        }

        public static IEnumerable<string> GetColors()
        {
            return Colors;
        }
    }
}