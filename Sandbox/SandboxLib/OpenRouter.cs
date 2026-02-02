using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Sandbox {
    public class OpenRouter {
        public static string Get(string s, byte[] img = null) {
            s = JsonConvert.ToString(s);
            var url = $"https://openrouter.ai/api/v1/chat/completions";
            var request = WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            s = $"{{\"model\":\"tngtech/deepseek-r1t2-chimera:free\",\"messages\":[{{\"role\":\"user\",\"content\":{s}}}]}}";
            var data = Encoding.UTF8.GetBytes(s);
            request.ContentLength = data.Length;
            request.ContentType = "application/json";
            request.Headers.Add("Authorization", $"Bearer {SandboxConfig.Default["openrouterKey"]}");
            using (var stream = request.GetRequestStream()) {
                stream.Write(data, 0, data.Length);
            }
            var r = (string)null;

            using (var response = request.GetResponse())
            using (Stream dataStream = response.GetResponseStream()) {
                var reader = new StreamReader(dataStream);
                r = reader.ReadToEnd();
            }

            var obj = JObject.Parse(r);

            return string.Join("\r\n", obj.SelectTokens("$.choices[*].message.content").Select(x => x.Value<string>()));
        }

    }
}
