using System.Collections.Generic;

namespace LiteBus.WebApi
{
    public static class MemoryDatabase
    {
        private static readonly List<decimal> Numbers = new();

        public static void AddNumber(decimal number)
        {
            Numbers.Add(number);
        }

        public static IEnumerable<decimal> GetNumbers()
        {
            return Numbers;
        }
    }
}