using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.IO;

namespace MvcApp.Controllers {
    public class HomeController : Controller {
        static List<string[]> ls;

        public ActionResult Index() {
            ViewBag.Title = "Home Page";

            return View();
        }

        public ActionResult Text() {
            if (ls == null) {
                ls = System.IO.File.ReadAllLines(@"d:\Projects\smalls\lisen-pretty.txt").Select(x => x.Split('|')).ToList();
            }
            
            var rnd = new Random();
            var rs = Enumerable.Range(0, 10).Select(x => ls[rnd.Next(ls.Count)]).Select(x => new[] { $"/smalls/lisen/{x[0].Substring(0,2)}/{x[0]}.mp3", x[1] }).ToList();

            return View(rs);
        }
    }
}
