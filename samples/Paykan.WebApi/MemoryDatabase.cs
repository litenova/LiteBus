using System.Collections.Generic;

namespace Paykan.WebApi
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