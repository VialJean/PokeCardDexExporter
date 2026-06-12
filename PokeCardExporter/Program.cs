using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Support.UI;
using System.Diagnostics;
using System.Text.RegularExpressions;
using WebDriverManager;
using WebDriverManager.DriverConfigs.Impl;

namespace PokeCardDexExporter
{
    internal partial class Program
    {
        // Intercepte fetch + XHR et stocke chaque réponse dans window.__pcxReponses
        private const string ScriptIntercepteur = """
            if (!window.__pcxIntercepteurActif) {
                window.__pcxIntercepteurActif = true;
                window.__pcxReponses = [];

                const _fetch = window.fetch;
                window.fetch = async function(...args) {
                    const res = await _fetch.apply(this, args);
                    try {
                        const url = typeof args[0] === 'string' ? args[0] : (args[0]?.url ?? '');
                        const body = await res.clone().text();
                        window.__pcxReponses.push({ url, body });
                    } catch(e) {}
                    return res;
                };

                const _open = XMLHttpRequest.prototype.open;
                const _send = XMLHttpRequest.prototype.send;
                XMLHttpRequest.prototype.open = function(method, url, ...rest) {
                    this.__pcxUrl = url;
                    return _open.apply(this, [method, url, ...rest]);
                };
                XMLHttpRequest.prototype.send = function(...args) {
                    this.addEventListener('load', () => {
                        if (this.status >= 200 && this.status < 300 && this.responseText)
                            window.__pcxReponses.push({ url: this.__pcxUrl ?? '', body: this.responseText });
                    });
                    return _send.apply(this, args);
                };
            }
        """;

        static async Task Main(string[] _)
        {
            var cartes = new List<Carte>();
            var sw = Stopwatch.StartNew();

            var driver = await LancerDriver();

            driver.ExecuteScript(ScriptIntercepteur);
            Console.WriteLine("Intercepteur réseau activé.");

            var series = await LireSeriesDepuisPage(driver);
            if (series.Count == 0)
            {
                Console.WriteLine("Impossible de lire les séries depuis la page de collection.");
                driver.Quit();
                return;
            }
            var toutesExtensions = series.SelectMany(s => s.Codes).Distinct().ToArray();

            Console.WriteLine("---------------------------------------------------------------------------------------------");
            for (int i = 0; i < series.Count; i++)
            {
                var (nom, codes) = series[i];
                Console.WriteLine($"  {i + 1}. {nom} : {string.Join("  ", codes)}");
            }
            Console.WriteLine($"* : Tout  |  1-{series.Count} : par série  |  MEG,POR : codes séparés par virgules");
            Console.WriteLine("---------------------------------------------------------------------------------------------");

            var choix = Console.ReadLine()?.Trim().ToUpper();
            if (string.IsNullOrEmpty(choix)) { driver.Quit(); return; }

            string[] extensionsAScanner;
            if (choix == "*")
            {
                extensionsAScanner = toutesExtensions;
            }
            else
            {
                var codesResolus = new List<string>();
                var invalides = new List<string>();
                foreach (var part in choix.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (int.TryParse(part, out var idx) && idx >= 1 && idx <= series.Count)
                        codesResolus.AddRange(series[idx - 1].Codes);
                    else if (toutesExtensions.Contains(part))
                        codesResolus.Add(part);
                    else
                        invalides.Add(part);
                }
                if (invalides.Count > 0)
                    Console.WriteLine($"Entrées non reconnues ignorées : {string.Join(", ", invalides)}");
                extensionsAScanner = [.. codesResolus.Distinct()];
                if (extensionsAScanner.Length == 0)
                {
                    Console.WriteLine("Aucune extension valide saisie.");
                    driver.Quit();
                    return;
                }
            }

            foreach (var ext in extensionsAScanner)
            {
                var cartesExt = new List<Carte>();
                await ScannerExtensionAsync(driver, ext, cartesExt);
                cartes.AddRange(cartesExt);
                await File.WriteAllTextAsync($"{ext}.json", JsonConvert.SerializeObject(cartesExt, Formatting.Indented));
                Console.WriteLine($"  → {ext}.json ({cartesExt.Count} cartes)");
            }

            if (extensionsAScanner.Length > 1)
            {
                await File.WriteAllTextAsync("collection.json", JsonConvert.SerializeObject(cartes, Formatting.Indented));
                Console.WriteLine($"\nExport complet en {sw.Elapsed.TotalSeconds:F1}s → collection.json ({cartes.Count} cartes)");
            }
            else
            {
                Console.WriteLine($"\nExport en {sw.Elapsed.TotalSeconds:F1}s → {extensionsAScanner[0]}.json ({cartes.Count} cartes)");
            }

            driver.Quit();
        }

        static async Task<EdgeDriver> LancerDriver()
        {
            new DriverManager().SetUpDriver(new EdgeConfig());
            var driver = new EdgeDriver();
            await driver.Navigate().GoToUrlAsync("https://www.pokecardex.com/forums/ucp.php?mode=login&redirect=index.php");

            Console.WriteLine();
            Console.WriteLine("---------------------------------------------------------------------------------------------");
            Console.WriteLine("Connectez-vous sur pokecardex.com, puis attendez la redirection vers /collection...");
            Console.WriteLine("---------------------------------------------------------------------------------------------");
            Console.WriteLine();

            new WebDriverWait(driver, TimeSpan.FromMinutes(5))
                .Until(d => d.Url.Contains("https://www.pokecardex.com/forums/index.php?sid"));

            await driver.Navigate().GoToUrlAsync("https://www.pokecardex.com/collection");

            return driver;
        }

        static async Task<List<(string Nom, string[] Codes)>> LireSeriesDepuisPage(EdgeDriver driver)
        {
            var bouton = Attendre(driver, By.CssSelector("button[aria-label='Sélectionner une série']"), 5);
            if (bouton == null) return [];

            driver.ExecuteScript("arguments[0].click();", bouton);
            await Task.Delay(500);

            if (Attendre(driver, By.CssSelector("div[role='dialog'][data-state='open']"), 5) == null)
                return [];

            // Extraire les séries et leurs extensions via JS
            // Chaque bloc série dans la dialog = div avec h2 (nom) + images symboles (codes)
            var raw = driver.ExecuteScript("""
                return Array.from(document.querySelectorAll(
                    "div[role='dialog'][data-state='open'] .space-y-4 > div"
                )).map(block => {
                    const h2 = block.querySelector('h2');
                    if (!h2) return null;
                    const codes = Array.from(block.querySelectorAll("img[src*='symboles'][alt]"))
                        .map(img => img.alt).filter(Boolean);
                    return codes.length > 0 ? { nom: h2.textContent.trim(), codes } : null;
                }).filter(Boolean);
                """);

            var result = new List<(string Nom, string[] Codes)>();
            if (raw is IList<object> rawList)
            {
                foreach (var item in rawList)
                {
                    if (item is not IDictionary<string, object> dict) continue;
                    var nom = dict["nom"]?.ToString() ?? "";
                    var codes = (dict["codes"] as IList<object>)
                        ?.Select(c => c?.ToString() ?? "").Where(c => c.Length > 0).ToArray() ?? [];
                    if (nom.Length > 0 && codes.Length > 0)
                        result.Add((nom, codes));
                }
            }

            // Fermer la dialog sans sélectionner
            try { driver.FindElement(By.TagName("body")).SendKeys(Keys.Escape); }
            catch { driver.ExecuteScript("document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}));"); }

            await Task.Delay(300);
            return result;
        }

        // ─── Scanner principal ────────────────────────────────────────────────────

        static async Task ScannerExtensionAsync(EdgeDriver driver, string extension, List<Carte> cartes)
        {
            Console.WriteLine($"\n[{extension}] Démarrage...");

            driver.ExecuteScript("window.__pcxReponses = [];");

            // Ouvrir le sélecteur de série (nouveau UI : un seul bouton déroulant)
            var boutonSerie = Attendre(driver, By.CssSelector("button[aria-label='Sélectionner une série']"));
            if (boutonSerie == null)
            {
                Console.WriteLine($"[{extension}] Bouton de sélection de série introuvable.");
                return;
            }
            driver.ExecuteScript("arguments[0].click();", boutonSerie);

            // La sélection s'ouvre dans une dialog Radix UI.
            // Chaque bouton d'extension contient une image symbole avec alt = code extension (ex: alt="POR").
            await Task.Delay(500); // Petite pause pour laisser le temps à la dialog de s'ouvrir et au DOM de se stabiliser

            var btnExtension = Attendre(driver, By.XPath(
                $"//div[@role='dialog'][@data-state='open']//button[.//img[@alt='{extension}']]"
            ));
            if (btnExtension == null)
            {
                Console.WriteLine($"[{extension}] Extension non trouvée dans la liste déroulante.");
                return;
            }
            btnExtension.Click();

            // Lire le total depuis le texte mis à jour du bouton de sélection
            int total = LireTotalDepuisBouton(driver);
            Console.WriteLine($"[{extension}] {total} cartes attendues.");

            await ScannerModales(driver, extension, cartes, total);
        }

        // ─── Lecture des réponses capturées ──────────────────────────────────────

        static List<(string Url, string Body)> RecupererReponses(EdgeDriver driver)
        {
            var result = new List<(string, string)>();
            if (driver.ExecuteScript("return window.__pcxReponses.map(r => [r.url, r.body]);") is not IList<object> raw)
                return result;

            foreach (var item in raw)
            {
                if (item is IList<object> pair && pair.Count >= 2)
                    result.Add((pair[0]?.ToString() ?? "", pair[1]?.ToString() ?? ""));
            }
            return result;
        }

        // ─── Parsing de réponse API (patterns courants) ───────────────────────────

        static List<Carte> TenterParserReponse(string body, string extension)
        {
            if (string.IsNullOrWhiteSpace(body)) return [];

            JToken json;
            try { json = JToken.Parse(body); }
            catch { return []; }

            // Pattern 1 : tableau à la racine
            if (json is JArray rootArray)
            {
                var cartes = ExtraireDepuisArray(rootArray, extension);
                if (cartes.Count > 0) return cartes;
            }

            // Pattern 2 : objet avec une clé contenant un tableau
            if (json is JObject obj)
            {
                foreach (var cle in new[] { "data", "cards", "cartes", "collection", "items", "results" })
                {
                    if (obj[cle] is JArray arr)
                    {
                        var cartes = ExtraireDepuisArray(arr, extension);
                        if (cartes.Count > 0) return cartes;
                    }

                    if (obj[cle] is JObject nested)
                    {
                        foreach (var sousCle in new[] { "cards", "cartes", "items" })
                        {
                            if (nested[sousCle] is JArray subArr)
                            {
                                var cartes = ExtraireDepuisArray(subArr, extension);
                                if (cartes.Count > 0) return cartes;
                            }
                        }
                    }
                }
            }

            return [];
        }

        static List<Carte> ExtraireDepuisArray(JArray array, string extension)
        {
            var cartes = new List<Carte>();

            foreach (var item in array)
            {
                if (item is not JObject obj) continue;

                int numero = obj["numero"]?.Value<int>()
                    ?? obj["number"]?.Value<int>()
                    ?? obj["num"]?.Value<int>()
                    ?? 0;

                if (numero == 0) continue;

                var carte = new Carte(extension, numero)
                {
                    Nom = obj["nom"]?.Value<string>() ?? obj["name"]?.Value<string>() ?? string.Empty,
                    Rarete = obj["rarete"]?.Value<string>() ?? obj["rarity"]?.Value<string>() ?? string.Empty,
                };

                // Pattern A : { versions: [{ nom: "Normale", quantite: 2 }] }
                if (obj["versions"] is JArray versions)
                {
                    foreach (var v in versions)
                    {
                        var vNom = v["nom"]?.Value<string>() ?? v["name"]?.Value<string>() ?? "?";
                        var vQte = v["quantite"]?.Value<int>() ?? v["quantity"]?.Value<int>() ?? 0;
                        if (vQte > 0) carte.Quantites[vNom] = vQte;
                    }
                }
                // Pattern B : { quantiteNormale: 1, quantiteReverse: 0 }
                else
                {
                    (string Label, string[] Cles)[] mappings = [
                        ("Normale", ["quantiteNormale", "normalQty", "normal"]),
                        ("Reverse", ["quantiteReverse", "reverseQty", "reverse"]),
                    ];
                    foreach (var (label, cles) in mappings)
                    {
                        foreach (var cle in cles)
                        {
                            var qty = obj[cle]?.Value<int>();
                            if (qty is > 0) { carte.Quantites[label] = qty.Value; break; }
                        }
                    }
                }

                cartes.Add(carte);
            }

            return cartes;
        }

        static int LireTotalDepuisBouton(EdgeDriver driver)
        {
            try
            {
                int total = 0;
                new WebDriverWait(driver, TimeSpan.FromSeconds(5)) { PollingInterval = TimeSpan.FromMilliseconds(100) }
                    .Until(d =>
                    {
                        var btn = d.FindElement(By.CssSelector("button[aria-label='Sélectionner une série']"));
                        var m = RegexTotal().Match(btn.GetAttribute("textContent") ?? "");
                        if (m.Success) { total = int.Parse(m.Groups[2].Value); return true; }
                        return false;
                    });
                return total;
            }
            catch { return 0; }
        }

        // ─── Fallback : scan par modales ──────────────────────────────────────────

        static async Task ScannerModales(EdgeDriver driver, string extension, List<Carte> cartes, int total)
        {
            Console.WriteLine($"[{extension}] Scan par modales ({total} cartes)...");

            if (Attendre(driver, By.CssSelector("div.grid[class*='grid-cols']")) == null)
            {
                Console.WriteLine($"[{extension}] Grille introuvable.");
                return;
            }

            await Task.Delay(800);

            // Un item de grille par carte ; on ne traite que les cartes possédées
            var items = driver.FindElements(By.CssSelector("div.grid[class*='grid-cols'] > div"));
            Console.WriteLine($"[{extension}] {items.Count} cartes dans la grille.");

            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10)) { PollingInterval = TimeSpan.FromMilliseconds(50) };
            int scannees = 0;

            foreach (var item in items)
            {
                try
                {
                    Console.WriteLine($"[{extension}] Carte {scannees + 1}/{total}...");
                    // Ignorer les cartes non possédées : tous les boutons de version ont opacity-
                    // var vImgs = item.FindElements(By.XPath(".//button[contains(@class,'size-6')]//img[@alt]"));
                    // if (vImgs.Count > 0 && vImgs.All(img => (img.GetAttribute("class") ?? "").Contains("opacity-")))
                    //     continue;

                    // Ouvrir la dialog via le bouton image (class "group")
                    var cardBtn = item.FindElement(By.CssSelector("button.group"));
                    driver.ExecuteScript("arguments[0].scrollIntoView({block:'center'});", cardBtn);
                    await Task.Delay(100);
                    driver.ExecuteScript("arguments[0].click();", cardBtn);

                    // Attendre l'ouverture
                    IWebElement dialog;
                    try { dialog = wait.Until(d => d.FindElement(By.CssSelector("div[role='dialog'][data-state='open']"))); }
                    catch { continue; }

                    var carte = LireCarteDepuisDialog(dialog, extension, scannees + 1);
                    if (carte != null) { cartes.Add(carte); scannees++; }

                    // Fermer la dialog (bouton aria-label="Fermer")
                    try { dialog.FindElement(By.CssSelector("button[aria-label='Fermer']")).Click(); }
                    catch { driver.ExecuteScript("arguments[0].dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}));", dialog); }

                    // Attendre la fermeture avant la carte suivante
                    try { wait.Until(d => d.FindElements(By.CssSelector("div[role='dialog'][data-state='open']")).Count == 0); }
                    catch { await Task.Delay(300); }
                }
                catch { }
            }

            Console.WriteLine($"[{extension}] {scannees} cartes scannées (sur {items.Count}).");
        }

        static Carte? LireCarteDepuisDialog(IWebElement dialog, string extension, int fallbackIndex)
        {
            try
            {
                // Nom : bouton text-2xl dans le header desktop
                string nom = "";
                try { nom = dialog.FindElement(By.XPath(".//button[contains(@class,'text-2xl')]")).Text.Trim(); }
                catch { }
                if (string.IsNullOrEmpty(nom))
                    try { nom = dialog.FindElement(By.XPath(".//h2[@class='sr-only']")).Text.Trim(); }
                    catch { }

                // Numéro : span "– NNN/MMM"
                int numero = fallbackIndex;
                try
                {
                    var spanNum = dialog.FindElement(By.XPath(
                        ".//span[contains(@class,'text-xl') and contains(@class,'font-semibold')]"));
                    var m = RegexNumero().Match(spanNum.Text);
                    if (m.Success) numero = int.Parse(m.Groups[1].Value);
                }
                catch { }

                // Rareté : img dont le src contient 'rarete'
                string rarete = "";
                try { rarete = dialog.FindElement(By.XPath(".//img[contains(@src,'rarete')]")).GetAttribute("alt") ?? ""; }
                catch { }

                string lienCardMarket = "";
                try { lienCardMarket = dialog.FindElement(By.XPath(".//a[contains(@href,'cardmarket.com')]")).GetAttribute("href") ?? ""; }
                catch { }

                var carte = new Carte(extension, numero) { Nom = nom, Rarete = rarete, LienCardMarket = lienCardMarket };

                // Versions possédées : chaque <section> correspond à une version
                // Le compteur est dans un span text-center font-medium à l'intérieur d'une ligne border-t
                foreach (var section in dialog.FindElements(By.TagName("section")))
                {
                    try
                    {
                        string vNom = "";
                        try { vNom = section.FindElement(By.XPath(".//img[contains(@class,'size-5')][@alt]")).GetAttribute("alt") ?? ""; }
                        catch { continue; }
                        if (string.IsNullOrEmpty(vNom)) continue;

                        int qty = 0;
                        foreach (var span in section.FindElements(By.XPath(
                            ".//div[contains(@class,'border-t')]//span[contains(@class,'text-center') and contains(@class,'font-medium')]")))
                        {
                            if (int.TryParse(span.Text.Trim(), out var n) && n > 0) qty += n;
                        }

                        carte.Quantites[vNom] = qty;
                    }
                    catch { }
                }

                return carte;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Erreur lecture dialog: {ex.Message}");
                return null;
            }
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────



        [GeneratedRegex(@"(\d+)/\d+")]
        private static partial Regex RegexNumero();

        [GeneratedRegex(@"\((\d+)/(\d+)\)")]
        private static partial Regex RegexTotal();

        static IWebElement? Attendre(IWebDriver driver, By by, int timeoutSec = 10)
        {
            try
            {
                return new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSec)) { PollingInterval = TimeSpan.FromMilliseconds(50) }
                    .Until(d => d.FindElement(by));
            }
            catch { return null; }
        }
    }

    public record Carte(string Extension, int Numero)
    {
        public string Nom { get; set; } = string.Empty;
        public string Rarete { get; set; } = string.Empty;
        public Dictionary<string, int> Quantites { get; set; } = [];
        public string LienCardMarket { get; set; } = string.Empty;

    }
}
