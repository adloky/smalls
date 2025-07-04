﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Sandbox;

namespace MvcApp.Controllers
{
    public class ComicsController : ApiController
    {
        [HttpPost]
        [Route("api/comics/rename")]
        public void Rename() {
            foreach (var lang in new[] { "en", "ru" }) {
                foreach (var tail in new[] { "-temp", "" }) {
                    var ps = Directory.GetFiles($"d:/.temp/comics/{lang}", "*.jpg").OrderBy(x => x).ToArray();
                    for (var i = 0; i < ps.Length; i++) {
                        var dir = Path.GetDirectoryName(ps[i]);
                        var ext = Path.GetExtension(ps[i]);
                        File.Move(ps[i], $"{dir}\\{(i + 1).ToString("000")}{tail}-{lang}{ext}");
                    }
                }
            }
        }

        [HttpPost]
        [Route("api/comics/del")]
        public void Del(string name) {
            File.Delete($"d:/.temp/comics/{name.Split('-')[1]}/{name}.jpg");
        }

        [HttpPost]
        [Route("api/comics/inc")]
        public void Inc(string name) {
            var ps = Directory.GetFiles($"d:/.temp/comics/{name.Split('-')[1]}", "*.jpg").Where(x => Path.GetFileNameWithoutExtension(x).CompareTo(name) >= 0).OrderByDescending(x => x).ToArray();
            foreach (var p in ps) {
                var num = Regex.Match(Path.GetFileNameWithoutExtension(p), @"\d\d\d").Value;
                var newNum = (int.Parse(num) + 1).ToString("000");
                File.Move(p, p.Replace(num, newNum));
            }
        }

        [HttpPost]
        [Route("api/comics/save")]
        public void Save(string path, [FromBody] string value) {
            path = $"d:/Projects/smalls/data/{path}";
            File.WriteAllText(path, value);
        }

        [HttpPost]
        [Route("api/comics/gemini")]
        public HttpResponseMessage Gemini([FromBody] string value) {
            var s = Sandbox.Gemini.Get(value).Replace("*", "");
            s = string.Join("\r\n", Regex.Split(s, @"\r?\n").Where(x => x.Trim() != "").Select(x => $"<p>{x}</p>"));
            return new HttpResponseMessage() {
                Content = new StringContent(s, Encoding.UTF8, "text/html")
            };
        }
    }
}
