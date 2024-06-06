using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ConApp {
    public class LancasterStemmer {
        enum RuleType {
            Stop = -1,
            Intact = 0,
            Cont = 1,
            Protect = 2,
            Contint = 3
        }

        class Rule {
            public string Match;
            public string Replacement;
            public RuleType Type;
        }

        static Regex vowelRe = new Regex("[aeiouy]", RegexOptions.Compiled);

        static string applyRules(string value, bool isIntact) {
            Rules.TryGetValue(value.Last(), out var ruleset);
            var index = -1;

            if (ruleset == null) {
                return value;
            }

            while (++index < ruleset.Length) {
                var rule = ruleset[index];

                if (!isIntact && (rule.Type == RuleType.Intact || rule.Type == RuleType.Contint)) {
                    continue;
                }

                var breakpoint = value.Length - rule.Match.Length;

                if (breakpoint < 0 || value.Substring(breakpoint) != rule.Match) {
                    continue;
                }

                if (rule.Type == RuleType.Protect) {
                    return value;
                }

                var next = value.Substring(0, breakpoint) + rule.Replacement;


                if (!acceptable(next)) {
                    continue;
                }

                if (rule.Type == RuleType.Cont || rule.Type == RuleType.Contint) {
                    return applyRules(next, false);
                }

                return next;
            }

            return value;
        }

        public static string Stem(string value) {
            return applyRules(value.ToLower(), true);
        }

        static bool acceptable(string value) {
            if (value == "")
                return false;

            return vowelRe.IsMatch(value.Substring(0, 1))
              ? value.Length > 1
              : value.Length > 2 && vowelRe.IsMatch(value);
        }

        static Dictionary<char, Rule[]> Rules = new Dictionary<char, Rule[]>() {
            {   'a', new [] {
                    new Rule() { Match = "ia", Replacement = "", Type = RuleType.Intact},
                    new Rule() { Match = "a", Replacement = "", Type = RuleType.Intact}
                }
            },
            {   'b', new [] {
                    new Rule() { Match = "bb", Replacement = "b", Type = RuleType.Stop}
                }
            },
            {   'c', new [] {
                    new Rule() { Match = "ytic", Replacement = "ys", Type = RuleType.Stop},
                    new Rule() { Match = "ic", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "nc", Replacement = "nt", Type = RuleType.Cont}
                }
            },
            {   'd', new [] {
                    new Rule() { Match = "dd", Replacement = "d", Type = RuleType.Stop},
                    new Rule() { Match = "ied", Replacement = "y", Type = RuleType.Cont},
                    new Rule() { Match = "ceed", Replacement = "cess", Type = RuleType.Stop},
                    new Rule() { Match = "eed", Replacement = "ee", Type = RuleType.Stop},
                    new Rule() { Match = "ed", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "hood", Replacement = "", Type = RuleType.Cont}
                }
            },
            {   'e', new [] {
                    new Rule() { Match = "e", Replacement = "", Type = RuleType.Cont}
                }
            },
            {   'f', new [] {
                    new Rule() { Match = "lief", Replacement = "liev", Type = RuleType.Stop},
                    new Rule() { Match = "if", Replacement = "", Type = RuleType.Cont}
                }
            },
            {   'g', new [] {
                    new Rule() { Match = "ing", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "iag", Replacement = "y", Type = RuleType.Stop},
                    new Rule() { Match = "ag", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "gg", Replacement = "g", Type = RuleType.Stop}
                }
            },
            {   'h', new [] {
                    new Rule() { Match = "th", Replacement = "", Type = RuleType.Intact},
                    new Rule() { Match = "guish", Replacement = "ct", Type = RuleType.Stop},
                    new Rule() { Match = "ish", Replacement = "", Type = RuleType.Cont}
                }
            },
            {   'i', new [] {
                    new Rule() { Match = "i", Replacement = "", Type = RuleType.Intact},
                    new Rule() { Match = "i", Replacement = "y", Type = RuleType.Cont}
                }
            },
            {   'j', new [] {
                    new Rule() { Match = "ij", Replacement = "id", Type = RuleType.Stop},
                    new Rule() { Match = "fuj", Replacement = "fus", Type = RuleType.Stop},
                    new Rule() { Match = "uj", Replacement = "ud", Type = RuleType.Stop},
                    new Rule() { Match = "oj", Replacement = "od", Type = RuleType.Stop},
                    new Rule() { Match = "hej", Replacement = "her", Type = RuleType.Stop},
                    new Rule() { Match = "verj", Replacement = "vert", Type = RuleType.Stop},
                    new Rule() { Match = "misj", Replacement = "mit", Type = RuleType.Stop},
                    new Rule() { Match = "nj", Replacement = "nd", Type = RuleType.Stop},
                    new Rule() { Match = "j", Replacement = "s", Type = RuleType.Stop}
                }
            },
            {   'l', new [] {
                    new Rule() { Match = "ifiabl", Replacement = "", Type = RuleType.Stop},
                    new Rule() { Match = "iabl", Replacement = "y", Type = RuleType.Stop},
                    new Rule() { Match = "abl", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "ibl", Replacement = "", Type = RuleType.Stop},
                    new Rule() { Match = "bil", Replacement = "bl", Type = RuleType.Cont},
                    new Rule() { Match = "cl", Replacement = "c", Type = RuleType.Stop},
                    new Rule() { Match = "iful", Replacement = "y", Type = RuleType.Stop},
                    new Rule() { Match = "ful", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "ul", Replacement = "", Type = RuleType.Stop},
                    new Rule() { Match = "ial", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "ual", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "al", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "ll", Replacement = "l", Type = RuleType.Stop}
                }
            },
            {   'm', new [] {
                    new Rule() { Match = "ium", Replacement = "", Type = RuleType.Stop},
                    new Rule() { Match = "um", Replacement = "", Type = RuleType.Intact},
                    new Rule() { Match = "ism", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "mm", Replacement = "m", Type = RuleType.Stop}
                }
            },
            {   'n', new [] {
                    new Rule() { Match = "sion", Replacement = "j", Type = RuleType.Cont},
                    new Rule() { Match = "xion", Replacement = "ct", Type = RuleType.Stop},
                    new Rule() { Match = "ion", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "ian", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "an", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "een", Replacement = "", Type = RuleType.Protect},
                    new Rule() { Match = "en", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "nn", Replacement = "n", Type = RuleType.Stop}
                }
            },
            {   'p', new [] {
                    new Rule() { Match = "ship", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "pp", Replacement = "p", Type = RuleType.Stop}
                }
            },
            {   'r', new [] {
                    new Rule() { Match = "er", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "ear", Replacement = "", Type = RuleType.Protect},
                    new Rule() { Match = "ar", Replacement = "", Type = RuleType.Stop},
                    new Rule() { Match = "or", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "ur", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "rr", Replacement = "r", Type = RuleType.Stop},
                    new Rule() { Match = "tr", Replacement = "t", Type = RuleType.Cont},
                    new Rule() { Match = "ier", Replacement = "y", Type = RuleType.Cont}
                }
            },
            {   's', new [] {
                    new Rule() { Match = "ies", Replacement = "y", Type = RuleType.Cont},
                    new Rule() { Match = "sis", Replacement = "s", Type = RuleType.Stop},
                    new Rule() { Match = "is", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "ness", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "ss", Replacement = "", Type = RuleType.Protect},
                    new Rule() { Match = "ous", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "us", Replacement = "", Type = RuleType.Intact},
                    new Rule() { Match = "s", Replacement = "", Type = RuleType.Contint},
                    //new Rule() { Match = "s", Replacement = "", Type = RuleType.Protect}
                    new Rule() { Match = "s", Replacement = "", Type = RuleType.Stop}
                }
            },
            {   't', new [] {
                    new Rule() { Match = "plicat", Replacement = "ply", Type = RuleType.Stop},
                    new Rule() { Match = "at", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "ment", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "ent", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "ant", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "ript", Replacement = "rib", Type = RuleType.Stop},
                    new Rule() { Match = "orpt", Replacement = "orb", Type = RuleType.Stop},
                    new Rule() { Match = "duct", Replacement = "duc", Type = RuleType.Stop},
                    new Rule() { Match = "sumpt", Replacement = "sum", Type = RuleType.Stop},
                    new Rule() { Match = "cept", Replacement = "ceiv", Type = RuleType.Stop},
                    new Rule() { Match = "olut", Replacement = "olv", Type = RuleType.Stop},
                    new Rule() { Match = "sist", Replacement = "", Type = RuleType.Protect},
                    new Rule() { Match = "ist", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "tt", Replacement = "t", Type = RuleType.Stop}
                }
            },
            {   'u', new [] {
                    new Rule() { Match = "iqu", Replacement = "", Type = RuleType.Stop},
                    new Rule() { Match = "ogu", Replacement = "og", Type = RuleType.Stop}
                }
            },
            {   'v', new [] {
                    new Rule() { Match = "siv", Replacement = "j", Type = RuleType.Cont},
                    new Rule() { Match = "eiv", Replacement = "", Type = RuleType.Protect},
                    new Rule() { Match = "iv", Replacement = "", Type = RuleType.Cont}
                }
            },
            {   'y', new [] {
                    new Rule() { Match = "bly", Replacement = "bl", Type = RuleType.Cont},
                    new Rule() { Match = "ily", Replacement = "y", Type = RuleType.Cont},
                    new Rule() { Match = "ply", Replacement = "", Type = RuleType.Protect},
                    new Rule() { Match = "ly", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "ogy", Replacement = "og", Type = RuleType.Stop},
                    new Rule() { Match = "phy", Replacement = "ph", Type = RuleType.Stop},
                    new Rule() { Match = "omy", Replacement = "om", Type = RuleType.Stop},
                    new Rule() { Match = "opy", Replacement = "op", Type = RuleType.Stop},
                    new Rule() { Match = "ity", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "ety", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "lty", Replacement = "l", Type = RuleType.Stop},
                    new Rule() { Match = "istry", Replacement = "", Type = RuleType.Stop},
                    new Rule() { Match = "ary", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "ory", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "ify", Replacement = "", Type = RuleType.Stop},
                    new Rule() { Match = "ncy", Replacement = "nt", Type = RuleType.Cont},
                    new Rule() { Match = "acy", Replacement = "", Type = RuleType.Cont}
                }
            },
            {   'z', new [] {
                    new Rule() { Match = "iz", Replacement = "", Type = RuleType.Cont},
                    new Rule() { Match = "yz", Replacement = "ys", Type = RuleType.Stop}
                }
            },
        };
    }
}
