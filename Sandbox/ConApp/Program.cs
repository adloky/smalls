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

namespace ConApp {
    class Program {
        static string get(string url) {
            Console.WriteLine($"GET: {url}");
            var request = WebRequest.Create(url);
            using (var response = request.GetResponse())
            using (Stream dataStream = response.GetResponseStream()) {
                var reader = new StreamReader(dataStream);
                return reader.ReadToEnd();
            }
        }

        static void Main(string[] args) {
            // li.data-wid
            var dom = CQ.Create(File.ReadAllText("d:/freq.html"));
            var i = 0;
            var rs = new List<string>();
            foreach (var x in dom["li[data-wid]"]) {
                var ss = new List<string>();
                ss.Add(x.Cq().Find(".spelling").Text());
                ss.Add(x.Cq().Find(".part-of-speech").Text());
                ss.Add(x.Cq().Find(".translations").Text());
                ss.Add(x.Cq().Find(".word-rates").Attr("class").Split(' ').Where(y => Regex.IsMatch(y, "^[ABC][12]$")).FirstOrDefault() ?? "");
                //ss.Add(x.Cq().Find(".word-rates"));
                ss.Add(x.Cq().Find("a").Attr("href"));
                //x.Cq().Text
                rs.Add(string.Join(";", ss));
                //Console.WriteLine();
                //Console.WriteLine(x.OuterHTML);
                i++;
                //if (i == 50)
                //    break;
            }
            File.WriteAllLines("d:/freq.txt", rs);
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