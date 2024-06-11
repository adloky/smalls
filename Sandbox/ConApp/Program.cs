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
    static class Program {
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
                    foreach (var xx in x) {
                        _lemmas[xx] = x[0];
                        fs.Add(xx);
                    }
                });
        }

        static Dictionary<string,string> _existingWords;
        static Regex pronBrRe = new Regex(@"\([^)]+\)");

        static Dictionary<string, string> existingWords {
            get {
                if (_existingWords != null)
                    return _existingWords;

                _existingWords = File.ReadAllLines("d:/Projects/smalls/en-dic.txt")
                    .Where(x => !x.Contains("Not found"))
                    .Select(x => {
                        var sp = x.ToLower().Split(new[] { " - ", "[", "]" }, StringSplitOptions.RemoveEmptyEntries);
                        var t = x.Contains(" - [") ? pronBrRe.Replace(sp[2], "") : "";
                        return new KeyValuePair<string, string>(sp[0], t);
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

        static Dictionary<char, char> ruPronDic = new Dictionary<char, char>() {
            { 'j', 'й' }, { 'u', 'у' }, { 'b', 'б' }, { 'i', 'и' }, { 'ð', 'ð' }, { 't', 'т' },
            { 'ə', 'э' }, { 'e', 'е' }, { 'ɪ', 'ы' }, { 'æ', 'э' }, { 'n', 'н' }, { 'd', 'д' },
            { 'ɒ', 'а' }, { 'v', 'в' }, { 'h', 'х' }, { 'w', 'w' }, { 'm', 'м' }, { 's', 'с' },
            { 'f', 'ф' }, { 'ɔ', 'о' }, { 'r', 'р' }, { 'ɡ', 'г' }, { 'ʊ', 'у' }, { 'a', 'а' },
            { 'k', 'к' }, { 'ʌ', 'а' }, { 'l', 'л' }, { 'ʒ', 'ж' }, { 'ʃ', 'ш' }, { 'p', 'п' },
            { 'ɜ', 'ё' }, { 'θ', 'θ' }, { 'ŋ', 'ŋ' }, { 'z', 'з' }, { 'ɑ', 'а' }, { 'g', 'г' },
            { 'o', 'о' }, { 'ɛ', 'е' },
        };

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

        static Regex ruVowelRe = new Regex("[уиоыэаёое]", RegexOptions.Compiled);

        static string ruPron(string s) {
            var cs = new List<char>();
            var isStress = false;
            foreach (var c in s) {
                switch (c) {
                    case 'ˈ':
                    case 'ˌ':
                        isStress = true;
                        break;

                    case 'ː':
                        cs.Add(char.ToLower(cs.Last()));
                        break;

                    default:
                        var rc = ruPronDic[c];
                        if (rc == 'ы' && cs.Count > 0 && ruVowelRe.IsMatch(cs.Last().ToString().ToLower())) {
                            rc = 'й';
                        }

                        if (rc == 'ш' && cs.Count > 0 && cs.Last() == 'т') {
                            cs.RemoveAt(cs.Count - 1);
                            rc = 'ч';
                        }

                        if (isStress && ruVowelRe.IsMatch(rc.ToString())) {
                            rc = char.ToUpper(rc);
                            isStress = false;
                        }
                        cs.Add(rc);
                        break;
                }
            }

            return new string(cs.ToArray());
        }

        #endregion

        public static void Shuffle<T>(this IList<T> list, int start, int end) {
            var len = end - start;
            var rs = list.Skip(start).Take(len).OrderBy(_ => Guid.NewGuid()).ToArray();
            for (var i = 0; i < len; i++) {
                list[start + i] = rs[i];
            }
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

                var ks = r.k.Split(' ');
                var ts = new List<string>();
                foreach (var k in ks) {
                    var t = (string)null;
                    if (!existingWords.TryGetValue(k, out t)) {
                        if (lemmas.TryGetValue(k, out var _k)) {
                            existingWords.TryGetValue(_k, out t);
                        }
                    }
                    if (t == null)
                        continue;
                    
                    ts.Add(ruPron(t));
                }

                if (ts.Count > 0) {
                    r.t = string.Join(" ", ts);
                }

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
            for (var j = 0; j < ds.Count; j++) {
                var d = ds[j];
                rs.Add($"DAY: {j+1}");
                for (var i = 0; i < d.Count; i += 10) {
                    var r = d.Skip(i).Take(10).ToList();
                    var n = r.Where(x => x.i == 1).Select(x => $"{x.k} {{{x.p}}} [{x.t}] {x.v}").ToList();
                    if (n.Count > 0) rs.Add("NEW: " + string.Join("; ", n));
                    var l = r.Where(x => x.i == intMax).Select(x => $"{x.k} {{{x.p}}}").ToList();
                    if (l.Count > 0) rs.Add("LAST: " + string.Join("; ", l));
                    var b = r.Select(x => $"{x.k} (как {x.p}: {x.v}) [{x.t}]").ToList();
                    rs.Add("BODY: " + string.Join("; ", b));
                }
            }

            File.WriteAllLines(pathEx(path, "-2"), rs);
        }

        static volatile bool ctrlC = false;

        [STAThread]
        static void Main(string[] args) {
            Console.CancelKeyPress += (o, e) => { ctrlC = true; e.Cancel = true; };

            /*
            var ss = File.ReadAllLines("d:/index.js").Where(x => x.Contains("\": \"")).Select(x => x.Replace("\": \"", " ").Replace("  \"", "").Replace("\",", ""));
            File.WriteAllLines("d:/index.txt", ss);

            return;
            */

            cmu.Where(kv => kv.Key.Length == 2 && "AEIOU".Contains(kv.Key.Substring(0,1))).ToList().ForEach(kv => {
                for (var i = 0; i < 3; i++) {
                    var v = (i > 0 ? char.ToUpper(kv.Value[0]) : kv.Value[0]) + kv.Value.Substring(1);
                    var k = $"{kv.Key}{i}";
                    if (cmu.ContainsKey(k))
                        continue;
                    cmu.Add(k, v);
                }
            });

            var dic = new Dictionary<string, string>();
            foreach (var w in existingWords.Keys) {
                var rs = new List<string>();
                if (lemmas.TryGetValue(w, out var k)) {
                    rs = lemmaForms[k];
                }
                else {
                    rs = new List<string>() { w };
                }

                rs.ForEach(x => {
                    if (!dic.ContainsKey(x)) {
                        dic.Add(x, null);
                    }
                });
            }

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

            //prepareWords("d:/words.txt");
            //Console.WriteLine(lemmaForms["dog"][0]);

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