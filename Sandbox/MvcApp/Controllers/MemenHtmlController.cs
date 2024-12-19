using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.IO;
using System.Text.RegularExpressions;

namespace MvcApp.Controllers
{
    public class MemenHtmlController : Controller
    {
        static StringSplitOptions ssop = StringSplitOptions.RemoveEmptyEntries;

        // GET: MemenHtml
        public ActionResult Backup() {
            var keyRe = new Regex(@"^(src|type|val)", RegexOptions.Compiled);
            var path = Directory.GetFiles(@"d:\DB\memen", "20*.txt").OrderByDescending(x => x).First();
            var s = Regex.Replace(System.IO.File.ReadAllText(path), @"^\[\{""?|""?\}\]$", "").Replace(@"\""", "'");
            var ss = Regex.Split(s, @"""?},{""?").ToList();
            ss = ss.Select(x => {
                var xs = Regex.Split(x, @"""?,""").Where(_x => keyRe.IsMatch(_x)).OrderBy(_x => _x).Select(_x => _x.Split(new[] { @""":""" }, ssop)[1]).ToArray();
                return $"{xs[0]} {{{xs[1]}}} {xs[2]}";
            }).OrderBy(x => x).ToList();
            ss.Insert(0, ss.Count.ToString());
            return View(ss);
        }

    }
}
