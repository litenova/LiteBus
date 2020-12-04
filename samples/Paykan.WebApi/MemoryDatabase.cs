using System.Collections.Generic;

namespace Paykan.WebApi
{
    public static class MemoryDatabase
    {
        private static List<string> Colors = new List<string>();

        public static void AddColor(string color) => Colors.Add(color);

        public static IEnumerable<string> GetColors() => Colors;
    }
}