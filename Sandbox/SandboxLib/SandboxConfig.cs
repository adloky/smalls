using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sandbox {
    public class SandboxConfig {
        static Dictionary<string, string> _default;

        static Dictionary<string, string> getDefault() {
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".sandbox");
            configPath = File.Exists(configPath) ? configPath : @"d:/.sandbox";
            return File.ReadAllLines(configPath).Where(x => !x.StartsWith("//")).Select(x => {
                var i = x.IndexOf(":");
                return new KeyValuePair<string, string>(x.Substring(0, i), x.Substring(i + 1).Trim());
            }).ToDictionary(x => x.Key, x => x.Value);
        }

        public static Dictionary<string, string> Default {
            get => _default ?? (_default = getDefault());
        }
    }
}
