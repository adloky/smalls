using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.IO;
using System.Text;

namespace MvcApp.Controllers
{
    public class MemenController : ApiController
    {
        
        [HttpGet]
        [Route("api/memen/lastbackup")]
        public HttpResponseMessage LastBackup() {
            return new HttpResponseMessage() {
                Content = new StringContent(File.ReadAllText("d:/DB/memen/last.txt"), Encoding.UTF8, "text/html")
            };
        }

        [HttpPost]
        [Route("api/memen/backup")]
        public void Backup([FromBody] string value) {
            foreach (var p in Directory.GetFiles("d:/DB/memen").Where(x => !x.Contains("last.txt")).OrderByDescending(x => x).Skip(10)) {
                File.Delete(p);
            }
            var ds = DateTime.Now.ToString("u").Replace("Z", "");
            File.WriteAllText("d:/DB/memen/last.txt", ds);
            var path = $"d:/DB/memen/{ds.Substring(0, 10)}.txt";
            File.WriteAllText(path, value);
        }
    }
}
