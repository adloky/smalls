using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Sandbox {
    public static class ObjectExtensions {
        public static string JoinStrings(this IEnumerable<string> ss, string sep = "") {
            return string.Join(sep, ss);
        }

        public static void Print<T>(this IEnumerable<T> os) {
            foreach (var o in os) {
                Console.WriteLine(o);
            }
        }

        public static IEnumerable<string> ToStringS<T>(this IEnumerable<T> os) {
            return os.Select(o => o.ToString());
        }

        private static Queue<(string s, Regex re)> splitRes = new Queue<(string s, Regex re)>();

        public static (string, string, string, string, string) SplitT5(this string s, string spRe) {
            while (splitRes.Count > 5) splitRes.Dequeue();
            var re = splitRes.Where(x => x.s == spRe).Select(x => x.re).FirstOrDefault();
            if (re == null) {
                re = new Regex(spRe, RegexOptions.Compiled);
                splitRes.Enqueue((spRe, re));
            }

            var r = re.Split(s);
            switch (r.Length) {
                case 0:  return (null, null, null, null, null);
                case 1:  return (r[0], null, null, null, null);
                case 2:  return (r[0], r[1], null, null, null);
                case 3:  return (r[0], r[1], r[2], null, null);
                case 4:  return (r[0], r[1], r[2], r[3], null);
                default: return (r[0], r[1], r[2], r[3], r[4]);
            }
        }

        public static (string, string) SplitT2(this string s, string spRe) {
            var (x1, x2, _, _,_) = s.SplitT5(spRe);
            return (x1, x2);
        }
    }
}
