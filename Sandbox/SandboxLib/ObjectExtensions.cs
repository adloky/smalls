using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sandbox {
    public static class ObjectExtensions {
        public static string JoinStrings(this IEnumerable<string> ss, string sep = "") {
            return string.Join(sep, ss);
        }

        public static void Print(this IEnumerable<object> os) {
            foreach (var o in os) {
                Console.WriteLine(o);
            }
        }
    }
}
