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
using System.Windows.Forms;
using System.Diagnostics;
using CsvHelper;
using System.Globalization;
using System.Web;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;
using OpenNLP.Tools.Chunker;
using OpenNLP.Tools.Parser;
using OpenNLP.Tools.PosTagger;
using OpenNLP.Tools.SentenceDetect;
using OpenNLP.Tools.Tokenize;
using OpenNLP.Tools.Trees;
using System.Security.Cryptography;
using Newtonsoft.Json;
using System.Drawing;
using Markdig;

namespace ConApp {
    public class Tag {
        public static Regex tagRe { get; } = new Regex(@"<(?<close>/)?(?<name>[^/> ]+)(""[^""]*""|[^/>])*/?>");
        private static Regex attrRe = new Regex(@" +(?<attr>[^=]+)=""(?<value>[^""]*)""");

        public string name { get; set; }

        public bool isOpen { get; private set; }

        public Dictionary<string, string> attr { get; } = new Dictionary<string, string>();

        public static Tag[] Parse(string s) {
            var r = new List<Tag>();
            var m = tagRe.Match(s);
            while (m.Success) {
                var tag = new Tag();
                tag.name = m.Groups["name"].Value;
                tag.isOpen = m.Groups["close"].Value != "/";
                var m2 = attrRe.Match(m.Value);
                while (m2.Success) {
                    tag.attr.Add(m2.Groups["attr"].Value, m2.Groups["value"].Value);
                    m2 = m2.NextMatch();
                }
                r.Add(tag);
                m = m.NextMatch();
            }
            return r.ToArray();
        }

        public static string Clear(string s, HashSet<string> names = null) {
            return Program.handleString(s, tagRe, (x, m) => (names == null || names.Contains(m.Groups["name"].Value.ToLower())) ? "" : x);
        }
    }

    static class Program {

        static Program() {
            // extend cmu
            cmu.Where(kv => kv.Key.Length == 2 && "AEIOU".Contains(kv.Key.Substring(0, 1))).ToList().ForEach(kv => {
                for (var i = 0; i < 3; i++) {
                    var v = (i > 0 ? char.ToUpper(kv.Value[0]) : kv.Value[0]) + kv.Value.Substring(1);
                    var k = $"{kv.Key}{i}";
                    if (cmu.ContainsKey(k))
                        continue;
                    cmu.Add(k, v);
                }
            });

            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".sandbox");
            configPath = File.Exists(configPath) ? configPath : @"d:/.sandbox";
            // config
            config = File.ReadAllLines(configPath).Where(x => !x.StartsWith("//")).Select(x => {
                var i = x.IndexOf(":");
                return new KeyValuePair<string, string>(x.Substring(0, i), x.Substring(i + 1).Trim());
            }).ToDictionary(x => x.Key, x => x.Value);
        }

        static Dictionary<string, string> config;

        public static string handleString(string s, Regex re, Func<string, Match, string> handler) {
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

        static ChromeDriver chromeDriver;

        static int navI = 0;

        static string gtranslate(string s) {
            if (chromeDriver == null)
                chromeDriver = new ChromeDriver();

            chromeDriver.Navigate().GoToUrl("https://translate.google.com/details?sl=en&tl=ru&text=" + s + "&op=translate");
            Thread.Sleep(navI % 20 == 0 ? 5000 : 1500);
            navI++;
            var html = chromeDriver.FindElement(By.CssSelector("[jsname=kepomc]")).GetAttribute("innerHTML");

            var dom = CQ.Create(html);
            var h3 = dom.Find("h3").Where(x => x.Cq().Text().Contains("варианты перевода")).FirstOrDefault();
            if (h3 == null) {
                s = $"{s} {{прочее}} NotFound";
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

        static Regex gValRe = new Regex(@"^(.*?) \[([1-3])\]$");

        static (string val, int freq)[] getGVals(string s, string part) {
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

        static string pathEx(string path, string ex) {
            var ext = Path.GetExtension(path);
            return $"{path.Substring(0, path.Length - ext.Length)}{ex}{ext}";
        }

        static void freqAddGVals(string path, string gPath) {
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

        static StringSplitOptions ssop = StringSplitOptions.RemoveEmptyEntries;

        #region stemmer

        static Dictionary<string, string> _lemmas;

        static Dictionary<string, List<string>> _lemmaForms;

        static Dictionary<string, string> lemmas {
            get {
                if (_lemmas == null) {
                    loadLemmas();
                }

                return _lemmas;
            }
        }

        static Dictionary<string, List<string>> lemmaForms {
            get {
                if (_lemmaForms == null) {
                    loadLemmas();
                }

                return _lemmaForms;
            }
        }

        static void loadLemmas() {
            _lemmas = new Dictionary<string, string>();
            _lemmaForms = new Dictionary<string, List<string>>();
            File.ReadAllLines("d:/Projects/smalls/e_lemma.txt")
                .Where(x => !x.StartsWith("["))
                .Select(x => x.ToLower().Split(new[] { " -> ", "," }, ssop)).ToList()
                .ForEach(x => {
                    var key = x[0];
                    var fs = new List<string>();
                    _lemmaForms[key] = fs;
                    foreach (var xx in x.Skip(1)) {
                        _lemmas[xx] = key;
                        fs.Add(xx);
                    }
                });
        }

        static IEnumerable<string> getLemmaForms(string s, bool strick = false) {
            s = s.ToLower();
            if (lemmas.ContainsKey(s) && !strick) {
                s = lemmas[s];
            }

            var r = Enumerable.Repeat(s, 1);
            if (lemmaForms.ContainsKey(s)) {
                r = r.Concat(lemmaForms[s]);
            }
            return r;
        }

        static string getLemmaBase(string s) {
            var part = "";
            if (s.Contains(" {")) {
                var sp = s.Split(new[] { " {" }, ssop);
                s = sp[0];
                part = $" {{{sp[1]}";
            }
            return (lemmas.ContainsKey(s.ToLower()) ? lemmas[s] : s) + part;
        }

        static Dictionary<string, int> freqGroups {
            get => _freqGroups ?? (_freqGroups = loadFreqGroups(@"d:\Projects\smalls\freq-20k.txt", config["freqGroups"].Split(',').Select(x => int.Parse(x)).ToArray()));
        }

        static Dictionary<string, int> _freqGroups;

        static Dictionary<string, int> loadFreqGroups(string path, int[] levels) {
            var r = new Dictionary<string, int>();
            levels = (new[] { 0 }).Concat(levels).Reverse().ToArray();
            var exs = new HashSet<string>(File.ReadAllLines(@"d:\Projects\smalls\freq-20k-excepts.txt"));
            File.ReadAllLines(path).Reverse().ToList().ForEach(x => {
                if (x.Contains("{междометие}")) return;
                var xs = x.Split(' ');
                var n = int.Parse(xs[0]);
                var w = xs[1].ToLower();
                if (exs.Contains(w)) return;

                var g = 0;
                while (n < levels[g]) g++;
                getLemmaForms(w, true).ToList().ForEach(f => {
                    if (f.Length == 1) return;
                    r[f] = g;
                });
            });
            return r;
        }

        static Regex freqGoupRe = new Regex(@"</?[^/> ]+(""[^""]*""|[^/>])*/?>|[a-z]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static string[][] freqGroupColors = new[] { new [] { "#ff9999", "#99cbff", "#99ff99", "#ffbc8c" }, new[] { "#ff9999", "#0079ff", "#00b200", "#cc5400" } };
        static string freqGrouping(string s, bool white = false) {
            var cs = freqGroupColors[white ? 0 : 1];
            s = handleString(s, freqGoupRe, (x, m) => {
                if (x[0] == '<') return x;
                freqGroups.TryGetValue(x.ToLower(), out var g);
                return g == cs.Length || g == 0 ? x : $"<font color=\"{cs[g]}\">{x}</font>";
            });
            return s;
        }

        static Dictionary<string,string> _existingWords;

        static Dictionary<string, string> existingWords {
            get {
                if (_existingWords != null)
                    return _existingWords;

                _existingWords = File.ReadAllLines("d:/Projects/smalls/en-dic.txt")
                    .Where(x => !x.Contains("Not found"))
                    .Select(x => {
                        var sp = x.ToLower().Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
                        var key = sp[0];
                        var t = prons.ContainsKey(key) ? prons[key] : "";
                        return new KeyValuePair<string, string>(key, t);
                    }).ToDictionary(x => x.Key, x => x.Value);
                    
                return _existingWords;
            }
        }

        static Regex prefixRe = new Regex("^(anti|auto|de|dis|down|extra|hyper|il|im|inter|in|ir|mega|mid|mis|non|over|out|post|pre|pro|re|semi|sub|super|tele|trans|ultra|un|up)", RegexOptions.Compiled);

        static string stem(string s) {
            s = s.ToLower();
            if (lemmas.TryGetValue(s, out var _s)) {
                s = _s;
            }

            while (true) {
                var m = prefixRe.Match(s);
                if (!m.Success)
                    break;

                _s = s.Substring(m.Value.Length);
                if (_s.Length < 3 || !existingWords.ContainsKey(_s))
                    break;

                s = _s;
            };

            return LancasterStemmer.Stem(s);
        }

        #endregion
        
        #region RU PRON

        static Dictionary<string, string> cmu = new Dictionary<string, string>() {
            { "AA", "а" }, { "AE", "э" }, { "AH0", "э" }, { "AH", "а" }, { "AO", "о" },
            { "AW", "ау" }, { "AY", "ай" }, { "EH", "е" }, { "ER", "эр" }, { "EY", "ей" },
            { "IH", "ы" }, { "IY", "и" }, { "OW", "oу" }, { "OY", "ой" }, { "UH", "у" },
            { "UW", "у" }, { "B", "б" }, { "CH", "ч" }, { "D", "д" }, { "DH", "ð" },
            { "F", "ф" }, { "G", "г" }, { "HH", "х" }, { "JH", "дж" }, { "K", "к" },
            { "L", "л" }, { "M", "м" }, { "N", "н" }, { "NG", "н" }, { "P", "п" },
            { "R", "р" }, { "S", "с" }, { "SH", "ш" }, { "T", "т" }, { "TH", "θ" },
            { "V", "в" }, { "W", "у" }, { "Y", "й" }, { "Z", "з" }, { "ZH", "ж" },
        };

        static Dictionary<string, string> _prons;

        static Dictionary<string, string> prons {
            get {
                if (_prons == null) {
                    _prons = File.ReadAllLines("d:/Projects/smalls/pronun.txt")
                        .Select(x => x.Split(' '))
                        .ToDictionary(x => x[0], x => x[1]);
                }

                return _prons;
            }
        }

        #endregion

        public static void Shuffle<T>(this IList<T> list, int start, int end) {
            var len = end - start;
            var rs = list.Skip(start).Take(len).OrderBy(_ => Guid.NewGuid()).ToArray();
            for (var i = 0; i < len; i++) {
                list[start + i] = rs[i];
            }
        }

        static Dictionary<string, string> partAbbr = new Dictionary<string, string> {
            { "артикль", "арт." },
            { "глагол", "гл." },
            { "местоимение", "мест." },
            { "наречие", "нар." },
            { "предлог", "пред." },
            { "прилагательное", "прил." },
            { "союз", "союз" },
            { "существительное", "сущ." },
            { "числительное", "числ." },
            { "междометие", "межд." },
            { "определитель", "опред." },
            { "прочее", "прочее" },
            { "existential", "exist." },
        };

        static Regex spRe = new Regex("[^a-z]+", RegexOptions.Compiled);

        static string getPron(string s) {
            s = s.ToLower();
            var ks = spRe.Split(s);
            var ts = new List<string>();
            foreach (var k in ks) {
                prons.TryGetValue(k, out var t);
                if (t == null)
                    continue;

                ts.Add(t);
            }

            if (ts.Count == 0)
                return null;

            return string.Join(" ", ts);
        }

        static void prepareWords(string path) {
            var ints = new Dictionary<int, int>() { { 1, 2 }, { 2, 4 }, { 4, 7 }, { 7, 12 }, { 12, 20 } };
            var intMax = ints.Values.Max();


            // read words
            var ws = File.ReadAllLines(path).Select(x => {
                var sp = x.Split(new[] { " {", "} ", " (", ")" }, StringSplitOptions.RemoveEmptyEntries);
                var r = (k: sp[0], p: sp[1], v: sp[2], e: (string)null, t: (string)null, i: 1);
                if (sp.Length > 3)
                    r.e = sp[3];

                r.t = getPron(r.k);

                return r;
            }).ToArray();
            ws.Shuffle(0, ws.Length);
            var newDays = ws.Length / 10;

            // fill
            var ds = ws.Take(newDays + intMax - 1).Select(x => (new[] { ws[0] }).ToList()).ToList();
            ds.ForEach(x => x.Clear());
            for (var j = 0; j < ws.Length; j += 10) {
                for (var i = j; i < j + 10; i++) {
                    ds[j / 10].Add(ws[i]);
                }
            }

            // add new else
            for (var i = 0; i < newDays; i++) {
                var l = ds[i].ToList();
                for (var j = 0; j < l.Count; j++) {
                    var x = l[j];
                    x.i = 0;
                    l[j] = x;
                }
                l.Shuffle(0, l.Count);
                ds[i].AddRange(l.ToList());
                l.Shuffle(0, l.Count);
                ds[i].AddRange(l.ToList());
            }

            // repeat
            for (var i = 0; i < ds.Count; i++) {
                ds[i].ForEach(w => {
                    if (w.i == intMax || w.i == 0)
                        return;

                    var d = ints[w.i] - w.i;
                    w.i += d;
                    ds[i + d].Add(w);
                });
            }

            // shuffle nexts
            for (var i = 0; i < ds.Count; i++) {
                var start = i < newDays ? 30 : 0;
                ds[i].Shuffle(start, ds[i].Count);
            }

            // output
            var rs = new List<string>();
            var sn = 0;
            for (var j = 0; j < ds.Count; j++) {
                var d = ds[j];
                rs.Add($"## DAY {j+1} ДЕНЬ {j + 1}");
                for (var i = 0; i < d.Count; i += 10) {
                    sn++;
                    rs.Add($"### STORY {sn} ИСТОРИЯ {sn}");
                    var r = d.Skip(i).Take(10).ToList();
                    var s = r.Select(x => (x.i == intMax ? "!" : "") + $"**{x.k}** [{x.t}] {{{partAbbr[x.p]}}}" + (x.i == 1 ? " " + x.v : "")).ToList();
                    rs.Add("WORDS " + string.Join("; ", s));
                    var b = r.Select(x => $"{x.k} (как {x.p}: {x.v})").ToList();
                    rs.Add("BODY " + string.Join("; ", b));
                }
            }

            File.WriteAllLines(pathEx(path, "-2"), rs);
        }

        static List<string> allForms(IEnumerable<string> ss) {
            var rs = new List<string>();
            foreach (var w in ss) {
                if (lemmas.TryGetValue(w, out var k)) {
                    rs.AddRange(lemmaForms[k]);
                }
                else {
                    rs.Add(w);
                }
            }

            return rs.Distinct().ToList();
        }

        static void fixQuotes(string path) {
            var re = new Regex(@"\w\""\w", RegexOptions.Compiled);
            var ss = File.ReadAllLines(path).Select(x => handleString(x.Replace("'", "\""), re, (y, m) => y.Replace("\"", "'")));
            File.WriteAllLines(path, ss);
        }

        static void deeplSplit(string path) {
            var qs = new Queue<string>(File.ReadAllLines(path));
            var rs = new List<string>();
            while (qs.Count > 0) {
                var len = 0;
                while (qs.Count > 0 && len + qs.Peek().Length < 4900) {
                    var _s = qs.Dequeue();
                    len += _s.Length + 2;
                    rs.Add(_s);
                }
                rs.Add("---");
            }
            File.WriteAllLines(pathEx(path, "-split"), rs);
        }

        static void deepl(string path) {
            var advRe = new Regex("\n\nПереведено с помощью DeepL.*", RegexOptions.Multiline);

            var ss = File.ReadAllLines(path);
            path = pathEx(path, "-deepl");
            
            var rs = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
            var qs = new Queue<string>(ss.Skip(rs.Count));

            var driver = new ChromeDriver();
            /*
            driver.Navigate().GoToUrl("https://www.deepl.com/ru/login");
            Thread.Sleep(2000);
            var pwd = config["deeplPwd"];
            driver.FindElement(By.CssSelector("#menu-login-username")).SendKeys("adloky@gmail.com");
            driver.FindElement(By.CssSelector("#menu-login-password")).SendKeys(pwd);
            driver.FindElement(By.CssSelector("#menu-login-submit")).Click();
            Thread.Sleep(5000);
            */

            while (qs.Count > 0) {
                Thread.Sleep(2000);
                driver.Navigate().GoToUrl("https://www.deepl.com/translator#en/ru/");
                var ms = new List<string>();
                var len = 0;
                while (qs.Count > 0 && len + qs.Peek().Length < 1400) {
                    var _s = qs.Dequeue();
                    len += _s.Length + 2;
                    ms.Add(_s);
                }

                var s = string.Join("\n", ms);

                Thread.Sleep(2000);
                var ta = driver.FindElement(By.CssSelector("div[contenteditable]"));
                IJavaScriptExecutor jsExecutor = (IJavaScriptExecutor)driver;
                jsExecutor.ExecuteScript("arguments[0].innerText='" + s.Replace("\n", "\\n").Replace("'", "\\'") + "';", ta);
                ta.SendKeys(" ");

                IWebElement btn = null;
                while (btn == null) {
                    Thread.Sleep(2000);
                    try {
                        var tools = driver.FindElements(By.CssSelector(".h-14"))[1];
                        jsExecutor.ExecuteScript("arguments[0].scrollIntoView(true);", tools);

                        Thread.Sleep(2000);
                        btn = driver.FindElement(By.CssSelector(@"button[data-testid=""translator-target-toolbar-copy""]"));
                        btn.Click();
                    }
                    catch {
                        btn = null;
                    }
                }
                Thread.Sleep(2000);

                ClipboardAsync Clipboard2 = new ClipboardAsync();
                var r = Clipboard2.GetText();
                //var r = Clipboard.GetText();
                r = r.Replace("\r", "");
                r = advRe.Replace(r, "");
                File.AppendAllLines(path, r.Split('\n').Select(x => x.Trim()));
            }
        }

        static Dictionary<string, string> posTags = new Dictionary<string, string> {
            { "RB", "наречие" }, { "RBR", "наречие" }, { "RBS", "наречие" }, { "WRB", "наречие" },
            { "NN", "существительное" }, { "NNS", "существительное" }, { "NNP", "существительное" },
            { "NNPS", "существительное" }, { "JJ", "прилагательное" }, { "JJR", "прилагательное" },
            { "JJS", "прилагательное" }, { "CC", "союз" }, { "DT", "определитель" },
            { "PDT", "определитель" }, { "WDT", "определитель" }, { "UH", "междометие" },
            { "MD", "глагол" }, { "VB", "глагол" }, { "VBD", "глагол" }, { "VBG", "глагол" },
            { "VBN", "глагол" }, { "VBP", "глагол" }, { "VBZ", "глагол" }, { "RP", "частица" },
            { "PRP", "местоимение" }, { "PRP$", "местоимение" }, { "WP", "местоимение" },
            { "WP$", "местоимение" }, { "IN", "предлог" }
        };

        static IEnumerable<string> posReduce(string path) {
            var exQue = new Queue<string>();
            var re = new Regex(@"\s+|[^\s]+", RegexOptions.Compiled);
            var s = File.ReadAllText(path);
            var ss = re.Matches(s).Cast<Match>().Select(x => x.Value).ToArray();
            var pRe = new Regex(@"_[^_]+$", RegexOptions.Compiled);

            var pathPos = pathEx(path, "-pos");
            // java -mx300m -cp 'stanford-postagger.jar:' edu.stanford.nlp.tagger.maxent.MaxentTagger -model $1 -textFile $2
            if (!File.Exists(pathPos)) {
            var proc = new Process();
                proc.StartInfo.FileName = "java.exe";
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.Arguments = "-mx300m -cp \"stanford-postagger.jar;\" edu.stanford.nlp.tagger.maxent.MaxentTagger -model models/english-left3words-distsim.tagger -textFile " + path;
                proc.StartInfo.WorkingDirectory = "d:/Portables/postagger";
                proc.StartInfo.RedirectStandardOutput = true;
                proc.Start();
                File.WriteAllText(pathPos, proc.StandardOutput.ReadToEnd().Replace("\n", " "));
            }

            var ps = File.ReadAllText(pathEx(path, "-pos"))
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => {
                    var t = pRe.Match(x).Value.Substring(1);
                    var w = x.Substring(0, x.Length - t.Length - 1);
                    return (w: w, t: t);
                }).ToArray();

            var i = 0;
            var j = 0;
            var prevDot = false;
            while (j < ps.Length) {
                var w = ss[i++];
                if (char.IsWhiteSpace(w[0])) {
                    yield return $"{w} {{{"пробел"}}}";
                    continue;
                }

                var wp = ps[j].w;
                if (!w.StartsWith(wp) && prevDot && wp == ".") {
                    j++;
                    wp = ps[j].w;
                }
                if (w.Length < wp.Length) {
                    exQue.ToList().ForEach(Console.WriteLine);
                    throw new Exception();
                }

                while (w.Length >= wp.Length) {
                    exQue.Enqueue($"{w} {wp}");
                    while (exQue.Count > 10) exQue.Dequeue();
                    if (!w.StartsWith(wp)) {
                        exQue.ToList().ForEach(Console.WriteLine);
                        throw new Exception();
                    }

                    if (!posTags.TryGetValue(ps[j].t, out var p)) {
                        p = "прочее";
                    }
                    prevDot = wp.Last() == '.';
                    yield return $"{ps[j].w} {{{p}}}";

                    j++;
                    if (w.Length == wp.Length)
                        break;

                    wp += ps[j].w;
                }
            }
        }

        static Dictionary<string, string> loadDic(string path) {
            var dic = new Dictionary<string, string>();
            var nRe = new Regex(@"^\d+ ", RegexOptions.Compiled);
            File.ReadAllLines(path).Select(x => {
                x = nRe.Replace(x, "");
                var xsp = x.Split('}');
                var k = xsp[0] + "}";
                var v = xsp[1].Trim();
                return (k, v);
            }).ToList().ForEach(kv => {
                if (kv.k.Count(x => x == ' ') > 1)
                    return;

                if (dic.ContainsKey(kv.k)) {
                    dic[kv.k] = $"{dic[kv.k]}; {kv.v}";
                }
                else {
                    dic[kv.k] = kv.v;
                }
            });
            return dic;
        }

        static Dictionary<string, string> learnDic = loadDic(@"d:\Projects\smalls\learn-dic-3000.txt");
        static HashSet<string> learnSet = new HashSet<string>(learnDic.Keys.SelectMany(x => getLemmaForms(x.Split(new[] { " {" }, ssop)[0])));

        static string getLearn(string s, Dictionary<string, string> dic = null) {
            if (dic == null)
                dic = learnDic;
            s = s.ToLower();
            var ssp = s.Split(' ');
            if (!dic.TryGetValue(s, out var v) && lemmas.TryGetValue(ssp[0], out var lemma)) {
                s = $"{lemma} {{{ssp[1]}}}";
                dic.TryGetValue(s, out v);
            }

            if (v == null)
                return null;

            return $"{s} {v}";
        }

        static void learnStat(string path) {
            var rDic = new Dictionary<string, int>();
            foreach (var pos in posReduce(path)) {
                var r = getLearn(pos);
                if (r == null)
                    continue;

                if (!rDic.ContainsKey(r)) {
                    rDic[r] = 0;
                }

                rDic[r] += 1;
            }

            var rs = rDic.OrderByDescending(x => x.Value)
                .Select(x => $"{x.Value} {x.Key}").ToArray();

            File.WriteAllLines(pathEx(path, "-dic"), rs);
        }

        static void makeTip(string path, bool srt = false) {
            var dic = loadDic(pathEx(path, "-dic"));
            var sb = new StringBuilder();
            foreach (var wp in posReduce(path)) {
                var w = wp.Split(new[] { " {" }, ssop)[0];
                var r = getLearn(wp, dic);
                if (r == null) {
                    sb.Append(w);
                    continue;
                }
                
                var pron = getPron(w.ToLower());
                if (pron != null) {
                    r = r.Replace("{", $"[{pron}] {{");
                }
                if (srt) {
                    sb.Append($"<u>{w}</u>");
                }
                else {
                    sb.Append($"<span class='tip-wrap' data-text='{r}'>**{w}**<span class='tip-text'> </span></span>");
                }
            }
            File.WriteAllText(pathEx(path, "-tip"), sb.ToString());
        }

        static Regex twSpRe = new Regex(" +", RegexOptions.Compiled);
        static Regex twSNlRe = new Regex(@"[!,\.:;?]\r\n", RegexOptions.Compiled | RegexOptions.Multiline);
        static Regex twSpNlRe = new Regex(@"^ +", RegexOptions.Compiled | RegexOptions.Multiline);
        static Regex twNlNlRe = new Regex(@"(\r\n)+", RegexOptions.Compiled | RegexOptions.Multiline);
        static Regex twNlRe = new Regex(@"\r\n", RegexOptions.Compiled | RegexOptions.Multiline);
        static Regex twSpSRe = new Regex(@" [!,\.:;?]", RegexOptions.Compiled | RegexOptions.Multiline);
        static Regex twHttpRe = new Regex(@"https?://t.co/[a-zA-Z0-9]+", RegexOptions.Compiled | RegexOptions.Multiline);


        static string tweet2ascii(string s) {
            s = s.Replace("’", "'").Replace("…", "...").Replace("“", "\"").Replace("”", "\"").Replace("—", "-").Replace("–", "-");
            s = string.Concat(s.Select(x => x > 127 ? ' ' : x));
            s = twHttpRe.Replace(s, "http");
            s = twSpNlRe.Replace(s, "");
            s = twNlNlRe.Replace(s, "\r\n");
            s = handleString(s, twSNlRe, (x, m) => $"{x[0]} ");
            s = twNlRe.Replace(s, ". ");
            s = twSpRe.Replace(s, " ");
            s = handleString(s, twSpSRe, (x, m) => $"{x[1]}");
            s = s.Trim();

            return s;
        }

        static void readCsv(string path) {
            using (var reader = new StreamReader(path))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture)) {
                var rs = csv.GetRecords(new { author = "" , content = "" });
                //var i = 0;

                File.WriteAllLines("d:/.temp/tweets.txt", rs.Select(x =>$"{x.author}: {tweet2ascii(x.content)}"));
            }
        }

        static string gemini(string s) {
            s = s.Replace(@"\", @"\\").Replace(@"""", @"\""").Replace("\r", @"\r").Replace("\n", @"\n");
            var url = $"https://generativelanguage.googleapis.com/v1/models/gemini-1.5-flash:generateContent?key={config["geminiKey"]}";
            var request = WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            s = $"{{\"contents\":[{{\"parts\":[{{\"text\":\"{s}\"}}]}}]}}";
            var data = Encoding.UTF8.GetBytes(s);
            request.ContentLength = data.Length;
            using (var stream = request.GetRequestStream()) {
                stream.Write(data, 0, data.Length);
            }
            var r = (string)null;

            using (var response = request.GetResponse())
            using (Stream dataStream = response.GetResponseStream()) {
                var reader = new StreamReader(dataStream);
                r = reader.ReadToEnd();
            }

            var obj = JObject.Parse(r);
            r = string.Join("\n", obj.SelectToken("$.candidates[0].content.parts").Select(x => x.SelectToken(".text")));

            return r;
        }

        static void srtClear(string path) {
            var ss = File.ReadAllLines(path);
            var rs = new List<string>();
            var i = 0;
            foreach (var s in ss) {
                i++;
                if (i < 3)
                    continue;
                if (string.IsNullOrEmpty(s)) {
                    i = 0;
                    continue;
                }
                rs.Add(s);
            }
            File.WriteAllLines(pathEx(path, "-clear"), rs);
        }

        
        static IEnumerable<List<string>> srtHandle(string path) {
            var ss = File.ReadAllLines(path);
            var numRe = new Regex(@"^\d+$", RegexOptions.Compiled);
            var timeRe = new Regex(@"^\d\d:\d\d:\d\d,\d\d\d --> \d\d:\d\d:\d\d,\d\d\d$", RegexOptions.Compiled);
            var i = 0;
            var rs = new List<string>();
            for (var j = 0; j < ss.Length; j++) {
                i++;
                var s = ss[j];
                if (i < 3) {
                    if (i == 1 && !numRe.IsMatch(s) || i == 2 && !timeRe.IsMatch(s)) {
                        throw new Exception();
                    }
                    rs.Add(s);
                    continue;
                }

                if (s == "") {
                    i = 0;
                    yield return rs;
                    rs = new List<string>();
                    continue;
                }

                rs.Add(s);
            }
        }
        
        static void srtCheck(string path) {
            var ss = File.ReadAllLines(path);
            var numRe = new Regex(@"^\d+$", RegexOptions.Compiled);
            var timeRe = new Regex(@"^\d\d:\d\d:\d\d,\d\d\d --> \d\d:\d\d:\d\d,\d\d\d$", RegexOptions.Compiled);
            var backRe = new Regex(@"-?\([^a-z)]*\)", RegexOptions.Compiled);
            var i = 0;
            for (var j = 0; j < ss.Length; j++) {
                i++;
                var s = ss[j];
                if (i < 3) {
                    if (i == 1 && !numRe.IsMatch(s) || i == 2 && !timeRe.IsMatch(s)) {
                        Console.WriteLine($"[{j+1}] {path}");
                        throw new Exception();
                    }
                    continue;
                }

                if (s == "") {
                    i = 0;
                    continue;
                }
            }
        }

        static void srtCombine(string path) {
            var fs = Directory.GetFiles(path, "*.srt").Where(x => !x.EndsWith("all.srt")).ToArray();
            var rs = new List<string>();
            foreach (var f in fs) {
                var ss = File.ReadAllLines(f).ToList();
                if (ss.Last() != "") ss.Add("");
                if (ss.Any(x => x == "00:00:00,000 --> 00:00:00,000"))
                    throw new Exception();

                ss.Insert(0, "");
                ss.Insert(0, Path.GetFileName(f));
                ss.Insert(0, "00:00:00,000 --> 00:00:00,000");
                ss.Insert(0, "0");

                rs.AddRange(ss);
            }
            File.WriteAllLines(Path.Combine(path, "all.srt"), rs);
        }

        static void srtSplit(string path, string ext = null) {
            srtCheck(path);
            var dir = Path.GetDirectoryName(path);
            var f = (string)null;
            var rs = new List<string>();
            var end = (new[] { "0", "00:00:00,000 --> 00:00:00,000", "END" }).ToList();
            foreach (var ss in srtHandle(path).Concat(Enumerable.Repeat(end,1))) {
                if (ss[1] == "00:00:00,000 --> 00:00:00,000") {
                    if (f != null)
                        File.WriteAllLines(Path.Combine(dir, ext == null ? f : f.Replace(".srt", $".{ext}.srt")), rs);

                    f = ss[2];
                    rs = new List<string>();
                }
                else {
                    rs.AddRange(ss);
                    rs.Add("");
                }
            }
        }

        static void srtLine(string path, bool tagClear = true) {
            var rs = new List<string>();
            srtCheck(path);
            foreach (var ss in srtHandle(path)) {
                ss[2] = string.Join(" ", ss.Skip(2));
                if (tagClear) {
                    ss[2] = Tag.Clear(ss[2]);
                    ss[2] = ss[2].Replace("{\\an8}", "");
                }
                rs.AddRange(ss.Take(3).Concat(Enumerable.Repeat("", 1)));
            }
            File.WriteAllLines(pathEx(path, "-line"), rs);
        }

        static void srtLearn(string path) {
            var s = File.ReadAllText(path);
            s = Tag.Clear(s, new HashSet<string>() { "u", "font" });
            s = freqGrouping(s, true);
            File.WriteAllText(path, s);
        }

        static Dictionary<string, string> openNlpTags = initOpenNlpTags();

        static Dictionary<string, string> initOpenNlpTags() {
            var dic = new Dictionary<string, string>();
            "MD VB VBD VBG VBN VBP VBZ".Split(' ').ToList().ForEach(x => { dic.Add(x, "{глагол}"); });
            "NNP NNPS".Split(' ').ToList().ForEach(x => { dic.Add(x, "{имя}"); });
            "UH".Split(' ').ToList().ForEach(x => { dic.Add(x, "{междометие}"); });
            "PRP PRP$ WP WP$".Split(' ').ToList().ForEach(x => { dic.Add(x, "{местоимение}"); });
            "RB RBR RBS WRB".Split(' ').ToList().ForEach(x => { dic.Add(x, "{наречие}"); });
            "DT WDT".Split(' ').ToList().ForEach(x => { dic.Add(x, "{определитель}"); });
            "JJ JJR JJS".Split(' ').ToList().ForEach(x => { dic.Add(x, "{прилагательное}"); });
            "EX FW LS POS SYM TO RP".Split(' ').ToList().ForEach(x => { dic.Add(x, "{прочее}"); });
            "CC IN PDT".Split(' ').ToList().ForEach(x => { dic.Add(x, "{служебное}"); });
            "NN NNS".Split(' ').ToList().ForEach(x => { dic.Add(x, "{существительное}"); });
            "CD".Split(' ').ToList().ForEach(x => { dic.Add(x, "{числительное}"); });
            return dic;
        }

        static EnglishRuleBasedTokenizer openNlpTokenizer = new EnglishRuleBasedTokenizer(false);
        static EnglishMaximumEntropyPosTagger openNlpTagget = new EnglishMaximumEntropyPosTagger(@"d:\Projects\smalls\OpenNLP\EnglishPOS.nbin", @"d:\Projects\smalls\OpenNLP\tagdict");

        static IEnumerable<string> posTagging(string s) {
            var ts = openNlpTokenizer.Tokenize(s);
            var pos = openNlpTagget.Tag(ts);
            for (var i = 0; i < ts.Length; i++) {
                if (openNlpTags.TryGetValue(pos[i], out var p2)) {
                    yield return $"{ts[i].ToLower()} {p2}";
                }
            }
        }


        static Regex ampRe = new Regex(@"[a-zA-Z']+", RegexOptions.Compiled);

        static string handleAmp(string s) {
            return handleString(s, ampRe, (x, m) => {
                if (!x.Contains("'")) return x;
                var xl = x.ToLower();
                if (xl.Contains("can't")) {
                    x = x.Replace("an't", "an not");
                    xl = x.ToLower();
                }

                if (xl.Contains("won't")) {
                    x = x.Replace("on't", "ill not");
                    xl = x.ToLower();
                }

                if (xl.Contains("n't")) {
                    x = x.Replace("n't", " not");
                }

                if (!x.Contains("'")) return x;

                if ((x.StartsWith("'") || s.EndsWith("'")) && x.Count(c => c == '\'') == 1)
                    return x;

                x = x.Substring(0, x.LastIndexOf("'"));

                return x;
            });
        }

        private static SHA256 sha;

        public static Guid BytesToGuid(byte[] bytes, int len) {
            if (sha == null) {
                sha = SHA256.Create();
            }

            var hash = sha.ComputeHash(bytes, 0, len);
            var guid = new Guid(hash.Take(16).ToArray());
            return guid;
        }

        public static Guid StringToGuid(string s) {
            var bytes = Encoding.ASCII.GetBytes(s);
            return BytesToGuid(bytes, bytes.Length);
        }

        public static IEnumerable<List<string>> dsl(IList<string> ss) {
            var l = new List<string>();
            for (var i = 0; i < ss.Count; i++) {
                var s = ss[i];
                if (!s.StartsWith("\t") && l.Count != 0) {
                    yield return l;
                    l = new List<string>();
                }
                l.Add(s);
            }
            yield return l;
        }

        public static void comicOcr(string path) {
            if (path.Last() != '/' && path.Last() != '\\') path += "/";
            var rs = new List<string>();
            var ps = Directory.GetFiles(path, "*.*").Where(x => x.EndsWith(".jpg") || x.EndsWith(".jpeg")).OrderBy(p => p).ToArray();
            foreach (var p in ps) {
                var jsonPath = Path.ChangeExtension(p, ".json");
                if (File.Exists(jsonPath))
                    continue;

                var httpClient = new HttpClient();
                var form = new MultipartFormDataContent();
                form.Add(new StringContent(config["ocrKey"]), "apikey");
                form.Add(new StringContent("eng"), "language");
                form.Add(new StringContent("2"), "ocrengine");
                form.Add(new StringContent("True"), "isOverlayRequired");
                var imageData = File.ReadAllBytes(p);
                form.Add(new ByteArrayContent(imageData, 0, imageData.Length), "image", "image.jpg");
                //var response = await httpClient.PostAsync();
                var task1 = Task.Run(() => httpClient.PostAsync("https://api.ocr.space/Parse/Image", form));
                task1.Wait();
                var response = task1.Result;

                var task2 = Task.Run(() => response.Content.ReadAsStringAsync());
                task1.Wait();
                var s = task2.Result;

                try {
                    JsonConvert.DeserializeObject<OcrRootObject>(s);
                }
                catch { Console.WriteLine(p + ": " + s); }


                var bm = new Bitmap(p);
                s = Regex.Replace(s, @"\}$", $",Width:{bm.Width}}}");
                File.WriteAllText(jsonPath, s);
                Console.WriteLine((s.Contains("\"IsErroredOnProcessing\":true") ? "ERROR: " : "") + p);

            }

            //rs.Add("* * * * " + Path.GetFileNameWithoutExtension(p) + " (" + n + ")");
            //rs.AddRange(File.ReadAllLines(p).Where(s => tRe.IsMatch(s)).Select(s => tRe.Replace(s, "")));
            //File.WriteAllLines(path + "en.txt", rs);
        }

        public static void comicOcrPost(string path, int fsize, int indent) {
            if (path.Last() != '/' && path.Last() != '\\') path += "/";
            var rs = new List<string>();
            var n = 0;
            foreach (var p in Directory.GetFiles(path, "*.json").OrderBy(p => p)) {
                n++;
                rs.Add("* * * * " + Path.GetFileNameWithoutExtension(p) + " (" + n + ")");
                var or = JsonConvert.DeserializeObject<OcrRootObject>(File.ReadAllText(p));
                var rects = or.GetRects().ToArray();
                var r = OcrRect.Group(rects, or.Width, fsize, indent);
                rs.AddRange(r.Select(x => string.Join(" ", x.Select(y => y.Text))));
            }
            File.WriteAllLines(path + "en.txt", rs);
        }

        public static void comicJoin(string path) {
            if (path.Last() != '/' && path.Last() != '\\') path += "/";
            var rs = new List<string>();
            var n = 0;
            var tRe = new Regex("^text: *");
            Directory.GetFiles(path, "*_translations.txt").OrderBy(p => p).ToList().ForEach(p => {
                n++;
                rs.Add("* * * * " + Path.GetFileNameWithoutExtension(p).Replace("_translations", "") + " (" + n + ")");
                rs.AddRange(File.ReadAllLines(p).Where(s => tRe.IsMatch(s)).Select(s => tRe.Replace(s, "")));
            });
            File.WriteAllLines(path + "en.txt", rs);
        }

        public static void comicComplete(string path) {
            if (path.Last() != '/' && path.Last() != '\\') path += "/";
            var es = File.ReadAllLines(path + "en.txt");
            var rs = File.ReadAllLines(path + "ru.txt");
            var ss = new List<string>();
            var wRe = new Regex(@"[a-zA-Z]+");
            for (var i = 0; i < es.Length; i++) {
                es[i] = es[i].Replace("<", "&lt;");
                rs[i] = rs[i].Replace("<", "&lt;");
                es[i] = freqGrouping(es[i]);
                ss.Add(es[i]);
                ss.Add(rs[i]);
            }
            ss = ss.Select((s, i) => $"<tr><td>{s}</td>{(i % 2 == 1 ? "" : "<td class=trn rowspan=2>>>></td>")}</tr>").ToList();
            ss.Insert(0, "<meta charset='utf-8'><style> body { margin: 0; } tr:nth-child(even) { background-color: #CCC; } table { width: 100%; } td { padding: 3pt; } tr:nth-child(odd) td { padding-top: 15pt; } .trn { vertical-align: top; } </style>");
            ss.Insert(1, @"<script src='https://code.jquery.com/jquery-3.7.0.js'></script>
                <script>
                    $(document).on('click', '.trn', function() {
                        var s = $(this).closest('tr').find('td:first').text().toLowerCase();
                        window.open('https://www.deepl.com/translator#en/ru/' + s);
                    });
                </script>");
            ss.Insert(2, "<table>");
            ss.Add("</table>");
            File.WriteAllLines(path + "result.html", ss);
        }

        static void toAscii(string path) {
            var rps = new string[] { ""
                                   , "" };
            var rpDic = rps[0].Select((x, i) => (k: rps[0][i], v: rps[1][i])).ToDictionary(x => x.k, x => x.v);

            var s = new string(File.ReadAllText(path).Select(x => rpDic.TryGetValue(x, out var nx) ? nx : x).ToArray());
            s = Regex.Replace(s, @"[\x00-\x09\x0b-\x0c\x0e-\x1f]", " ");
            s = Regex.Replace(s, @" +", " ");
            s = Regex.Replace(s, @"\r?\n | \r?\n", "\r\n");
            s = Regex.Replace(s, @"\.( ?\.)+", "...");
            File.WriteAllText(pathEx(path, "-2"), s);
        }

        static byte[] speechKit(string s) {
            var iamToken = config["speechKitKey"];
            var folderId = "b1gc2cklho9c16s0h2pj";

            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", "Bearer " + iamToken);
            var values = new Dictionary<string, string> {
                { "text", s },
                { "lang", "en-US" },
                { "voice", "john" },
                { "folderId", folderId }
            };
            var content = new FormUrlEncodedContent(values);

            var task1 = Task.Run(() => client.PostAsync("https://tts.api.cloud.yandex.net/speech/v1/tts:synthesize", content));
            task1.Wait();
            var response = task1.Result;

            var task2 = Task.Run(() => response.Content.ReadAsByteArrayAsync());
            task1.Wait();
            return task2.Result;
        }

        static string[] edgeVocs = new[] { "Ava", "Andrew", "Emma", "Brian", "Ana", "Aria", "Christopher", "Eric", "Guy", "Jenny", "Michelle", "Roger", "Steffan" };

        static byte[] edgeTts(string s, string v) {
            var httpClient = new HttpClient();
            var json = $"{{ \"input\": \"{s}\", \"voice\": \"en-US-{v}Neural\", \"response_format\": \"mp3\", \"speed\": 1.0 }}";

            var url = $"http://localhost:5050/v1/audio/speech";
            var request = WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            var data = Encoding.UTF8.GetBytes(json);
            request.ContentLength = data.Length;
            using (var stream = request.GetRequestStream()) {
                stream.Write(data, 0, data.Length);
            }
            var r = (string)null;

            using (var response = request.GetResponse())
            using (Stream dataStream = response.GetResponseStream())
            using (var memStream = new MemoryStream()) {
                dataStream.CopyTo(memStream);
                return memStream.ToArray();
            }
        }

        static void run(string exe, string ps) {
            Process p = new Process();
            p.StartInfo.FileName = exe;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardInput = true;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.Arguments = ps;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            p.StartInfo.CreateNoWindow = true;
            p.EnableRaisingEvents = true;
            p.ErrorDataReceived += runReceived;
            p.OutputDataReceived += runReceived;
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
        }

        static void runReceived(object sender, DataReceivedEventArgs e) {
            if (!string.IsNullOrEmpty(e.Data)) {
                Console.Write(e.Data);
            }
        }

        static void srtOcr(string path) {
            var mPs = string.Join(" ", Enumerable.Range(1, 20).Select(i => $"-map -0:s:{i}"));
            path = path.Replace("\\", "/");
            var ptn = path.Substring(path.LastIndexOf('/') + 1);
            path = path.Substring(0, path.LastIndexOf('/'));
            var fs = Directory.GetFiles(path, ptn);
            foreach (var f in fs) {
                var f2 = pathEx(f, "-2");
                run(@"d:\Portables\ffmpeg\bin\ffmpeg.exe", $" -i {f} -map 0 {mPs} -c copy {f2}");
                if (!File.Exists(f2))
                    throw new Exception();
                File.Delete(f);
                File.Move(f2, f);
            }
            foreach (var f in fs) {
                run(@"d:\Portables\SubtitleEdit\SubtitleEdit.exe", $" /convert {f} srt /RemoveFormatting");
            }
        }

        static void serRename(string dir) {
            var re = new Regex(@".*?S(\d+).*?E(\d+).*");
            Directory.GetFiles(dir).Select(x => Path.GetFileName(x)).ToList().ForEach(f => {
                var ext = Path.GetExtension(f);
                var nf = handleString(f, re, (x, m) => {
                    var sn = int.Parse(m.Groups[1].Value);
                    var en = int.Parse(m.Groups[2].Value);
                    return $"S{sn.ToString("00")}E{en.ToString("00")}{ext}" ;
                });
                File.Move(Path.Combine(dir, f), Path.Combine(dir, nf));
                //Console.WriteLine(Path.Combine(dir, nf));
            });
        }

        static void fixOcr(string path) {
            var os = @"УКЕНХВАРОСМТукехаросвтДЛÓẤÉÄÒÍÔÃÚÑÁІỆỚÊẺʼ»„";
            var ns = @"YKEHXBAPOCMTykexapocBTAAOAEAOIOAUNAIEOEE'""""";
            var dic = os.Select((c,i) => (os[i], ns[i])).ToDictionary(x => x.Item1, x => x.Item2);
            var s = File.ReadAllText(path);
            s = new string(s.Select(c => dic.ContainsKey(c) ? dic[c] : c).ToArray());
            File.WriteAllText(path, s);
        }

        static Regex nsPunctRe = new Regex(@"\s*(\.\.\.|[!?,\.:;])\s*", RegexOptions.Compiled);
        static Regex nsDotsRe = new Regex(@"\.\.+", RegexOptions.Compiled);
        static Regex nsAmpRe = new Regex(@"[´`]", RegexOptions.Compiled);
        static Regex nsHypRe = new Regex(@"[–—]", RegexOptions.Compiled);
        static Regex nsTuneRe = new Regex(@"[♫♬♪]", RegexOptions.Compiled);
        static Regex nsSpaceRe = new Regex(@"\s+", RegexOptions.Compiled);

        static string normSent(string s) {
            s = nsAmpRe.Replace(s, "'");
            s = nsHypRe.Replace(s, "-");
            s = nsTuneRe.Replace(s, " ");

            s = nsDotsRe.Replace(s, "...");
            s = handleString(s, nsPunctRe, (x,m) => x.Trim() + " ").Trim();
            s = nsSpaceRe.Replace(s, " ");
            return s;
        }


        #region md

        static Regex enRe = new Regex("[A-Za-z]+", RegexOptions.Compiled);
        static Regex ruRe = new Regex("[А-яа-я]+", RegexOptions.Compiled);

        static int enru(string s) {
            s = Tag.Clear(s);
            
            var en = enRe.Matches(s).Cast<Match>().Select(m => m.Value.Length).Sum();
            var ru = ruRe.Matches(s).Cast<Match>().Select(m => m.Value.Length).Sum();

            var sum = Math.Max(1, en + ru);
            return (en * 100 / sum) - (ru * 100 / sum);
        }

        static Regex hRe = new Regex(@"h\d", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static void mdHandle(string path) {
            var rs2 = new List<string[]>();
            var pSet = new HashSet<string> { "p", "common" };
            File.ReadAllLines(path).ToList().ForEach(s => {
                if (s.Trim() == "") return;
                var b = enru(s);
                var cmn = s.Contains("<common/>");
                var sm = Tag.Clear(Markdown.ToHtml(s), pSet);
                if (b >= 0 || cmn) rs2.Add(new[] { sm, null });
                if (b <= 0 || cmn) rs2[rs2.Count - 1][1] = sm;
            });

            var contents = new List<string>();
            for (var i = 0; i < rs2.Count; i++) {
                var s = rs2[i][0];
                var h = Tag.Parse(s).Where(t => hRe.IsMatch(t.name)).Select(t => t.name).FirstOrDefault();
                if (h == null) continue;
                contents.Add($"<p class=\"indent{h[1]}\"><a href=\"#ref{contents.Count}\"><b>{Tag.Clear(s)}</b></a></p>");
                s = $"{s}<a name=\"ref{contents.Count - 1}\"></a>";
                rs2[i][0] = s;
            }

            var rs = File.ReadAllLines(@"d:\Projects\smalls\book-en.html").ToList();
            rs.AddRange(contents);
            rs.AddRange(rs2.Select(x => Tag.Clear(x[0]) == Tag.Clear(x[1]) ? $"<div class=\"columns1\"><div>{freqGrouping(x[0])}</div></div>" : $"<div class=\"columns2\"><div>{freqGrouping(x[0])}</div><div>{x[1]}</div></div>"));
            var name = Path.GetFileNameWithoutExtension(path);
            File.WriteAllLines($"d:/{name}.html", rs);
        }

        private static Queue<CancellationTokenSource> delayCtsQue = new Queue<CancellationTokenSource>();
        private static Task lastChangeTask = Task.Run(() => { });

        private static void mdMonitor() {
            using (var watcher = new FileSystemWatcher(@"d:/Projects/smalls", "*.md"))
            using (var watcher2 = new FileSystemWatcher(@"d:/Projects/private", "*.md"))
            {
                watcher.EnableRaisingEvents = watcher2.EnableRaisingEvents = true;
                FileSystemEventHandler cb = (object sender, FileSystemEventArgs e) => {
                    if (e.ChangeType != WatcherChangeTypes.Changed || e.Name[0] == '~') {
                        return;
                    }

                    while (delayCtsQue.Count > 0) {
                        var cts = delayCtsQue.Dequeue();
                        cts.Cancel();
                        cts.Dispose();
                    }

                    var delayCts = new CancellationTokenSource();
                    delayCtsQue.Enqueue(delayCts);

                    lastChangeTask = lastChangeTask.ContinueWith(async t => {
                        await Task.Delay(1000, delayCts.Token);
                        if (delayCts.IsCancellationRequested) return;

                        mdHandle(e.FullPath);
                        Console.WriteLine(e.FullPath);
                    });
                };

                watcher.Changed += cb;
                watcher2.Changed += cb;

                Console.WriteLine("Press enter to exit.");
                Console.ReadLine();
            }
        }

        #endregion

        #region gemini adapt

        static void geminiSplit(string path) {
            var ss = File.ReadAllLines(path);
            var sz = 0;
            var rs = new List<string>();
            var rs1 = new List<string>();
            var rs2 = new List<string>();

            var gt4k  = ss.Where(s => s.Length > 4000).ToList();
            gt4k.ForEach(s => {
                Console.WriteLine($"{s.Length} {s.Substring(0,30)}");
            });
            if (gt4k.Count > 0) return;

            foreach (var s in ss) {
                if (s.ToLower().StartsWith("chapter ") || s == "***") {
                    rs.AddRange(rs1);
                    rs.AddRange(rs2);
                    if (rs1.Count > 0 || rs2.Count > 0) rs.Add("---");
                    rs1.Clear();
                    rs2.Clear();
                }

                if (rs1.Count > 0 && (rs1.Sum(x => x.Length) + rs2.Sum(x => x.Length) + s.Length > 4000)) {
                    rs.AddRange(rs1);
                    rs1.Clear();
                    rs.Add("---");
                }
                if (rs2.Count > 0 && (rs2.Sum(x => x.Length) + s.Length > 4000)) {
                    if (rs1.Count != 0) throw new Exception(); 
                    rs.AddRange(rs2);
                    rs2.Clear();
                    rs.Add("---");
                }
                rs2.Add(s);
                if (s.Length > 200) {
                    rs1.AddRange(rs2);
                    rs2.Clear();
                }
            }
            rs.AddRange(rs1);
            rs.AddRange(rs2);
            if (rs1.Count > 0 || rs2.Count > 0) rs.Add("---");

            File.WriteAllLines(pathEx(path, "-split"), rs);
        }

        static void geminiAdapt(string path) {
            var pathRu = pathEx(path, "-adapt");
            if (!File.Exists(pathRu)) File.WriteAllText(pathRu, "");
            var skip = File.ReadAllText(pathRu).Split(new[] { "\r\n---\r\n" }, ssop).Length;
            var ss = File.ReadAllText(path).Split(new[] { "\r\n---\r\n" }, ssop).Skip(skip).ToArray();
            var i = 0;
            foreach (var s in ss) {
                //var r = "Адаптируй текст для понимания на B1-уровне знания английского, верни только результат:\r\n" + s;
                //var r = "Замени низкочастотные слова на высокочастотные синонимы B2-уровня знания английского и верни только результат:\r\n" + s;
                //var r = "Замени редкоупотребляемые слова на частоупотребляемые синонимы B2-уровня знания английского, а также адаптируй текст для B1-уровня и верни только результат:\r\n" + s;

                var r = "";
                r = "Адаптируй текст для понимания на B1-уровне знания английского, а также ";
                r += "замени, по возможности, слова за пределами B2-уровня знания английского на частоупотребляемые синонимы, верни только результат:\r\n" + s;

                var s2 = (string)null;
                do {
                    try {
                        s2 = gemini(r);
                    }
                    catch (Exception e) {
                        Console.WriteLine(e.Message);
                        Thread.Sleep(10000);
                    }
                 } while (s2 == null);
                Console.WriteLine(i++);
                File.AppendAllText(pathRu, Regex.Replace(s2 + "\r\n---", @"(\r?\n)+", "\r\n") + "\r\n");
            }
        }

        #endregion

        static volatile bool ctrlC = false;

        static void Main(string[] args) {
            Console.CancelKeyPress += (o, e) => { ctrlC = true; e.Cancel = true; };

            //geminiSplit(@"d:\.temp\reader-9-orig.txt");
            //geminiAdapt(@"d:\.temp\reader-9.txt");

            mdMonitor(); return;

            /*
            var path = @"d:/Projects/private/dic.txt";
            var ss = File.ReadAllLines(path);
            var pn = ss.Count(s => s.StartsWith("+"));
            ss = ss.Select(s => Regex.Replace(s, @"\+?#\d+ ", "")).SelectMany(s => Regex.Split(s, "; ?")).ToArray();

            var rs = new List<string>();
            for (var i = 0; i < (ss.Length / 10) + 1; i++) {
                var r = $"{(i < pn ? "+" : "")}#{i+1} " + string.Join("; ",  ss.Skip(i * 10).Take(10).Where(x => x != ""));
                rs.Add(r);
            }

            File.WriteAllLines(path, rs);
            */

            /*
            // |,[,/
            foreach (var ss in srtHandle(@"d:\.temp\srt\all.srt")) {
                foreach (var s in ss.Skip(2)) {
                    if (s.Contains("1")) {
                        Console.WriteLine(s);
                    }
                }
            }
            */
            //srtOcr(@"d:\.temp\simps-tor\1\*.mp4");
            //serRename(@"e:\scooby");

            /*
            if (!File.Exists(@"d:\.temp\srt\all.srt")) 
                srtCombine(@"d:\.temp\srt\");
            srtLine(@"d:\.temp\srt\all.srt");

            srtLearn(@"d:\.temp\srt\all.srt");
            srtSplit(@"d:\.temp\srt\all.srt", "eng");
            if (File.Exists(@"d:\.temp\srt\all-ru.srt"))
                srtSplit(@"d:\.temp\srt\all-ru.srt", "rus");
            */

            //prepareWords("d:/words.txt");
            //fixQuotes(@"d:\.temp\7.txt");
            //deepl(@"d:\.temp\st\S01E01[eng]-clear.srt");
            //File.WriteAllLines("d:/3.txt", posReduce("d:/.temp/3.txt ").Where(x => x.p != "пробел" && x.p != "прочее").Select(x => $"{x.w} {x.p}"));
            //var s = gemini(File.ReadAllText("d:/1.txt"));
            //toAscii(@"d:\.temp\3.txt");
            //learnStat($"d:/.temp/4.txt");
            //makeTip($"d:/.temp/4.txt");

            /*
            var name = "S01E05";
            srtLine($"d:/.temp/srt/{name}[eng]-orig.srt");
            srtClear($"d:/.temp/srt/{name}[eng].srt");
            learnStat($"d:/.temp/srt/{name}[eng]-clear.srt");
            makeTip($"d:/.temp/srt/{name}[eng]-clear.srt", true);
            srtCombile($"d:/.temp/srt/{name}[eng]-clear-tip.srt", $"d:/.temp/srt/{name}[eng].srt");
            */

            //comicOcr(@"d:\.temp\comics-ocr\");
            //comicOcrPost(@"d:\.temp\comics-ocr\", 10, 3); // 20,5 archie
            //fixOcr(@"d:\.temp\comics-ocr\en.txt");
            //deeplSplit(@"d:\.temp\comics-ocr\en.txt");
            //comicComplete(@"d:\.temp\comics-ocr\");

            Console.WriteLine("Press ENTER");
            Console.ReadLine();
        }
    }
}

/*
            // all words forms
            var lSet = new HashSet<string>(File.ReadAllLines(@"d:\Projects\smalls\dic-corpus.txt").Select(x => x.Split(' ')[0])
               .SelectMany(x => (lemmaForms.TryGetValue(x, out var xs) ? xs : Enumerable.Empty<string>()).Concat(Enumerable.Repeat(x, 1))));
 */

/*
            // conen.js
            var nRe = new Regex(@"^\d+ ", RegexOptions.Compiled);
            var ss = File.ReadAllLines(@"d:\Projects\smalls\freq-20k.txt")
                .Select(x => (i: int.Parse(nRe.Match(x).Value.Trim()), s: nRe.Replace(x, "")))
                .Where(x => x.i >= 1000 && x.i <= 5000)
                .Select(x =>  @"""" + x.i.ToString("00000") + " " + x.s + @""",");
            File.WriteAllLines(@"d:\dic-2.js", ss);

*/


/*
// conen/
var path = @"d:\subs.txt";
var ss = File.ReadAllLines(path).Select(x => x).ToList();
var rs = Enumerable.Range(0, 202).Select(x => new StringBuilder()).ToArray();
//for (var i = 0; i < rs.Length; i++) rs[i] = "";
var n = -1;
foreach (var s in ss) {
    if (s.Contains("{")) {
        n = int.Parse(s.Substring(0, 3));
    }
    rs[n].Append("\r\n" + s);
}

for (var i = 0; i < 202; i++) {
    File.WriteAllText($"d:/Projects/smalls/conen/{i.ToString("000")}.txt", rs[i].ToString().Substring(2));
}
*/

/*
// словарь извесных слов для контекста
var path = @"d:\Projects\smalls\subs.txt";
var subs = File.ReadAllLines(path).Where(x => x.StartsWith("DIC: ")).Select(x => x.Substring(5)).Where(x => x.CompareTo("0500") > 0).ToList();
var dic = File.ReadAllLines(@"d:\Projects\smalls\freq-us.txt").ToDictionary(x => x.Split('{')[0].Trim(), x => x.Split('}')[1].Trim());
subs = subs.Select(x => {
    var key = x.Split('{')[0].Trim();
    if (dic.TryGetValue(Regex.Replace(key, @"^0+", "") , out var val)) {
        x += $" {val}";
    }
    if (learnDic.ContainsKey(x.Substring(5).Split('}')[0] + "}") || x.CompareTo("3001") > 0) {
        x = "// " + x;
    }
    return x;
}).ToList();
File.WriteAllLines(@"d:/conen.txt", subs);
*/


/*
            // PRONUNCES
            var dic = allForms(existingWords.Keys).ToDictionary(x => x, x => (string)null);

            File.ReadAllLines("d:/index.txt").ToList().ForEach(x => {
                if (x.Contains("("))
                    return;

                var i = x.IndexOf(' ');
                var k = x.Substring(0, i);
                var v = x.Substring(i + 1);
                if (!dic.ContainsKey(k))
                    return;

                var sb = new StringBuilder();
                v.Split(' ').ToList().ForEach(y => sb.Append(cmu[y]));
                dic[k] = sb.ToString();
            });

            File.WriteAllLines("d:/pronun.txt", dic.Where(kv => kv.Value != null).OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}"));

 */

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