using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConApp {
    public class OcrRootObject {
        public OcrParsedResult[] ParsedResults { get; set; }
        public int OCRExitCode { get; set; }
        public bool IsErroredOnProcessing { get; set; }
        public string ErrorMessage { get; set; }
        public string ErrorDetails { get; set; }

        public int Width { get; set; }

        public OcrRect[] GetRects() {
            return ParsedResults[0].TextOverlay.Lines.Select(l => {
                var r = new OcrRect();
                var ws = l.Words.Where(x => !(x.Width == 0 && x.Height == 0)).ToArray();
                r.Text = l.LineText;
                r.Left = (int)ws.Select(w => w.Left).Min();
                r.Top = (int)ws.Select(w => w.Top).Min();
                r.Right = (int)ws.Select(w => w.Left + w.Width).Max();
                r.Bottom = (int)ws.Select(w => w.Top + w.Height).Max();
                return r;
            }).ToArray();
        }
    }

    public class OcrParsedResult {
        public OcrTextOverlay TextOverlay { get; set; }
        public object FileParseExitCode { get; set; }
        public string ParsedText { get; set; }
        public string ErrorMessage { get; set; }
        public string ErrorDetails { get; set; }
    }

    public class OcrTextOverlay {
        public OcrLine[] Lines { get; set; }
    }

    public class OcrLine {
        public string LineText { get; set; }
        public OcrWord[] Words { get; set; }
        public float MaxHeight { get; set; }
        public float MinTop { get; set; }
   }

    public class OcrWord {
        public string WordText { get; set; }
        public float Left { get; set; }
        public float Top { get; set; }
        public float Height { get; set; }
        public float Width { get; set; }
    }

    public class OcrRect {
        public string Text { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Right { get; set; }
        public int Bottom { get; set; }

        public Rectangle GetRectangle(int indent) {
            return new Rectangle(Left - indent, Top - indent, Right - Left + indent, Bottom - Top + indent);
        }

        public int Intersect(OcrRect rect, int indent) {
            return Rectangle.Intersect(GetRectangle(indent), rect.GetRectangle(indent)).Width;
        }

        private static IEnumerable<OcrRect> group(OcrRect rect, HashSet<OcrRect> set, int width, int fsize, int indent) {
            if (!set.Contains(rect)) return Enumerable.Empty<OcrRect>();
            set.Remove(rect);
            var irs = set.Where(r => rect.Intersect(r, indent) > fsize * 3).ToArray();
            return Enumerable.Repeat(rect, 1).Concat(irs.SelectMany(r => group(r, set, width, fsize, indent)));
        }

        public static IList<OcrRect[]> Group(OcrRect[] rects, int width, int fsize, int indent) {
            fsize = fsize * width / 1000;
            indent = indent * width / 1000;
            var set = new HashSet<OcrRect>(rects); // .Where(x => x.Bottom - x.Top < 2 * fsize)
            var r = new List<OcrRect[]>();
            while (set.Count > 0) {
                r.Add(group(set.First(), set, width, fsize, indent).ToArray());
            }
            return r;
        }
    }
}
