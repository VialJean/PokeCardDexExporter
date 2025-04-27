using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Diagnostics;
using WebDriverManager;
using WebDriverManager.DriverConfigs.Impl;

namespace PokeCardDexExporter
{
    internal partial class Program
    {
        private static string[] extensions = [
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
            WaitUntilElementExists(driver, By.XPath("//*[@id=\"selection-serie\"]"));
            var a = driver.FindElement(By.XPath("//*[@id=\"selection-serie\"]"));
            a.Click();
            WaitUntilElementExists(driver, By.XPath($"//*[@id=\"{extension}\"]"));

            var b = driver.FindElement(By.XPath($"//*[@id=\"{extension}\"]"));
            int max = int.Parse(b.GetDomAttribute("data-total"));

            b.Click();

            var gridView = WaitUntilElementExists(driver, By.XPath("//*[@id=\"grid_view\"]"));

            var premiereCarte = gridView.FindElement(By.XPath("div[1]"));

            premiereCarte.Click();

            WaitForClass(driver, By.XPath("//*[@id=\"modalDetailsCarte\"]"), "show");

            if (premierScan)
            {
                var collection = WaitUntilElementExists(driver, By.XPath("//*[@id=\"tableaux\"]/div[1]/div/h6"));

                collection.Click();
                var doubles = WaitUntilElementExists(driver, By.XPath("//*[@id=\"tableaux\"]/div[4]/div/h6"));
                doubles.Click();

                premierScan = false;
            }

            do
            {
                Carte carte = new(extension, carteIndex);
                string carteId = WaitUntilElementExists(driver, By.XPath("//*[@id=\"card-id-badge\"]")).Text;

                var table = WaitUntilElementExists(driver, By.XPath($"//*[@id=\"table-collection-{carteId}\"]"));

                var lignes = table.FindElements(By.TagName("tr"));

                WaitForClass(driver, By.XPath("//*[@id=\"card-collection\"]"), "show");

                if (lignes.Count > 1)
                {
                    foreach (var ligne in lignes.Skip(1))
                    {
                        string quantiteText = WaitUntilElementExists(driver, element: ligne, By.ClassName("quantite")).Text;
                        var quantite = int.Parse(quantiteText);
                        var version = WaitUntilElementExists(Driver: driver, element: ligne, By.ClassName("version")).Text;

                        if (version == "Normale")
                        {
                            carte.QuantiteNormale = quantite;
                        }
                        else if (version == "Reverse")
                        {
                            carte.QuantiteReverse = quantite;
                        }
                    }
                }

                table = WaitUntilElementExists(driver, By.XPath($"//*[@id=\"table-possessions-{carteId}\"]"));

                lignes = table.FindElements(By.TagName("tr"));

                if (lignes.Count > 1)
                {
                    foreach (var ligne in lignes.Skip(1))
                    {
                        string quantiteText = WaitUntilElementExists(driver, element: ligne, By.ClassName("quantite")).Text;
                        var quantite = int.Parse(quantiteText);
                        var version = WaitUntilElementExists(Driver: driver, element: ligne, By.ClassName("version")).Text;

                        if (version == "Normale")
                        {
                            carte.QuantiteNormale += quantite;
                        }
                        else if (version == "Reverse")
                        {
                            carte.QuantiteReverse += quantite;
                        }
                    }
                }


                cartes.Add(carte);

                driver.FindElement(By.XPath("//*[@id=\"next-card\"]")).Click();
                carteIndex++;
            }
            while (carteIndex != max);

            driver.FindElement(By.XPath("//*[@id=\"modalDetailsCarte\"]/div/div/div[1]/button")).Click();

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

    }

    public record Carte(string Extension, int Numero)
    {
        public int QuantiteNormale { get; set; }
        public int QuantiteReverse { get; set; }
    }
}
