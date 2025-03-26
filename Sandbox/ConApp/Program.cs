﻿using System;
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
using Microsoft.VisualBasic.FileIO;

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

    public static class EnumHelper {
        static Random rnd = new Random();

        public static IEnumerable<T> Random<T>(this IEnumerable<T> _this) { // where T : class
            var xs = _this.ToList();
            var rs = new T[xs.Count * 4];
            xs.ForEach(x => {
                var i = -1;
                do {
                    i = rnd.Next(rs.Length);
                } while (rs[i] != null);
                rs[i] = x;
            });
            
            return rs.Where(x => x != null);
        }
    }

    public class PosToken {
        public int characterOffsetBegin { get; set; }
        public int characterOffsetEnd { get; set; }
        public string pos { get; set; }
    }

    public class Pos {
        public string s { get; set; }
        public string pos { get; set; }

        public override string ToString() {
            return pos == null ? s : $"{s} {{{pos}}}";
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
            get => _freqGroups ?? (_freqGroups = loadFreqGroups(!config.ContainsKey("freqGroupsPath") ? @"d:\Projects\smalls\freq-20k.txt" : config["freqGroupsPath"], config["freqGroups"].Split(',').Select(x => int.Parse(x)).ToArray()));
        }

        static Dictionary<string, int> _freqGroups;

        static Regex dicRankRe = new Regex(@"^\d+ ", RegexOptions.Compiled);

        static Dictionary<string, int> loadFreqGroups(string path, int[] levels) {
            var r = new Dictionary<string, int>();
            var exsPath = pathEx(path, "-excepts");
            var exs = new HashSet<string>();
            if (File.Exists(exsPath)) {
                File.ReadAllLines(exsPath).ToList().ForEach(x => exs.Add(x));
            }
            File.ReadAllLines(path).Where(x => x.Contains(" {")).Select((x,i) => (x: x, i: i+1)).Reverse().ToList().ForEach(xi => {
                var x = xi.x;
                var i = xi.i;
                var rm = dicRankRe.Match(x);
                if (rm.Success) {
                    x = x.Substring(rm.Value.Length);
                    i = int.Parse(rm.Value.Trim());
                }
                if (i > levels.Last()) return;
                if (x.Contains("{междометие}")) return;

                var w = x.Split(new[] { " {" }, ssop)[0].ToLower();
                if (exs.Contains(w)) return;

                var g = 0;
                while (i > levels[g]) {
                    g++;
                }
                getLemmaForms(w, true).ToList().ForEach(f => {
                    if (f.Length == 1) return;
                    if (g == 0) {
                        r.Remove(f);
                    }
                    else {
                        r[f] = g - 1;
                    }
                });
            });
            return r;
        }

        static Regex freqGoupRe = new Regex(@"[a-z]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static string[][] freqGroupColors = new[] { new [] { "#ffbc8c", "#99ff99", "#99cbff", "#b399ff" }, new[] { "#cc5400", "#00b200", "#0079ff", "#8000ff" } };
        static string freqGrouping(string s, bool white = false, Dictionary<string,int> dic = null) {
            dic = dic ?? freqGroups;
            if (Tag.Parse(s).Length > 0) return s;
            var cs = freqGroupColors[white ? 0 : 1];
            s = handleString(s, freqGoupRe, (x, m) => {
                if (dic.TryGetValue(x.ToLower(), out var g)) {
                    return $"<font color=\"{cs[g]}\">{x}</font>";
                }
                return x;
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
            { "NN", "существительное" }, { "NNS", "существительное" }, { "NNP", "имя" },
            { "NNPS", "имя" }, { "JJ", "прилагательное" }, { "JJR", "прилагательное" },
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
            File.ReadAllLines(path).Where(x => x.Contains(" {")).Select(x => {
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

        static string gemini(string s, byte[] img = null) {
            s = s.Replace(@"\", @"\\").Replace(@"""", @"\""").Replace("\r", @"\r").Replace("\n", @"\n");
            var url = $"https://generativelanguage.googleapis.com/v1/models/gemini-2.0-flash:generateContent?key={config["geminiKey"]}";
            var request = WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            var base64 = img == null ? "" : $",{{\"inline_data\":{{\"mime_type\":\"image/jpeg\",\"data\":\"{Convert.ToBase64String(img)}\"}}}}";
            s = $"{{\"contents\":[{{\"parts\":[{{\"text\":\"{s}\"}}{base64}]}}]}}";
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

        static string deepseek(string s) {
            s = s.Replace(@"\", @"\\").Replace(@"""", @"\""").Replace("\r", @"\r").Replace("\n", @"\n");
            var url = $"https://api.deepseek.com/chat/completions";
            var request = WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            s = $"{{\"model\":\"deepseek-chat\",\"messages\":[{{\"role\":\"user\",\"content\":\"{s}\"}}],\"stream\":false}}";
            var data = Encoding.UTF8.GetBytes(s);
            request.ContentLength = data.Length;
            request.ContentType = "application/json";
            request.Headers.Add("Authorization", $"Bearer {config["deepseekKey"]}");
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
            var i = 0;
            var ns = Directory.GetFiles(path, "*.*").Where(x => x.EndsWith(".jpg") || x.EndsWith(".jpeg")).Select(x => Path.GetFileNameWithoutExtension(x)).OrderBy(p => p).ToArray();
            var brRe = new Regex(@"[\.!?]+[""']|[\.!?]+");
            foreach (var n in ns) {
                i++;
                rs.Add("* * * * " + n + " (" + i + ")");
                var ps = (new[] { $"{path}{n}.txt", $"{path}{n}.json" }).Where(x => File.Exists(x)).ToArray();
                foreach (var p in ps) {
                    if (p.EndsWith(".txt")) {
                        var s = string.Join(" ", File.ReadAllLines(p));
                        var ss = handleString(s, brRe, (x,m) => $"{x}\n").Split('\n').Select(x => x.Trim()).Where(x => x != "").ToArray();
                        rs.AddRange(ss);
                    }
                    else if (p.EndsWith(".json")) {
                        var or = JsonConvert.DeserializeObject<OcrRootObject>(File.ReadAllText(p));
                        var rects = or.GetRects().ToArray();
                        var r = OcrRect.Group(rects, or.Width, fsize, indent);
                        rs.AddRange(r.Select(x => string.Join(" ", x.Select(y => y.Text))));
                    }
                }
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

        static Queue<(string k, Dictionary<string, int> d)> mdCache = new Queue<(string k, Dictionary<string, int> d)>();

        static T getDicVal<T>(string key, T def, params Dictionary<string, T>[] ds) {
            foreach (var d in ds) {
                if (d.TryGetValue(key, out var r)) return r;
            }
            return def;
        }

        static void mdHandle(string path) {
            var rs2 = new List<string[]>();
            var pSet = new HashSet<string> { "p", "common" };
            var ss = File.ReadAllLines(path).ToList();

            #region config

            var confTag = ss.Take(10).SelectMany(x => Tag.Parse(x)).Where(t => t.name.ToLower() == "config").FirstOrDefault() ?? new Tag() { name = "config" };
            var freqGroupsPath = getDicVal("freqGroupsPath", "d:/Projects/smalls/freq-20k.txt", confTag.attr, config);
            var freqGroups = getDicVal("freqGroups", "1500,2500,3800,5500,11000", confTag.attr, config).Split(',').Select(x => int.Parse(x)).ToArray();
            var mdPostStr = getDicVal("mdPost", null, confTag.attr, config);

            var post = mdPostStr == null ? x => x : (Func<IEnumerable<string>,IEnumerable<string>>)Delegate.CreateDelegate(
                typeof(Func<IEnumerable<string>, IEnumerable<string>>), null,
                typeof(Program).GetMethod(mdPostStr, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public));

            var cacheKey = $"{freqGroupsPath}|{string.Join(",", freqGroups)}|{File.GetLastWriteTime(freqGroupsPath).ToString("s")}";
            var freqDic = mdCache.Where(x => x.k == cacheKey).FirstOrDefault().d;
            if (freqDic == null) {
                freqDic = loadFreqGroups(freqGroupsPath, freqGroups);
                mdCache.Enqueue((cacheKey, freqDic));
                while (mdCache.Count > 5) mdCache.Dequeue();
            }

            #endregion

            ss.ForEach(s => {
                if (Tag.Clear(s).Trim() == "") return;

                var b = enru(s);
                var cmn = s.Contains("<common/>");
                var sm = Tag.Clear(s, pSet);
                if (b >= 0 || cmn) rs2.Add(new[] { sm, "" });
                if (b <= 0 || cmn) rs2[rs2.Count - 1][1] = sm;
            });

            var ps = post(rs2.Select(x => x[0])).ToList();
            ps = ps.Select(p => freqGrouping(p, dic: freqDic)).ToList(); 
            for (var i = 0; i < ps.Count; i++) {
                rs2[i][0] = ps[i];
            }

            rs2.ForEach(x => {
                x[0] = Tag.Clear(Markdown.ToHtml(x[0]), pSet);
                x[1] = Tag.Clear(Markdown.ToHtml(x[1]), pSet);
            });

            var contents = new List<string>();
            for (var i = 0; i < rs2.Count; i++) {
                var s = rs2[i][0];
                var h = Tag.Parse(s).Where(t => hRe.IsMatch(t.name)).Select(t => t.name).FirstOrDefault();
                if (h == null) continue;
                contents.Add($"<p class=\"indent{h[1]}\"><a href=\"#ref{contents.Count}\"><b>{Tag.Clear(s)}</b></a></p>");
                s = $"<a name=\"ref{contents.Count - 1}\"></a>{s}";
                rs2[i][0] = s;
            }

            var isOne = !rs2.Any(x => enru(x[1]) < 0);

            var rs = File.ReadAllLines(@"d:\Projects\smalls\book-en.html").ToList();
            rs.AddRange(contents);
            rs.AddRange(rs2.Select(x => isOne || Tag.Clear(x[0]) == Tag.Clear(x[1]) ? new [] { x[0] } : x)
                .Select(x => $"<div class=\"columns{x.Length}\">{string.Join("", x.Select(y => $"<div>{y}</div>"))}</div>"));
            var name = Path.GetFileNameWithoutExtension(path);
            File.WriteAllLines($"d:/{name}.html", rs);
        }

        private static Queue<CancellationTokenSource> delayCtsQue = new Queue<CancellationTokenSource>();
        private static Task lastChangeTask = Task.Run(() => { });

        public static IEnumerable<string> mdPostCom(IEnumerable<string> xs) {
            var wRe = new Regex(@"[a-zA-Z<]+", RegexOptions.Compiled);
            var ss = xs.ToList();
            var wi = -1;
            for (var i = 0; i < ss.Count; i++) {
                var s = ss[i];
                if (s.StartsWith("WORDS: ")) {
                    s = handleString(s, wRe, (x,m) => {
                        if (x == "WORDS" || x.StartsWith("<")) return x;
                        return $"**{x}**";
                    });
                    wi = i;
                }
                else if (wi >= 0) {
                    wRe.Matches(s).Cast<Match>().Select(m => m.Value).ToList().ForEach(x => {
                        x = x.ToLower();
                        var xfs = new[] { x, getLemmaBase(x) };
                        foreach (var xf in xfs) {
                            ss[wi] = ss[wi].Replace($"**{xf}**", xf);
                        }
                    });
                }
                ss[i] = s;
            }
            return ss;
        }
        

        private static void mdMonitor(Func<IEnumerable<string>,IEnumerable<string>> post = null) {
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

        static Regex adaptKeepRe = new Regex(@"^((Chapter|#+) [^\r\n]+|_)\r?\n", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static void geminiSplit(string path) {
            var ss = File.ReadAllLines(path);
            var sz = 0;
            var rs = new List<string>();
            var rs1 = new List<string>();
            var rs2 = new List<string>();
            var hRe = new Regex(@"^#+ ");

            var gt4k  = ss.Where(s => s.Length > 4000).ToList();
            gt4k.ForEach(s => {
                Console.WriteLine($"{s.Length} {s.Substring(0,30)}");
            });
            if (gt4k.Count > 0) return;

            foreach (var s in ss) {
                if (adaptKeepRe.IsMatch($"{s}\n")) {
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

                var pre = adaptKeepRe.Matches(s).Cast<Match>().Select(m => m.Value).FirstOrDefault() ?? "";
                var r = "";
                r = "Адаптируй текст для понимания на B1-уровне знания английского, а также ";
                r += "замени, по возможности, слова за пределами B2-уровня знания английского на частоупотребляемые синонимы, верни только результат:\r\n" + s.Substring(pre.Length);

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
                File.AppendAllText(pathRu, Regex.Replace(pre + s2 + "\r\n---", @"(\r?\n)+", "\r\n") + "\r\n");
            }
        }

        #endregion


        static string[] posAbrs = "арт гл мест нар пред прил сущ числ межд опр".Split(' ');
        static string[] posFulls = partAbbr.Keys.ToArray();
        static Regex posRe = new Regex(@"\{[^}]+\}", RegexOptions.Compiled);

        static string getPosAbr(string s, bool needPoint = false) {
            var isWorked = false;
            s = handleString(s, posRe, (x,m) => {
                x = x.Substring(1, x.Length - 2);
                var abr = posAbrs.FirstOrDefault(p => x.StartsWith(p));
                if (abr == null) return x;
                if (needPoint) abr += ".";
                isWorked = true;
                return $"{{{abr}}}";
            });
            if (isWorked) return s;
            var abr2 = posAbrs.FirstOrDefault(p => s.StartsWith(p));
            if (abr2 == null) return s;
            if (needPoint) abr2 += ".";
            return abr2;
        }

        static string getPosAbrBack(string s, bool needPoint = false) {
            var s2 = handleString(s, posRe, (x, m) => {
                x = x.Substring(1, x.Length - 2);
                if (x.EndsWith(".")) {
                    x = x.Substring(0, x.Length - 1);
                }

                var abr = posFulls.FirstOrDefault(p => p.StartsWith(x));
                if (abr == null) return x;
                if (needPoint) abr += ".";
                return $"{{{abr}}}";
            });

            if (s2 != s) return s2;

            if (s.EndsWith(".")) {
                s = s.Substring(0, s.Length - 1);
            }

            var abr2 = posFulls.FirstOrDefault(p => p.StartsWith(s));
            if (abr2 == null) return s;
            return abr2;
        }


        static void genStories(string path, int n) {
            //var tpl = "Придумай небольшую историю на английском языке для самого базового уровня знания английского в ВИДЕ с сюжетом основанном на СЮЖЕТЕ, с использованием слов из списка, выделив их жирным в итоговом тексте: СЛОВА. Затем дай перевод истории и также выдели жирным переведенные слова из списка. И никакой служебной информации, отделив текст от перевода только '---'.";
            // var tpl = "Придумай небольшой диалог на английском языке для самого элементарного уровня знания английского (A2), с использованием слов из списка, выделив их жирным в итоговом тексте: СЛОВА. Затем дай перевод и также выдели жирным переведенные слова из списка. И никакой служебной информации, отделив текст от перевода только '---'.";
            var tpl = "Придумай максимально короткую историю на английском языке для самого элементарного уровня знания английского (A2), с использованием слов из списка, выделив их жирным в итоговом тексте: СЛОВА. Затем дай перевод и также выдели жирным переведенные слова из списка. Верни только результат, отделив текст от перевода '---'.";
            var styles = new string[] { "стиле космической фантастики", "стиле научной фантастики", "стиле фэнтези", "виде трагедии", "виде деловой драмы", "виде деловой драмы" };
            var plots = new string[] { "спасении", "мести", "преследовании", "бедствии", "исчезновении", "жертве", "мятеже", "похищении", "загадке", "достижении", "ненависти", "соперничестве", "адюльтере", "безумии", "убийстве", "самопожертвовании", "честолюбии", "открытии", "выживании", "испытании", "дружбе", "находке" };

            var xs = File.ReadAllLines(path);
            var ws = xs.Select(x => Regex.Split(x, @" *[\[\]\{\}] *").ToList()).ToList();
            var dic = loadDic(@"d:\Projects\smalls\freq-20k.txt");
            ws.ForEach(w => {
                var k = $"{w[0]} {{{w[1]}}}";
                if (w.Count == 3) {
                    w.Add(w[2]);
                    w[2] = "-";
                }
                if (w[2] == "-" && dic.ContainsKey(k)) {
                    var v = dic[k];
                    var m = Regex.Match(v, @"\[([^\]]*)\]");
                    if (m.Success) {
                        w[2] = m.Groups[1].Value;
                    }
                }
                var posAbr = getPosAbr(w[1], true);

                w.Add(posAbr);
            });

            var rs = new List<string>();
            var rnd = new Random();
            var si = 1;
            for (var i = 0; i < n; i++) {
                ws = ws.Random().ToList();
                var gws = ws.Select((x, j) => (x: x, i: j)).GroupBy(x => x.i / 10).Select(x => x.Select(y => y.x).ToList()).ToList();
                var gwsL = gws.Last();
                if (gwsL.Count < 10) {
                    for (var j = 0; j < gwsL.Count; j++) {
                        gws[j].Add(gwsL[j]);
                    }
                    gws.Remove(gwsL);
                }

                foreach (var gs in gws) {
                    rs.Add($"## #{si}<common/>");
                    rs.Add(string.Join("; ", gs.Select(g => $"**{g[0]}** [{g[2]}] *{g[4]}* {g[3]}")) + "<common/>");
                    var wStr = string.Join("; ", gs.Select(g => $"{g[0]} как {g[1]}: {g[3]}"));
                    var q = tpl.Replace("ВИДЕ", styles[rnd.Next(styles.Length)])
                        .Replace("СЮЖЕТЕ", plots[rnd.Next(plots.Length)])
                        .Replace("СЛОВА", wStr);

                    var qr = (string)null;
                    do {
                        try {
                            qr = gemini(q);
                        }
                        catch { Thread.Sleep(5000); }
                    } while (qr == null);

                    var qrs = Regex.Split(qr, @"\r?\n").Where(x => !x.StartsWith("#") && !Regex.IsMatch(x, @"^\*\*[^\*]*\*\*$")).ToArray();
                    var en = qrs.Where(x => enru(x) > 0).ToArray();
                    var ru = qrs.Where(x => enru(x) < 0).ToArray();
                    if (en.Length != ru.Length) throw new Exception();
                    for (var k = 0; k < en.Length; k++) {
                        rs.Add(en[k]);
                        rs.Add(ru[k]);
                    }
                    Console.WriteLine(si);
                    si++;
                }
            }

            File.WriteAllLines(pathEx(path, "-ss"), rs.Select(x => x + "\r\n"));
        }

        static IEnumerable<Pos> getPos(string str) {
            // "annotators":"tokenize,ssplit,pos","outputFormat":"json"
            var url = $"http://localhost:9000/?properties={{{WebUtility.UrlEncode(@"""annotators"":""tokenize,ssplit,pos"",""outputFormat"":""json""")}}}";

            var i = 0;
            foreach (var s in nSplit(str, 16 * 1024)) {
                if (i > 0) {
                    yield return new Pos() { s = "\r\n" };
                }
                i++;

                var request = WebRequest.Create(url);
                request.Method = "POST";
                using (var dataStream = request.GetRequestStream()) {
                    var d = Encoding.UTF8.GetBytes(s);
                    dataStream.Write(d, 0, d.Length);
                }

                var r = (string)null;
                using (var response = request.GetResponse())
                using (Stream dataStream = response.GetResponseStream()) {
                    var reader = new StreamReader(dataStream);
                    r = reader.ReadToEnd();
                }

                var b = 0;
                foreach (var t in JObject.Parse(r).SelectTokens("$..tokens").SelectMany(x => x).Select(x => x.ToObject<PosToken>())) {
                    if (!posTags.ContainsKey(t.pos)) continue;
                    var p = posTags[t.pos];
                    if (t.characterOffsetBegin != b) {
                        yield return new Pos() { s = s.Substring(b, t.characterOffsetBegin - b) };
                    }

                    yield return new Pos() { s = s.Substring(t.characterOffsetBegin, t.characterOffsetEnd - t.characterOffsetBegin), pos = p };
                    b = t.characterOffsetEnd;
                }

                if (b != s.Length) {
                    yield return new Pos() { s = s.Substring(b, s.Length - b) };
                }
            }
        }

        static IEnumerable<string> nSplit(string str, int max) {
            var re = new Regex(@"\r?\n", RegexOptions.Compiled);
            var ss = re.Split(str);
            var sb = new StringBuilder();
            foreach (var s in ss) {
                var l = sb.Length + s.Length + (sb.Length == 0 ? 0 : 2);
                if (l > max) {
                    if (sb.Length > 0) {
                        yield return sb.ToString();
                        sb.Clear();
                    }
                    if (s.Length > max) {
                        yield return s;
                        continue;
                    }
                }
                if (sb.Length > 0) {
                    sb.Append("\r\n");
                }
                sb.Append(s);
            }

            if (sb.Length > 0) {
                yield return sb.ToString();
            }
        }

        static IEnumerable<T> mergeEven<T>(IEnumerable<T> ae, IEnumerable<T> be) {
            var a = ae.ToList();
            var b = be.ToList();

            if (a.Count < b.Count) {
                var t = a; a = b; b = t;
            }

            if (b.Count == 0) {
                foreach (var x in a) yield return x;
            }

            var s = (float)a.Count / b.Count;
            var i = 0;
            var av = 0.5;
            var bv = s / 2;
            foreach (var bx in b) {
                while (av < bv) {
                    yield return a[i++];
                    av += 1;
                }
                bv += s;
                yield return bx;
            }

            for (; i < a.Count; i++)
                yield return a[i];
        }

        static string dicFind(Dictionary<string, string> d, string s) {
            s = s.ToLower();
            if (d.ContainsKey(s)) return s;
            s = getLemmaBase(s);
            if (d.ContainsKey(s)) return s;
            return null;
        }

        static void geminiComicOcr(string path) {
            if (path.Last() != '/' && path.Last() != '\\') path += "/";
            var rs = new List<string>();
            var ps = Directory.GetFiles(path, "*.*").Where(x => x.EndsWith(".jpg") || x.EndsWith(".jpeg")).OrderBy(p => p).ToArray();
            foreach (var p in ps) {
                var name = Path.GetFileNameWithoutExtension(p);
                var txtPath = $"{path}{name}.txt";
                if (File.Exists(txtPath)) {
                    continue;
                }
                var data = File.ReadAllBytes(p);
                var r = (string)null;
                while (r == null) {
                    try {
                        r = gemini("Recognize text from image. Return only result text.", data); // 
                    }
                    catch (Exception e) {
                        Console.WriteLine(e.Message);
                        Thread.Sleep(5000);
                    }
                }
                File.WriteAllText(txtPath, Regex.Replace(r, @"(\r?\n)+", "\r\n"));
                Console.WriteLine(name);
            }
        }

        static volatile bool ctrlC = false;

        static void Main(string[] args) {
            Console.CancelKeyPress += (o, e) => { ctrlC = true; e.Cancel = true; };

            /*
            var d20k = loadDic(@"d:\Projects\smalls\freq-20k.txt");
            var d100 = loadDic(@"d:\Projects\smalls\words-100.txt");
            var s = File.ReadAllText(@"d:\rs.txt");

            getPos(s).Where(x => x.pos != null).Select(x => dicFind(d20k, x.ToString()))
                .Where(x => x != null && !d100.ContainsKey(x))
                .GroupBy(x => x).Select(g => (k: g.Key, n: g.Count()))
                .OrderByDescending(x => x.n).ToList().ForEach(x => Console.WriteLine($"{x.k} {x.n}"));
            */
            //Console.WriteLine(getPosAbrBack("{сущ}"));

            /*
            // 18*6
            var p = "Придумай на английском маленький отрывок из диалога для детей с начальным уровнем знания английского (A1) мальчика с мамой (2 варианта), с папой (1 вариант) и с подружкой (2 варианта), все слова должны быть в диалоге. Слова: СЛОВА.";
            var path = @"d:\Projects\smalls\words-100.txt";
            var ss = File.ReadAllLines(path).ToList();
            var gs = ss.Select((x, i) => (x, i / 7)).GroupBy(x => x.Item2).Select(g => g.Select(x => x.Item1).ToList()).ToList();
            foreach (var g in gs) {
                var q = string.Join(", ", g.Select(x => Regex.Replace(x, @"\{.*?\}", "-")));
                q = p.Replace("СЛОВА", q);
                Console.WriteLine(q);
            }
            */

            //var ts = ss.Skip(108).ToList();
            //ss = ss.Take(ss.Count - ts.Count).ToList();

            /*
            var dic = ss.Select((s, i) => {
                var v = Regex.Match(s, @"[^\}]+\}").Value;
                return (v, $"{i.ToString("000000")} {s}");
            }).ToDictionary(x => x.Item1, x => x.Item2);

            File.ReadAllLines(@"d:/words-300-freq.txt").Select((s, i) => {
                var m = Regex.Match(s, @"(\d+) ([^\}]+\})");
                return ($"{m.Groups[2].Value}", int.Parse(m.Groups[1].Value));
            }).ToList().ForEach(x => {
                dic[x.Item1] = Regex.Replace(dic[x.Item1], @"^000", x.Item2.ToString("000"));
            });
            
            ss = dic.Select(x => x.Value).OrderByDescending(x => x).ToList();
            */


            /*
            var a = ss.Where(s => !s.Contains("#3")).ToList();
            var b = ss.Where(s => s.Contains("#3")).ToList();

            var at = a.Skip(50).ToList();
            a = a.Take(a.Count - at.Count).ToList();
            ss = mergeEven(a, b).Concat(at).ToList();
            */

            //genStories(@"d:/stories.txt", 5);

            //geminiSplit(@"d:\.temp\reader-16-orig.txt");
            //geminiAdapt(@"d:\.temp\reader-17.txt");

            mdMonitor(); return; // mdPostCom

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

            //geminiComicOcr(@"d:\.temp\comics-ocr\");
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
            // repeat words
            var ss = File.ReadAllLines(@"d:\Projects\smalls\words-cross.txt").Take(108).ToList();
            List<List<string>> bs = Enumerable.Repeat(0, 20).Select(x => new List<string>()).ToList();
            var gs = ss.Select((x, i) => (x, i / 18)).GroupBy(x => x.Item2).Select(g => g.Select(x => x.Item1).ToList()).ToList();

            for (var i = 0; i < gs.Count; i++) {
                var g = gs[i];
                foreach (var s in g) {
                    var n = s.Contains("#3") ? 3 : 2;
                    for (var j = 0; j < n; j++) {
                        bs[i + j].Add(s);
                    }
                }
            }

            bs = bs.Select(x => x.Random().ToList()).ToList();
            ss = bs.SelectMany(x => x).ToList();
            var a = ss.Where(s => !s.Contains("#s")).ToList();
            var b = ss.Where(s => s.Contains("#s")).ToList();
            ss = mergeEven(a, b).ToList();

            File.WriteAllLines(@"d:/words-100-list.txt", ss);

            // check nears
            ss = File.ReadAllLines(@"d:/words-100-list.txt").ToList();
            var q = new Queue<string>();
            for (var i = 0; i < ss.Count; i++) {
                var s = ss[i];
                if (q.Contains(s)) {
                    throw new Exception();
                }
                q.Enqueue(s);
                if (q.Count > 7) q.Dequeue();
            }
*/

/*
 // 300 words
            var a1 = loadDic(@"d:\.temp\Spotlight-3\words-cross.txt");
            var r = File.ReadAllLines(@"d:\Projects\smalls\freq-20k.txt").Select(s => {
                var m = Regex.Match(s, @"(\d+) ([^\}]+\})");
                return (int.Parse(m.Groups[1].Value), m.Groups[2].Value);
            }).ToDictionary(x => x.Value, x => x.Item1);
            var r2 = File.ReadAllLines(@"d:\Projects\smalls\freq-20k.txt").Select(s => {
                var m = Regex.Match(s, @"(\d+) ([^\}]+\}) (.+)");
                return (m.Groups[2].Value, m.Groups[3].Value);
            }).ToDictionary(x => x.Item1, x => x.Item2);

            r.Where(x => x.Value > 200 || a1.ContainsKey(x.Key)).ToList().ForEach(x => { r.Remove(x.Key); });
            r.ToList().ForEach(x => {
                Console.WriteLine($"{x.Value.ToString("00000")} {x.Key} {r2[x.Key]}");
            });

            //r.OrderBy(x => x.Value).ToList().ForEach(x => Console.WriteLine($"{x.Value.ToString("00000")} {x.Key} {Regex.Replace(r2[x.Key], @"\[[^\d]+\] ", "")}"));
            //a1.Keys.Where(x => !r.ContainsKey(x)).ToList().ForEach(Console.WriteLine);
 */

/*
            // Spotlight
            Func<string, string> getKey = x => x.Split(new[] { " {" }, ssop)[0];
            var s = File.ReadAllText(@"d:\.temp\Spotlight-3\1.txt");
            var exs = new HashSet<string>(File.ReadAllLines(@"d:\.temp\Spotlight-3\words-a1.txt").Select(x => getKey(x)));
            var ss = Regex.Matches(s, @"[a-z]+").Cast<Match>().Select(x => getLemmaBase(x.Value)).Where(x => exs.Contains(x)).ToList();

            var r = ss.GroupBy(x => x).Select(g => (k: g.Key, n: g.Count())).ToList();

            var sl = new HashSet<string>(
                ss.GroupBy(x => x).Select(g => (k: g.Key, n: g.Count())).Where(x => x.n > 1).Select(x => x.k));

            File.ReadAllLines(@"d:\.temp\Spotlight-3\words.txt").Where(x => sl.Contains(getKey(x))).Random().ToList().ForEach(Console.WriteLine);

 */

/*
            var rs = new List<string>();
            var path = @"d:\.temp\123\poetry.csv";
            using (TextFieldParser csvParser = new TextFieldParser(path)) {
                csvParser.CommentTokens = new string[] { "#" };
                csvParser.SetDelimiters(new string[] { "," });
                csvParser.HasFieldsEnclosedInQuotes = true;

                csvParser.ReadLine();
                var prevN = -1;
                var ss = new List<string>();
                while (!csvParser.EndOfData) {
                    string[] fields = csvParser.ReadFields();
                    var s = fields[0];
                    var n = int.Parse(fields[1]);
                    if (n == prevN) {
                        ss.Add(s);
                        continue;
                    }

                    prevN = n;

                    if (ss.Count == 0) continue;

                    var shC = ss.Count(x => x.Length <= 40) * 100 / ss.Count;
                    var capC = ss.Count(x => x.Length > 0 && char.IsUpper(x[0])) * 100 / ss.Count;
                    if (capC > 70 && shC > 70) {
                        rs.AddRange(ss);
                        rs.Add("");
                    }
                    ss.Clear();
                }
            }

            File.WriteAllLines(@"d:\poems.txt", rs);
 */

/*
            var rs = Enumerable.Range(1, 150).Select(x => $"<b>{x}-й день:</b> ").ToList();
            var its = new int[] { 0, 1, 3, 6, 11, 19 };

            for (var i = 0; i < 100; i++) {
                foreach (var it in its) {
                    rs[i + it] += $", {i + 1}";
                }
            }
            rs = rs.Where(x => x != "").ToList();
            rs.ForEach(x => { Console.WriteLine(x.Replace("b> , ", "b> ")); });
 */

/*
            var css =
@"<style>
  p, td { font-size: 16pt; }
  td { vertical-align: top; }
  table.main { width: 27.2cm; }
  td.num { width: 2cm; font-size: 28pt; }
  td.txt { width: 13.6cm; text-indent: 0.6cm; padding: 0pt 4pt; font-size: 16pt; }
  p { margin: 0pt; }
</style>";

            var ss = File.ReadAllLines(@"d:\Projects\private\stories.md").Where(x => x != "").ToList();
            var h = (string)null;
            var isPrevH = false;
            var isH = false;
            var rs = new List<string>();
            rs.Add(css);
            var clTags = new HashSet<string>() { "p" };
            ss.ForEach(s => {
                isPrevH = isH;
                isH = s.StartsWith("##");

                if (isH) {
                    h = s.Substring(4) + ". ";
                    return;
                }

                s = Tag.Clear(Markdown.ToHtml(s), clTags);

                if (isPrevH) {
                    rs.Add($"</table><p>---</p><table class='main'><tr><td class='num'>{h}</td><td>{s}</td></tr></table><p>&nbsp;</p>");
                    rs.Add($"<table class='main'>");
                    return;
                }

                if (enru(s) > 0) {
                    rs.Add($"<tr><td class='txt'>{s}</td>");
                }
                else {
                    rs.Add($"<td class='txt'>{s}</td></tr>");
                }
            });

            File.WriteAllLines(@"d:\2.html", rs);
*/


/*
        // schedule
        var rs = Enumerable.Repeat("", 50).ToList();
        var its = new int[] { 0, 2, 4, 8 };

        for (var i = 0; i < 30; i++) {
            foreach (var it in its) {
                rs[i + it] += $", {i+1}";
            }
        }
        rs = rs.Where(x => x != "").ToList();
        rs.ForEach(x => { Console.WriteLine(x.Substring(2));  });
*/

/*
            // words combine
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