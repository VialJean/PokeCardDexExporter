using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Diagnostics;
using System.Text.RegularExpressions;
using WebDriverManager;
using WebDriverManager.DriverConfigs.Impl;

namespace PokeCardDexExporter
{
    internal partial class Program
    {
        private static string[] extensions = [
            "SVP",
            "SVI",
            "PAL",
            "OBF",
            "MEW",
            "PAR",
            "PAF",
            "TEF",
            "TWM",
            "SFA",
            "SCR",
            "SSP",
            "PRE",
            "JTG",
            "DRI",
            "BLK",
            "WHT",
            "MEP",
            "MEG",
            "PFL",
            "ASC",
            "POR"
        ];
        public static bool premierScan = true;
        static async Task Main(string[] args)
        {
            List<Carte> cartes = [];
            Stopwatch stopwatch = new Stopwatch();

            Console.WriteLine("---------------------------------------------------------------------------------------------");
            Console.WriteLine("Sélectionner l'extension à scanner");
            Console.WriteLine("Extensions disponibles :");
            foreach (var extension in extensions)
            {
                Console.WriteLine(extension);
            }
            Console.WriteLine("* : Scanner toutes les extensions");
            Console.WriteLine("---------------------------------------------------------------------------------------------");

            var choix = Console.ReadLine();
            if (choix != null)
            {
                stopwatch.Start();


                if (choix == "*")
                {
                    Console.WriteLine("Début de l'export");
                    var driver = await LancerDriver();
                    foreach (var extension in extensions)
                    {
                        ScanCartes(driver, extension, cartes);
                    }
                    await File.WriteAllTextAsync("collection.json", JsonConvert.SerializeObject(cartes, Formatting.Indented));

                    Console.WriteLine($"Export réalisé en {stopwatch.Elapsed.TotalSeconds} secondes");
                    driver.Quit();
                    driver.Dispose();
                }
                else if (extensions.Contains(choix))
                {

                    Console.WriteLine("Début de l'export");
                    var driver = await LancerDriver();

                    ScanCartes(driver, choix, cartes);
                    await File.WriteAllTextAsync($"{choix}.json", JsonConvert.SerializeObject(cartes, Formatting.Indented));

                    driver.Quit();
                    driver.Dispose();

                }
                else
                {
                    Console.WriteLine($"Extension {choix} non reconnue");
                }
            }
            stopwatch.Stop();
        }

        public static async Task<ChromeDriver> LancerDriver()
        {
            new DriverManager().SetUpDriver(new ChromeConfig());
            ChromeDriverService service = ChromeDriverService.CreateDefaultService();
            service.SuppressInitialDiagnosticInformation = true;
            service.EnableVerboseLogging = false;
            var options = new ChromeOptions();
            options.AddArguments("--silent");
            var driver = new ChromeDriver(service, options);

            await driver.Navigate().GoToUrlAsync("https://www.pokecardex.com/forums/ucp.php?mode=login&redirect=index.php");
            Console.WriteLine();
            Console.WriteLine("---------------------------------------------------------------------------------------------");
            Console.WriteLine("En attente de l'affichage de la page https://www.pokecardex.com/collection après connexion...");
            Console.WriteLine("---------------------------------------------------------------------------------------------");
            Console.WriteLine();

            WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromMinutes(2));

            wait.Until(d => d.Url == "https://www.pokecardex.com/collection");
            return driver;
        }

        static void ScanCartes(ChromeDriver driver, string extension, List<Carte> cartes)
        {
            Console.WriteLine($"Export de l'extension {extension}...");
            int carteIndex = 1;
            int max = 0;
            var a = WaitUntilElementExists(driver, By.XPath("//*[@id=\"root\"]/div[1]/div/div[2]/div[1]/button"));
            a.Click();
            var b = WaitUntilElementExists(driver, By.CssSelector($"img[src*='{extension}.png']"));
            var parent = b.FindElement(By.XPath("../.."));
            var span = parent.FindElement(By.TagName("span"));
            var text = span.GetAttribute("textContent");

            var match = Regex.Match(text, @"\((\d+)/(\d+)\)");
            if (match.Success)
            {
                max = int.Parse(match.Groups[2].Value);
            }

            b.Click();

            var gridView = WaitUntilElementExists(driver, By.XPath("//*[@id=\"root\"]/div[2]/div/div/div/div"));

            var premiereCarte = gridView.FindElement(By.XPath("div[1]"));

            premiereCarte.Click();

            var modal = WaitUntilElementExists(driver, By.CssSelector("div[role='dialog'][data-state='open']"));

            //if (premierScan)
            //{
            //    var collection = WaitUntilElementExists(driver, By.XPath("//*[@id=\"tableaux\"]/div[1]/div/h6"));

            //    collection.Click();
            //    var doubles = WaitUntilElementExists(driver, By.XPath("//*[@id=\"tableaux\"]/div[4]/div/h6"));
            //    doubles.Click();

            //    premierScan = false;
            //}

            do
            {
                Carte carte = new(extension, carteIndex);

                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

                // 1. Attendre la modale
                var dialog = wait.Until(d =>
                    d.FindElement(By.CssSelector("div[role='dialog'][data-state='open']"))
                );

                var title = dialog.FindElement(By.XPath(".//span[contains(@class,'text-2xl')]")).Text;

                // 2. Récupérer les lignes du tableau (Ma collection)
                var lignes = dialog.FindElements(By.XPath(".//table//tbody/tr"));

                foreach (var ligne in lignes)
                {
                    var tds = ligne.FindElements(By.TagName("td"));

                    if (tds.Count < 4)
                        continue;

                    // 3. Version (colonne 2)
                    var version = tds[1].Text.Trim();

                    // 4. Quantité (colonne 4)
                    var quantiteText = tds[3].Text.Trim();
                    var quantite = int.Parse(quantiteText);

                    if (version.Contains("Normale"))
                    {
                        carte.QuantiteNormale += quantite;
                    }
                    else if (version.Contains("Reverse"))
                    {
                        carte.QuantiteReverse += quantite;
                    }
                }

                cartes.Add(carte);

                // 5. Bouton "Carte suivante"
                var nextBtn = dialog.FindElement(By.XPath(".//button[contains(., 'Carte suivante')]"));
                nextBtn.Click();

                // 6. Attendre que la carte change (super important sinon stale element)
                if (carteIndex + 1 < max)
                {
                    wait.Until(d =>
                            {
                                var newTitle = d.FindElement(By.XPath("//div[@role='dialog']//span[contains(@class,'text-2xl')]")).Text;
                                return newTitle != title;
                            });
                }

                carteIndex++;

            }
            while (carteIndex != max);

            var closeBtn = driver.FindElement(By.XPath(
                "//div[@role='dialog']//h2//button"
            ));

            closeBtn.Click();
            Console.WriteLine($"Export de l'extension {extension} terminé");
        }

        public static IWebElement WaitUntilElementExists(IWebDriver Driver, By elementLocator, int timeout = 10)
        {
            try
            {
                var wait = new WebDriverWait(Driver, TimeSpan.FromSeconds(timeout)) { PollingInterval = TimeSpan.FromMilliseconds(10) };
                return wait.Until(x => x.FindElement(elementLocator));
            }
            catch (NoSuchElementException)
            {
                Console.WriteLine("Element with locator: '" + elementLocator + "' was not found in current context page.");
                throw;
            }
        }

        public static IWebElement WaitUntilElementExists(IWebDriver Driver, IWebElement element, By elementLocator, int timeout = 10)
        {
            try
            {
                var wait = new WebDriverWait(Driver, TimeSpan.FromSeconds(timeout)) { PollingInterval = TimeSpan.FromMilliseconds(10) };
                return wait.Until(x => element.FindElement(elementLocator));
            }
            catch (NoSuchElementException)
            {
                Console.WriteLine("Element with locator: '" + elementLocator + "' was not found in current context page.");
                throw;
            }
        }

        public static void WaitForClass(IWebDriver driver, By by, string className, int timeoutSeconds = 10)
        {
            WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSeconds));

            wait.Until(drv =>
            {
                var element = drv.FindElement(by);
                string classes = element.GetDomAttribute("class");
                return classes != null && classes.Split(' ').Contains(className);
            });
        }

        public static void WaitForNextCard(IWebDriver driver)
        {
            WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
            wait.Until(d =>
            {
                var newDialog = d.FindElement(By.CssSelector("div[role='dialog'][data-state='open']"));
                var title = newDialog.FindElement(By.XPath(".//span[contains(@class,'text-2xl')]")).Text;
                return !string.IsNullOrWhiteSpace(title);
            });
        }

    }

    public record Carte(string Extension, int Numero)
    {
        public int QuantiteNormale { get; set; }
        public int QuantiteReverse { get; set; }
    }
}
