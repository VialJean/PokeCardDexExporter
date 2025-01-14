using HtmlAgilityPack;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;
using System;
using System.Diagnostics;
using System.Text;
using static System.Net.WebRequestMethods;

namespace Sylvain
{
    internal class Program
    {
        const string webhookUrl = "https://discord.com/api/webhooks/1328486528657395803/sr-jfgq2c-WlnQOgzxoTL1zEil3xxtz2w7LpCHc3veCmISMS6Q4Dd-f5o7fX28rGhqGJ";

        static async Task Main(string[] args)
        {
            string[] urls = [
                "https://www.relictcg.com/collections/nouveautes-pokemon/products/coffret-ultra-premium-amphinobi-ex-fr",
                "https://www.relictcg.com/products/display-faille-paradoxe-ev04-fr",
                "https://www.relictcg.com/products/coffret-build-battle-stadium-ecarlate-et-violet-faille-paradoxe-ev04-fr",
                "https://www.relictcg.com/products/coffret-rugit-lune-ex-faille-paradoxe-ev04-fr",
                "https://www.relictcg.com/products/coffret-garde-de-fer-ex-faille-paradoxe-ev04-fr",
                "https://www.relictcg.com/products/coffret-dogrino-ex-faille-paradoxe-ev04-fr",
                "https://www.relictcg.com/products/tri-pack-ecarlate-et-violet-faille-paradoxe-ev04-fr?variant=47244983075161",
                "https://www.relictcg.com/products/copie-de-coffret-dresseur-delite-faille-paradoxe-ev04-fr"
            ];
            string message = "Voici le titre du produit trouvé : Display - Écarlate et Violet - Faille Paradoxe [EV04] - FR";

            var options = new ChromeOptions();
            options.AddArgument("--headless"); // Mode headless pour ne pas afficher le navigateur
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");

            await SendDiscordMessage(webhookUrl, "Démarrage du scan V2");

            using (IWebDriver driver = new ChromeDriver(options))
            {
                var produits = await ScanProductsV2(driver, "https://www.relictcg.com/collections/pokemon");
                foreach (var produit in produits)
                {
                    await SendDiscordMessage(webhookUrl, produit);
                }
            }

            await SendDiscordMessage(webhookUrl, "Fin du scan V2");

            Console.ReadLine();
        }

        static async Task<Produit?> ScanProducts(IWebDriver driver, string searchUrl)
        {

            try
            {
                driver.SwitchTo().NewWindow(WindowType.Window);

                await driver.Navigate().GoToUrlAsync(searchUrl);

                var titleElement = driver.FindElement(By.CssSelector("h1.product__title"));
                string? title = titleElement?.Text;

                var buyButtonElement = driver.FindElement(By.ClassName("product-form__submit"));
                var etat = buyButtonElement.Enabled ? "En stock" : "Epuisé";
                Etat etatEnum = buyButtonElement.Enabled ? Etat.En_stock : Etat.Epuise;

                var priceElement = driver.FindElement(By.ClassName("price-item"));
                var price = priceElement?.Text;

                var imageElement = driver.FindElement(By.ClassName("image_zoom_box_src"));
                var image = "https:" + imageElement.GetDomAttribute("src");

                Uri uri = new Uri(searchUrl);
                string host = uri.Host;
                string siteName = host.StartsWith("www.") ? host.Substring(4) : host;

                driver.Close();
                driver.SwitchTo().Window(driver.WindowHandles[0]);

                if (title != null && buyButtonElement != null && price != null)
                {
                    Console.WriteLine($"Produit : {title}");
                    Console.WriteLine($"Prix : {price}");
                    Console.WriteLine($"Etat : {etat}");

                    return new Produit(title, etatEnum, price, searchUrl, image, siteName);
                }
                else
                {
                    Console.WriteLine("Aucun produit correspondant trouvé avec ce sélecteur XPath.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors du scan : {ex.Message}");
            }

            return null;
        }
        static async Task<ICollection<Produit>> ScanProductsV2(IWebDriver driver, string searchUrl)
        {
            List<Produit> liste = [];
            try
            {
                Uri uri = new Uri(searchUrl);
                string host = uri.Host;
                await driver.Navigate().GoToUrlAsync(searchUrl);

                bool fin = false;
                do
                {
                    Debug.WriteLine($"Scan de la page {driver.Url}");
                    string urlActuelle = driver.Url;

                    var listElements = driver.FindElement(By.CssSelector("ul.snize-search-results-content"));

                    foreach (var element in listElements.FindElements(By.CssSelector("li")))
                    {
                        var linkElement = element.FindElement(By.ClassName("snize-view-link"));
                        if (linkElement != null)
                        {
                            var link = uri.GetLeftPart(UriPartial.Scheme) + host + linkElement.GetDomAttribute("href");


                            var produit = await ScanProducts(driver, link);
                            if (produit != null)
                            {
                                liste.Add(produit);

                            }

                        }
                    }

                    var nextButtonElement = driver.FindElement(By.CssSelector("a.snize-pagination-next"));
                    if (nextButtonElement != null)
                    {
                        nextButtonElement.Click();
                        // Attendre la fin de la redirection (par exemple, attendre un changement d'URL)
                        WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
                        wait.Until(d => d.Url != urlActuelle);
                    }
                    else
                    {
                        fin = true;
                    }
                } while (!fin);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors du scan : {ex.Message}");
            }

            return liste;
        }

        static async Task SendDiscordMessage(string webhookUrl, Produit produit)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    var payload = new
                    {
                        embeds = new[]
                {
                new
                {
                    title = produit.Site,
                    description = produit.Titre,
                    //color = 16711680, // Rouge en RGB décimal
                    fields = new[]
                    {
                        new { name = "Prix", value = produit.Prix, inline = false },
                        new { name = "Stock", value = produit.Etat == Etat.En_stock ? "En stock" : "Épuisé", inline = false },

                    },
                    url=produit.Url,
                    image = new
                    {
                        url=produit.Image
                    },
                    footer = new
                    {
                        text = $"Scan effectué à {DateTime.Now:T}"
                    }
                }
            }
                    };

                    var jsonPayload = new StringContent(
                        Newtonsoft.Json.JsonConvert.SerializeObject(payload),
                        Encoding.UTF8,
                        "application/json"
                    );

                    HttpResponseMessage response = await client.PostAsync(webhookUrl, jsonPayload);

                    response.EnsureSuccessStatusCode();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                Debug.WriteLine(ex.Message);
            }

        }

        static async Task SendDiscordMessage(string webhookUrl, string message)
        {
            using (HttpClient client = new HttpClient())
            {
                var payload = new
                {
                    content = message// Le contenu du message
                };

                var jsonPayload = new StringContent(
                    Newtonsoft.Json.JsonConvert.SerializeObject(payload),
                    Encoding.UTF8,
                    "application/json"
                );

                HttpResponseMessage response = await client.PostAsync(webhookUrl, jsonPayload);

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Échec de la requête. Code statut : {response.StatusCode}");
                }
            }
        }

        record Produit(string Titre, Etat Etat, string Prix, string Url, string Image, string Site);

        public enum Etat
        {
            En_stock,
            Epuise
        }
    }
}
