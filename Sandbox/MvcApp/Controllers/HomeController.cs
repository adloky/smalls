using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.IO;
using System.Text.RegularExpressions;
using MvcApp.Models;

namespace MvcApp.Controllers {
    public class HomeController : Controller {
        static List<string[]> ls;

        [Route("snapshots/{*path}")]
        public ActionResult Snapshots(string path) {
            ViewBag.path = path;
            path = Path.Combine("e:/videos", path);
            var rnRe = new Regex(@"\r?\n", RegexOptions.Compiled);
            var tRe = new Regex(@" ?--> ?", RegexOptions.Compiled);
            var tnRe = new Regex(@"[:\.,]", RegexOptions.Compiled);

            Func<string, string[][]> sp = s => Regex.Split(s, @"\r?\n\r?\n").Select(s2 => rnRe.Split(s2).Skip(1).ToArray()).Where(ss => ss.Length >= 2).ToArray();

            var ens = sp(System.IO.File.ReadAllText($"{path}.eng.srt"));
            var rus = sp(System.IO.File.ReadAllText($"{path}.rus.srt"));
            var rs = new List<SnapshotsLine>();
            for (var i = 0; i < ens.Length; i++) {
                var en = ens[i];
                var ru = rus[i];
                var t = tRe.Split(en[0])[0] + ",0";
                t = string.Join("_", tnRe.Split(t).Select((x,j) => j <= 2 ? x.PadLeft(2, '0') : x.PadRight(2, '0').Substring(0, 2)).Take(4).ToArray());
                var r = new SnapshotsLine();
                r.Time = t;
                r.En = string.Join("<br/>", en.Skip(1));
                r.Ru = string.Join("<br/>", ru.Skip(1));
                rs.Add(r);
            }

            return View(rs);
        }
        
        public ActionResult Text() {
            if (ls == null) {
                ls = System.IO.File.ReadAllLines(@"d:\Projects\smalls\lisen.txt")
                    .Select(x => { var xs = x.Split(new[] { " | " }, StringSplitOptions.None); return new[] { xs[0], xs[2] }; }).ToList();
            }
            
            var rnd = new Random();
            var rs = Enumerable.Range(0, 10).Select(x => ls[rnd.Next(ls.Count)]).Select(x => new[] { $"/smalls/lisen/{x[0].Substring(0,2)}/{x[0]}.mp3", x[1] }).ToList();

            return View(rs);
        }
        
    }
}
