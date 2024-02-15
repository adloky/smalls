using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System.Windows.Forms;
using System.IO;
using System.Text.RegularExpressions;

namespace DeeplCon
{
    class Program
    {
        [STAThreadAttribute]
        static void Main(string[] args)
        {
            var advRe = new Regex("\n\nПереведено с помощью DeepL.*", RegexOptions.Multiline);

            var path = @"d:\french-b-fs.txt";
            var ss = File.ReadAllLines(path);
            
            path = path.Replace(".txt", "-d.txt");
            var rs = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
            var qs = new Queue<string>(ss.Skip(rs.Count));

            var driver = new ChromeDriver();
            driver.Navigate().GoToUrl("https://www.deepl.com/ru/login");
            Thread.Sleep(2000);
            driver.FindElement(By.CssSelector("#menu-login-username")).SendKeys("adloky@gmail.com");
            driver.FindElement(By.CssSelector("#menu-login-password")).SendKeys("Ka2simapd#");
            driver.FindElement(By.CssSelector("#menu-login-submit")).Click();
            Thread.Sleep(5000);

            while (qs.Count > 0) {
                Thread.Sleep(2000);
                driver.Navigate().GoToUrl("https://www.deepl.com/translator#en/ru/");
                var ms = new List<string>();
                var len = 0;
                while (qs.Count > 0 && len + qs.Peek().Length < 4900) {
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
                //ta.SendKeys(s);
                IWebElement btn = null;
                while (btn == null) {
                    Thread.Sleep(2000);
                    try {
                        btn = driver.FindElement(By.CssSelector(@"button[data-testid=""translator-target-toolbar-copy""]"));
                        btn.Click();
                    } catch {
                        btn = null;
                    }
                }
                
                Thread.Sleep(2000);
                var r = Clipboard.GetText();
                r = r.Replace("\r", "");
                r = advRe.Replace(r, "");
                File.AppendAllLines(path, r.Split('\n').Select(x => x.Trim()));
            }
        }
    }
}
