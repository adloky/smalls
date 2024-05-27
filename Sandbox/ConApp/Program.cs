using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
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
            var books = File.ReadAllLines("d:/adapt.txt").Select(s => {
                var ss = s.Split(';');
                return (num: int.Parse(ss[0]), name: ss[1]);
            }).ToArray();

            foreach (var b in books) {
                var path = $"d:/_a/{b.name}.html";
                if (File.Exists(path))
                    continue;

                var max = 1;
                var r = "";
                for (var p = 1; p <= max; p++) {
                    var html = get($"https://madbook.org/view?book={b.num}&page={p}");
                    Thread.Sleep(1000);
                    html = html.Replace("</span>", " </span>");
                    var dom = CQ.Create(html);
                    if (p == 1) {
                        max = int.Parse(dom.Select(".sheet-num-inside").Text().Trim().Split(' ').Last());
                        Console.WriteLine($"MAX: {max}");
                    }
                    var body = string.Join("", dom.Select(".paragraph").Select(x => $"<p class=\"paragraph\">{x.Cq().Text()}</p>"));

                    r += $"{body}<!-- {p} --><hr/>\r\n";
                }

                File.WriteAllText(path, r);
            }
        }
    }
}
