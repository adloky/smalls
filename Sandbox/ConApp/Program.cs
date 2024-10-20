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

namespace ConApp {
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

            // config
            config = File.ReadAllLines("d:/.sandbox").Select(x => {
                var i = x.IndexOf(":");
                return new KeyValuePair<string,string>(x.Substring(0, i), x.Substring(i + 1).Trim());
            }).ToDictionary(x => x.Key, x => x.Value);
        }

        static Dictionary<string, string> config;

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

        static StringSplitOptions ssop = StringSplitOptions.None;

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
            { "L", "л" }, { "M", "м" }, { "N", "н" }, { "NG", "ŋ" }, { "P", "п" },
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

        
        static IEnumerable<List<string>> handleSrt(string path) {
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
        

        static void checkSrt(string path) {
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

        static void srtLine(string path) {
            var ss = File.ReadAllLines(path);
            var rs = new List<string>();
            var numRe = new Regex(@"^\d+$", RegexOptions.Compiled);
            var timeRe = new Regex(@"^\d\d:\d\d:\d\d,\d\d\d --> \d\d:\d\d:\d\d,\d\d\d$", RegexOptions.Compiled);
            var backRe = new Regex(@"-?\([^a-z)]*\)", RegexOptions.Compiled);
            var i = 0;
            foreach (var s in ss) {
                i++;
                if (i < 3) {
                    if (i == 1 && !numRe.IsMatch(s) || i == 2 && !timeRe.IsMatch(s))
                        throw new Exception();

                    rs.Add(s);
                    if (i == 2)
                        rs.Add("");

                    continue;
                }

                if (s == "") {
                    i = 0;

                    if (rs.Last() == "") {
                        rs.RemoveRange(rs.Count - 3, 3);
                    }
                    else {
                        rs.Add(s);
                    }
                    
                    continue;
                }

                var s2 = backRe.Replace(s, "").Trim();
                if (s2 != "") {
                    rs[rs.Count - 1] += (rs.Last() == "" ? "" : " ") + s2;
                }
            }
            File.WriteAllLines(path.Replace("-orig", ""), rs);
        }

        static void srtCombile(string path, string pathOrig) {
            var ss = File.ReadAllLines(path);
            var os = File.ReadAllLines(pathOrig);
            var rs = new List<string>();
            var k = 0;
            var j = 0;
            for (var i = 0; i < os.Length; i++) {
                k++;
                if (k < 3) {
                    rs.Add(os[i]);
                    continue;
                }
                if (string.IsNullOrEmpty(os[i])) {
                    k = 0;
                    rs.Add(os[i]);
                    continue;
                }
                rs.Add(ss[j]);
                j++;
            }
            File.WriteAllLines(path.Replace("-clear", ""), rs);
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
            var lSet = new HashSet<string>(learnDic.Select(x => x.Key.Split(' ')[0])
                .SelectMany(x => (lemmaForms.TryGetValue(x, out var xs) ? xs : Enumerable.Empty<string>()).Concat(Enumerable.Repeat(x, 1))));
            var wRe = new Regex(@"[a-zA-Z]+");
            for (var i = 0; i < es.Length; i++) {
                es[i] = es[i].Replace("<", "&lt;");
                rs[i] = rs[i].Replace("<", "&lt;");
                es[i] = handleString(es[i], wRe, (x,m) => lSet.Contains(x.ToLower()) ? $"<u>{x}</u>" : x);
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


        static volatile bool ctrlC = false;

        [STAThread]
        static void Main(string[] args) {
            Console.CancelKeyPress += (o, e) => { ctrlC = true; e.Cancel = true; };

            var path = @"d:/phras.txt";
            var ss = File.ReadAllLines(path);
            for (var i = 0; i < ss.Length; i++) {
                if (ctrlC) break;
                var s = ss[i].Substring(6);
                if (s.Contains("{")) continue;
                var t = s + " {прочее} NotFound";
                for (var j = 0; j < 5 && t.Contains("NotFound"); j++) {
                    try { t = gtranslate(s); } catch { }
                }
                t = t.Substring(s.Length + 1);
                ss[i] += " " + t;
            }
            File.WriteAllLines(path, ss);

            //prepareWords("d:/words.txt");
            //fixQuotes(@"d:\.temp\7.txt");
            //deepl(@"d:\.temp\st\S01E01[eng]-clear.srt");
            //File.WriteAllLines("d:/3.txt", posReduce("d:/.temp/3.txt ").Where(x => x.p != "пробел" && x.p != "прочее").Select(x => $"{x.w} {x.p}"));
            //var s = gemini(File.ReadAllText("d:/1.txt"));
            //toAscii(@"d:\.temp\3.txt");
            //learnStat($"d:/.temp/3.txt");
            //makeTip($"d:/.temp/3.txt");

            /*
            var name = "S01E05";
            srtLine($"d:/.temp/srt/{name}[eng]-orig.srt");
            srtClear($"d:/.temp/srt/{name}[eng].srt");
            learnStat($"d:/.temp/srt/{name}[eng]-clear.srt");
            makeTip($"d:/.temp/srt/{name}[eng]-clear.srt", true);
            srtCombile($"d:/.temp/srt/{name}[eng]-clear-tip.srt", $"d:/.temp/srt/{name}[eng].srt");
            */

            //comicOcr(@"d:\.temp\archie\");
            //comicOcrPost(@"d:\.temp\archie\", 20, 5);
            //deeplSplit(@"d:\.temp\archie\en.txt");
            //comicComplete(@"d:\.temp\archie\");

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
            // conen/ 
            var path = @"d:\Projects\smalls\subs.txt";
            var ss = File.ReadAllLines(path).Select(x => x).ToList();
            var rs = new string[51];
            for (var i = 0; i < rs.Length; i++) rs[i] = "";
            var n = -1;
            foreach (var s in ss.Select(x => x.Substring(5))) {
                if (s.Contains("{")) {
                    n = int.Parse(s.Substring(0, 2));
                }
                rs[n] += "\r\n" + s;
            }

            for (var i = 5; i < 50; i++) {
                File.WriteAllText($"d:/Projects/smalls/conen/{i.ToString("00")}.txt", rs[i].Substring(2));
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