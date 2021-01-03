using System.Collections;
using System.Collections.Generic;

namespace CelesteStudio.TtilebarButton.Utils
{
    internal static class CollectionHelper
    {
        public static void Synchronize<T>(IList<T> src, IList des)
        {
            var srcH = new HashSet<T>(src);

            // Remove Old
            for (var i = des.Count - 1; i >= 0; i--)
                if (!srcH.Remove((T)des[i]))
                    des.RemoveAt(i);

            // Add New
            foreach (var item in srcH)
                des.Add(item);
        }
    }
}
