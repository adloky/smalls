using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.IO;

namespace MvcApp.Controllers {
    public class HomeController : Controller {
        public ActionResult Index() {
            ViewBag.Title = "Home Page";

            return View();
        }

        public ActionResult Text() {
            var ss = System.IO.File.ReadAllLines(@"d:\Projects\smalls\bins\con-ru.txt");
            var rnd = new Random();
            var rs = Enumerable.Range(0, 10).Select(x => ss[rnd.Next(ss.Length)]).Select(x => x.Split('|')).ToList();

            return View(rs.Select(x => x[0]).Union(rs.Select(x => x[1])));
        }
    }
}
