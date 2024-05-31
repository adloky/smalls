using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CsQuery;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace ConApp {
    class Program {
        static string handleString(string s, Regex re, Func<string, Match, string> handler) {
            var m = re.Match(s);
            var i = 0;
            var sb = new StringBuilder();

            while (m.Success) {
                var ls = s.Substring(i, m.Index - i);
                sb.Append(ls);
                ls = s.Substring(m.Index, m.Length);
                ls = handler(ls, m);
                sb.Append(ls);
                i = m.Index + m.Length;
                m = m.NextMatch();
            }
            var ls2 = s.Substring(i, s.Length - i);
            sb.Append(ls2);

            return sb.ToString();
        }

        static string get(string url) {
            Console.WriteLine($"GET: {url}");
            var request = WebRequest.Create(url);
            using (var response = request.GetResponse())
            using (Stream dataStream = response.GetResponseStream()) {
                var reader = new StreamReader(dataStream);
                return reader.ReadToEnd();
            }
        }

        private static ChromeDriver chromeDriver;

        private static int navI = 0;

        static string gtranslate(string s) {
            if (chromeDriver == null)
                chromeDriver = new ChromeDriver();

            chromeDriver.Navigate().GoToUrl("https://translate.google.com/details?sl=en&tl=ru&text=" + s + "&op=translate");
            Thread.Sleep(navI % 20 == 0 ? 20000 : 5000);
            navI++;
            var html = chromeDriver.FindElement(By.CssSelector("[jsname=kepomc]")).GetAttribute("innerHTML");

            var dom = CQ.Create(html);
            var h3 = dom.Find("h3").Where(x => x.Cq().Text().Contains("варианты перевода")).FirstOrDefault();
            if (h3 == null) {
                s = $"{s} NotFound";
                Console.WriteLine(s);
                return s;
            }

            var body = h3.ParentNode;
            var rs = new List<string>();
            foreach (var part in body.Cq().Find("tbody")) {
                var partName = (string)null;
                var rows = part.Cq().Find("tr");
                var ts = new List<string>();
                for (var i = 0; i < rows.Length; i++) {
                    var hs = rows[i].Cq().Find("th").Select(x => x.Cq().Text().Trim()).ToArray();
                    var freq = rows[i].Cq().Find(".EiZ8Dd").Count();
                    if (i == 0) {
                        partName = hs[0];
                        ts.Add($"{hs[1]} [{freq}]");
                    }
                    else {
                        ts.Add($"{hs[0]} [{freq}]");
                    }
                }
                rs.Add($" {{{partName}}} {string.Join(", ", ts)}");
            }
            s = $"{s}{string.Join("", rs)}";
            Console.WriteLine(s);
            return s;
        }

        private static volatile bool ctrlC = false;

        [STAThreadAttribute]
        static void Main(string[] args) {
            Console.CancelKeyPress += (o, e) => { ctrlC = true; e.Cancel = true; };
            /*
            var path = "d:/freq-us-ex.txt";
            var path2 = path.Replace(".txt", "-2.txt");
            var path3 = "d:/freq-us-2.txt";
            var rs = File.ReadAllLines(path3);
            var dic = File.ReadAllLines(path2).Distinct().ToDictionary(x => x.Split(' ')[0]);
            //var ss = File.ReadAllLines(path).Where(x => x != "").Skip(rs.Count).ToArray();
            for (var i = 0; i < rs.Length; i++) {
                var s = rs[i];
                var k = s.Split(' ')[0];
                if (!s.Contains("NotFound") || !dic.ContainsKey(k))
                    continue;

                rs[i] = dic[k];
            }
            File.WriteAllLines(path3, rs);
            */
            
            var path = "d:/freq-us-ex.txt";
            var path2 = path.Replace(".txt", "-2.txt");
            var rs = File.ReadAllLines(path2).ToList();
            var ss = File.ReadAllLines(path).Where(x => x != "").Skip(rs.Count).ToArray();
            
            //var re = new Regex(@"\*\*|\|");
            foreach (var s in ss) {
                if (ctrlC)
                    break;
                
                var t = s + " NotFound";
                var i = 0;
                do {
                    try {
                        t = gtranslate(s);
                    }
                    catch {
                        break;
                    }
                    i++;
                }
                while (t.Contains("NotFound") && i <= 5);
                rs.Add(t);
            }
            chromeDriver.Dispose();
            File.WriteAllLines(path2, rs.Select(x => x));
            
            Console.WriteLine("Press ENTER");
            Console.ReadLine();
        }
    }
}

/*
            var path = "d:/All_Dorothys_adventures_in_Oz.htm";
            var html = File.ReadAllText(path);
            path = path.Replace(".htm", "-2.htm");
            var dom = CQ.Create(html);
            foreach (var p in dom.Select(".Heading2")) {
                p.OuterHTML = $"<h1>{p.Cq().Text()}</h1>";
            }
            foreach (var p in dom.Select(".Heading3")) {
                p.OuterHTML = $"<h2>{p.Cq().Text()}</h2>";
            }
            foreach (var p in dom.Select(".MsoNormal")) {
                if (!p.OuterHTML.Contains("font-size:14.0pt"))
                    continue;
                p.OuterHTML = $"<h2>{p.Cq().Text()}</h2>";
            }
            File.WriteAllText(path, dom.Html());

 */