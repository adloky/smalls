using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ConApp {

    public static class Aline {
        // ──────────────────────────────────────────────────────────────────────────
        // Веса параметров (по умолчанию из оригинальной статьи Kondrak 2000)
        // ──────────────────────────────────────────────────────────────────────────
        private const double C_Skip = 10.0;   // штраф за пропуск сегмента
        private const double C_Sub = 35.0;   // макс. штраф за замену
        private const double C_Exp = 45.0;   // макс. штраф за расширение
        private const double C_Vwl = 5.0;    // штраф за несовпадение тип гласный/согласный
        private const double C_Tail = 0.015;    // штраф за длину хвоста


        // Веса фонетических признаков
        private const double W_Place = 40.0;
        private const double W_Manner = 50.0;
        private const double W_Voice = 10.0;
        private const double W_Nasal = 10.0;
        private const double W_Retroflex = 10.0;
        private const double W_Lateral = 10.0;
        private const double W_Aspirated = 5.0;
        private const double W_Long = 1.0;
        private const double W_High = 5.0;
        private const double W_Back = 5.0;
        private const double W_Round = 5.0;

        // ядро + кол-во звуков после
        public static int NucleusWindow { get; set; } = 0;

        // ──────────────────────────────────────────────────────────────────────────
        // Перечисления фонетических признаков
        // ──────────────────────────────────────────────────────────────────────────
        private enum Place {
            Bilabial = 0, Labiodental, Dental, Alveolar, Retroflex,
            Palatal, Velar, Uvular, Pharyngeal, Glottal
        }

        private enum Manner {
            Stop = 0, Affricate, Fricative, Nasal, LateralApprox,
            Rhotic, Approximant, Vowel
        }

        private enum VoicedVal { Voiceless = 0, Voiced = 1 }
        private enum BinaryVal { No = 0, Yes = 1 }
        private enum HighVal { Low = 0, Mid = 1, High = 2 }
        private enum BackVal { Front = 0, Central = 1, Back = 2 }
        private enum RoundVal { Unrounded = 0, Rounded = 1 }

        // ──────────────────────────────────────────────────────────────────────────
        // Структура фонемы
        // ──────────────────────────────────────────────────────────────────────────
        private class Phoneme {
            public string Symbol;
            public bool IsVowel;

            // Согласные
            public Place PlaceVal;
            public Manner MannerVal;
            public VoicedVal VoiceVal;
            public BinaryVal NasalVal;
            public BinaryVal RetrflxVal;
            public BinaryVal LateralVal;
            public BinaryVal AspiratedVal;

            // Гласные
            public HighVal HighV;
            public BackVal BackV;
            public RoundVal RoundV;

            // Общее
            public BinaryVal LongVal;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Таблица фонем (IPA-подобные символы)
        // ──────────────────────────────────────────────────────────────────────────
        private static readonly Dictionary<string, Phoneme> PhonemeTable
            = new Dictionary<string, Phoneme>(StringComparer.Ordinal) {
            // ── Гласные ──────────────────────────────────────────────────────────
            ["а"] = V("а", HighVal.Low, BackVal.Central, RoundVal.Unrounded),
            ["е"] = V("е", HighVal.Mid, BackVal.Front, RoundVal.Unrounded),
            ["и"] = V("и", HighVal.High, BackVal.Front, RoundVal.Unrounded),
            ["о"] = V("о", HighVal.Mid, BackVal.Back, RoundVal.Rounded),
            ["у"] = V("у", HighVal.High, BackVal.Back, RoundVal.Rounded),
            ["э"] = V("э", HighVal.Mid, BackVal.Central, RoundVal.Unrounded),
            ["y"] = V("y", HighVal.High, BackVal.Front, RoundVal.Rounded),

            // ── Согласные (смычные) ──────────────────────────────────────────────
            ["п"] = C("п", Place.Bilabial, Manner.Stop, VoicedVal.Voiceless),
            ["б"] = C("б", Place.Bilabial, Manner.Stop, VoicedVal.Voiced),
            ["т"] = C("т", Place.Alveolar, Manner.Stop, VoicedVal.Voiceless),
            ["д"] = C("д", Place.Alveolar, Manner.Stop, VoicedVal.Voiced),
            ["к"] = C("к", Place.Velar, Manner.Stop, VoicedVal.Voiceless),
            ["г"] = C("г", Place.Velar, Manner.Stop, VoicedVal.Voiced),
            

            // ── Аффрикаты ────────────────────────────────────────────────────────
            ["ч"] = C("ч", Place.Palatal, Manner.Affricate, VoicedVal.Voiceless),
            ["дж"] = C("дж", Place.Palatal, Manner.Affricate, VoicedVal.Voiced),
            ["тс"] = C("тс", Place.Alveolar, Manner.Affricate, VoicedVal.Voiceless),
            ["дз"] = C("дз", Place.Alveolar, Manner.Affricate, VoicedVal.Voiced),

            // ── Фрикативные ──────────────────────────────────────────────────────
            ["ф"] = C("ф", Place.Labiodental, Manner.Fricative, VoicedVal.Voiceless),
            ["в"] = C("в", Place.Labiodental, Manner.Fricative, VoicedVal.Voiced),
            ["с"] = C("с", Place.Alveolar, Manner.Fricative, VoicedVal.Voiceless),
            ["з"] = C("з", Place.Alveolar, Manner.Fricative, VoicedVal.Voiced),
            ["ш"] = C("ш", Place.Palatal, Manner.Fricative, VoicedVal.Voiceless),
            ["ж"] = C("ж", Place.Palatal, Manner.Fricative, VoicedVal.Voiced),
            ["х"] = C("х", Place.Velar, Manner.Fricative, VoicedVal.Voiceless),

            // ── Носовые ──────────────────────────────────────────────────────────
            ["м"] = Cn("м", Place.Bilabial, VoicedVal.Voiced),
            ["н"] = Cn("н", Place.Alveolar, VoicedVal.Voiced),

            // ── Латеральные и ротические ─────────────────────────────────────────
            ["л"] = Cl("л", Place.Alveolar),
            ["р"] = Cr("р", Place.Alveolar),

            // ── Аппроксиманты ────────────────────────────────────────────────────
            ["й"] = Ca("й", Place.Palatal),
        };

        // ──────────────────────────────────────────────────────────────────────────
        // Вспомогательные фабрики
        // ──────────────────────────────────────────────────────────────────────────
        private static Phoneme V(string s, HighVal h, BackVal b, RoundVal r) => new Phoneme { Symbol = s, IsVowel = true, HighV = h, BackV = b, RoundV = r };

        private static Phoneme C(string s, Place pl, Manner mn, VoicedVal vo) => new Phoneme { Symbol = s, IsVowel = false, PlaceVal = pl, MannerVal = mn, VoiceVal = vo };

        private static Phoneme Cn(string s, Place pl, VoicedVal vo) => new Phoneme {
            Symbol = s, IsVowel = false, PlaceVal = pl, MannerVal = Manner.Nasal,
            VoiceVal = vo, NasalVal = BinaryVal.Yes
        };

        private static Phoneme Cl(string s, Place pl) => new Phoneme {
            Symbol = s, IsVowel = false, PlaceVal = pl, MannerVal = Manner.LateralApprox,
            VoiceVal = VoicedVal.Voiced, LateralVal = BinaryVal.Yes
        };

        private static Phoneme Cr(string s, Place pl) => new Phoneme {
            Symbol = s, IsVowel = false, PlaceVal = pl, MannerVal = Manner.Rhotic,
            VoiceVal = VoicedVal.Voiced
        };

        private static Phoneme Ca(string s, Place pl) => new Phoneme {
            Symbol = s, IsVowel = false, PlaceVal = pl, MannerVal = Manner.Approximant,
            VoiceVal = VoicedVal.Voiced
        };

        // ──────────────────────────────────────────────────────────────────────────
        // Функция схожести двух фонем σ(p, q)
        // ──────────────────────────────────────────────────────────────────────────
        private static double Similarity(Phoneme p, Phoneme q) {
            if (p == null || q == null) return 0.0;

            double score = 0.0;

            // Тип (гласный/согласный) – общий признак
            if (p.IsVowel != q.IsVowel) return 0.0;   // нет смысла сравнивать напрямую

            if (p.IsVowel && q.IsVowel) {
                // Гласные: high, back, round, long
                score += W_High * (1.0 - Math.Abs((int)p.HighV - (int)q.HighV) / 2.0);
                score += W_Back * (1.0 - Math.Abs((int)p.BackV - (int)q.BackV) / 2.0);
                score += W_Round * (1.0 - Math.Abs((int)p.RoundV - (int)q.RoundV));
                score += W_Long * (1.0 - Math.Abs((int)p.LongVal - (int)q.LongVal));
            }
            else {
                // Согласные: place, manner, voice, nasal, retroflex, lateral, aspirated
                double placeMax = (double)(Place.Glottal);
                double mannerMax = (double)(Manner.Vowel);

                score += W_Place * (1.0 - Math.Abs((int)p.PlaceVal - (int)q.PlaceVal) / placeMax);
                score += W_Manner * (1.0 - Math.Abs((int)p.MannerVal - (int)q.MannerVal) / mannerMax);
                score += W_Voice * (1.0 - Math.Abs((int)p.VoiceVal - (int)q.VoiceVal));
                score += W_Nasal * (1.0 - Math.Abs((int)p.NasalVal - (int)q.NasalVal));
                score += W_Retroflex * (1.0 - Math.Abs((int)p.RetrflxVal - (int)q.RetrflxVal));
                score += W_Lateral * (1.0 - Math.Abs((int)p.LateralVal - (int)q.LateralVal));
                score += W_Aspirated * (1.0 - Math.Abs((int)p.AspiratedVal - (int)q.AspiratedVal));
                score += W_Long * (1.0 - Math.Abs((int)p.LongVal - (int)q.LongVal));
            }

            return score;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Стоимость пропуска (skip penalty)
        // ──────────────────────────────────────────────────────────────────────────
        private static double SkipCost(Phoneme p) {
            // Гласные штрафуются сильнее
            return p != null && p.IsVowel ? C_Skip + C_Vwl : C_Skip;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Разбивка слова на фонемные символы (жадный: сначала 2-символьные)
        // ──────────────────────────────────────────────────────────────────────────
        private static List<string> Tokenize(string word) {
            var result = new List<string>();
            int i = 0;
            while (i < word.Length) {
                if (i + 1 < word.Length) {
                    string di = word.Substring(i, 2);
                    if (PhonemeTable.ContainsKey(di)) {
                        result.Add(di);
                        i += 2;
                        continue;
                    }
                }
                result.Add(word[i].ToString());
                i++;
            }
            return result;
        }

        private static Regex yAuоRe = new Regex(@"[ыяюё]", RegexOptions.Compiled);

        private static string PrepWord(string s) {
            return yAuоRe.Replace(s.ToLower(), m => {
                switch (m.Value) {
                    case "я": return "йа";
                    case "ю": return "йу";
                    case "ё": return "йо";
                    case "ы": return "и";
                    default: throw new Exception();
                }
            });
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Основной алгоритм: выравнивание по Kondrak (2000)
        // Возвращает нормализованный балл схожести [0, 1]
        // ──────────────────────────────────────────────────────────────────────────
        public static double Compute(string wordA, string wordB) {
            List<string> seqA = Tokenize(PrepWord(wordA));
            List<string> seqB = Tokenize(PrepWord(wordB)).Take(seqA.Count + 2).ToList();

            int m = seqA.Count;
            int n = seqB.Count;

            // dp[i][j] = лучший балл при выравнивании seqA[0..i-1] и seqB[0..j-1]
            double[,] dp = new double[m + 1, n + 1];

            // Инициализация
            dp[0, 0] = 0.0;
            for (int i = 1; i <= m; i++) {
                Phoneme p = GetPhoneme(seqA[i - 1]);
                dp[i, 0] = dp[i - 1, 0] - SkipCost(p);
            }


            for (int j = 1; j <= n; j++) {
                Phoneme q = GetPhoneme(seqB[j - 1]);
                dp[0, j] = dp[0, j - 1] - SkipCost(q);
            }

            // Заполнение таблицы
            for (int i = 1; i <= m; i++) {
                Phoneme p = GetPhoneme(seqA[i - 1]);
                for (int j = 1; j <= n; j++) {
                    Phoneme q = GetPhoneme(seqB[j - 1]);

                    // 1. Совмещение p и q (substitution / match)
                    double sigma = (p != null && q != null && p.IsVowel == q.IsVowel)
                        ? Similarity(p, q)
                        : 0.0;

                    double matchCost = dp[i - 1, j - 1] + sigma;

                    // 2. Пропуск p (gap in B)
                    double skipA = dp[i - 1, j] - SkipCost(p);

                    // 3. Пропуск q (gap in A)
                    double skipB = dp[i, j - 1] - SkipCost(q);

                    // 4. Несовпадение типа (один гласный, другой согласный) — штраф
                    double mismatch = (p != null && q != null && p.IsVowel != q.IsVowel)
                        ? dp[i - 1, j - 1] - C_Vwl
                        : double.NegativeInfinity;

                    dp[i, j] = Math.Max(Math.Max(matchCost, skipA),
                                        Math.Max(skipB, mismatch));
                }
            }

            double rawScore = double.NegativeInfinity;
            int rawJ = 0;
            for (int j = 0; j <= n; j++) {
                if (dp[m, j] > rawScore) {
                    rawScore = dp[m, j];
                    rawJ = j;
                }
            }

            var tailPenalty = (n - rawJ) * C_Tail;

            // Нормализация: делим на лучший из самосравнений
            double selfA = SelfScore(seqA);
            double selfB = SelfScore(seqB);
            double normalizer = Math.Max(selfA, selfB);

            if (normalizer <= 0.0) return rawScore > 0 ? 1.0 : 0.0;

            return Math.Max(0.0, Math.Min(1.0, rawScore / selfA - tailPenalty));
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Балл самосравнения (максимально возможный балл для слова)
        // ──────────────────────────────────────────────────────────────────────────
        private static double SelfScore(List<string> seq) {
            double score = 0.0;
            foreach (string sym in seq) {
                Phoneme p = GetPhoneme(sym);
                if (p != null)
                    score += Similarity(p, p);
            }
            return score;
        }

        private static Phoneme GetPhoneme(string sym)
            => PhonemeTable.TryGetValue(sym, out Phoneme ph) ? ph : null;
    }
}
