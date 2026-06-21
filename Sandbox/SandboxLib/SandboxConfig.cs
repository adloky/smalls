using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Sandbox {
    public class SandboxConfig {
        static Dictionary<string, string> _default;

        static Regex commRe = new Regex(@"^ *//.*| +// .*", RegexOptions.Compiled);

        public static void Reread() {
            var kvs = File.ReadAllLines(@"d:/.sandbox").ToList();
            var curPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".sandbox");
            if (File.Exists(curPath)) {
                kvs.AddRange(File.ReadAllLines(curPath));
            }
            kvs = kvs.Select(x => commRe.Replace(x, "")).Where(x => x.Contains(":")).ToList();

            _default = kvs.Select(x => {
                var i = x.IndexOf(":");
                return new KeyValuePair<string, string>(x.Substring(0, i).Trim(), x.Substring(i + 1).Trim());
            })
            .GroupBy(x => x.Key).Select(g => g.Last())
            .ToDictionary(x => x.Key, x => x.Value);
        }

        public static Dictionary<string, string> Default {
            get {
                if (_default == null) Reread();
                return _default;
            }
        }
    }
}
