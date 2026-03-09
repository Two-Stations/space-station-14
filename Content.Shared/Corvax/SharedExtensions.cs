using System.Collections.Generic;
using Robust.Shared.GameObjects;

namespace Content.Shared.Corvax
{
    public static class SharedExtensions
    {
        public static List<EntityUid> GetOrNew(this IDictionary<string, List<EntityUid>> dictionary, string key)
        {
            if (!dictionary.TryGetValue(key, out var value))
            {
                value = new List<EntityUid>();
                dictionary.Add(key, value);
            }

            return value;
        }
    }
}
