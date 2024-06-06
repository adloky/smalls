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
using Porter2StemmerStandard;

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

        private static Regex gValRe = new Regex(@"^(.*?) \[([1-3])\]$");

        private static (string val, int freq)[] getGVals(string s, string part) {
            var op = StringSplitOptions.None;
            var valStr = s.Split(new[] { " {" }, op).Skip(1).Select(x => {
                var ss = x.Split(new[] { "} " }, op);
                return (part: ss[0], vals: ss[1]);
            }).Where(x => x.part == part).Select(x => x.vals).FirstOrDefault();
            if (valStr == null)
                return new (string val, int freq)[] { };

            return valStr.Split(new[] { "; " }, op).Select(x => {
                var m = gValRe.Match(x);
                return (val: m.Groups[1].Value, freq: int.Parse(m.Groups[2].Value));
            }).ToArray();
        }

        private static string pathEx(string path, string ex) {
            var ext = Path.GetExtension(path);
            return $"{path.Substring(0, path.Length - ext.Length)}{ex}{ext}";
        }

        private static void freqAddGVals(string path, string gPath) {
            var ss = File.ReadAllLines(path);
            var rs = new List<string>();
            var dic = File.ReadAllLines(gPath).ToDictionary(x => x.Split('{')[0].Trim());

            foreach (var s in ss) {
                if (!s.Contains("**")) {
                    rs.Add(s);
                    continue;
                }

                var sp = s.Split(new[] { " **", "** " }, StringSplitOptions.None);
                var num = sp[0];
                var key = sp[1];
                var part = sp[2].Replace("{", "").Split('}')[0];
                var vals = getGVals(dic[key], part);
                var min = vals.Length == 0 ? 10 : vals.Select(x => x.freq).Max() - 1;
                vals = vals.Where(x => x.freq >= min).ToArray();
                if (vals.Length == 0) {
                    rs.Add(s);
                    continue;
                }

                var s2 = $"{num} **{key}** {{{part}}} {string.Join("; ", vals.Select(x => $"{x.val} [{x.freq}]"))}";
                rs.Add(s2);
            }
            
            File.WriteAllLines(pathEx(path, "-2"), rs);
        }

        private static StringSplitOptions ssop = StringSplitOptions.None;

        #region stemmer

        private static Dictionary<string, string> _lemmas;

        private static Dictionary<string, string> lemmas {
            get {
                if (_lemmas != null)
                    return _lemmas;

                _lemmas = new Dictionary<string, string>();
                File.ReadAllLines("d:/Projects/smalls/e_lemma.txt")
                    .Where(x => !x.StartsWith("["))
                    .Select(x => x.ToLower().Split(new[] { " -> ", "," }, ssop)).ToList()
                    .ForEach(x => {
                        foreach (var xx in x.Skip(1)) {
                            _lemmas[xx] = x[0];
                        }
                    });

                return _lemmas;
            }
        }

        private static HashSet<string> _existingWords;

        private static HashSet<string> existingWords {
            get {
                if (_existingWords != null)
                    return _existingWords;

                var dic = File.ReadAllLines("d:/Projects/smalls/en-dic.txt")
                    .Select(x => x.Split(new[] { " - " }, ssop)[0].ToLower())
                    .Where(x => x.Length >= 3);

                _existingWords = new HashSet<string>(dic);
                return _existingWords;
            }
        }

        private static Regex prefixRe = new Regex("^(anti|auto|de|dis|down|extra|hyper|il|im|inter|in|ir|mega|mid|mis|non|over|out|post|pre|pro|re|semi|sub|super|tele|trans|ultra|un|up)", RegexOptions.Compiled);

        private static string stem(string s) {
            s = s.ToLower();
            if (lemmas.TryGetValue(s, out var _s)) {
                s = _s;
            }

            while (true) {
                var m = prefixRe.Match(s);
                if (!m.Success)
                    break;

                _s = s.Substring(m.Value.Length);
                if (!existingWords.Contains(_s))
                    break;

                s = _s;
            };

            return LancasterStemmer.Stem(s);
        }

        #endregion

        private static volatile bool ctrlC = false;

        [STAThread]
        static void Main(string[] args) {
            Console.CancelKeyPress += (o, e) => { ctrlC = true; e.Cancel = true; };

            //var ss = File.ReadAllLines("d:/2.txt").Select(x => stem(x)).ToArray();
            //File.WriteAllLines("d:/2-2.txt", ss);
            var s = stem("management");
            Console.WriteLine(s);

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