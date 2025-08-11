using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Sandbox;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MvcApp.Controllers
{
    public class AiController : ApiController
    {

        [HttpPost]
        [Route("api/proxy-ds")]
        public IHttpActionResult ProxyDs([FromBody] string s) {
            s = JsonConvert.ToString(s);
            var url = $"https://api.proxyapi.ru/deepseek/chat/completions";
            var request = WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            s = $"{{\"model\":\"deepseek-chat\",\"messages\":[{{\"role\":\"user\",\"content\":{s}}}]}}";
            var data = Encoding.UTF8.GetBytes(s);
            request.ContentLength = data.Length;
            request.ContentType = "application/json";
            request.Headers.Add("Authorization", $"Bearer {SandboxConfig.Default["proxyapiKey"]}");
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

            var rs = obj.SelectTokens("$.choices[*].message.content").Select(x => x.Value<string>()).ToArray();

            return Ok(rs);
        }
    }
}
