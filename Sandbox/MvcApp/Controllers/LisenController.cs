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
    public class LisenController : ApiController
    {
        static string[] vals = File.ReadAllLines(@"d:\Projects\smalls\lisen.txt");
        static Random rnd = new Random();

        [HttpPost]
        [Route("api/lisen/next")]
        public HttpResponseMessage LastBackup() {
            var val = vals[rnd.Next(vals.Length)];
            return new HttpResponseMessage() {
                Content = new StringContent(val, Encoding.UTF8, "text/html")
            };
        }
    }
}
