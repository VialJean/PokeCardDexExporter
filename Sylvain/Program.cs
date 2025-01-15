using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
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
    internal partial class Program
    {
        const string webhookUrl = "https://discord.com/api/webhooks/1328486528657395803/sr-jfgq2c-WlnQOgzxoTL1zEil3xxtz2w7LpCHc3veCmISMS6Q4Dd-f5o7fX28rGhqGJ";

        static async Task Main(string[] args)
        {
            using (var context = new AppDbContext())
            {
                // Appliquer les migrations (crée la base de données si elle n'existe pas)
                context.Database.EnsureCreated();
                Console.WriteLine("Base de données initialisée.");

                var options = new ChromeOptions();
                options.AddArgument("--headless"); // Mode headless pour ne pas afficher le navigateur
                options.AddArgument("--disable-gpu");
                options.AddArgument("--no-sandbox");

                await SendDiscordMessage(webhookUrl, "Démarrage du scan V2");

                using (IWebDriver driver = new ChromeDriver(options))
                {
                    var produits = await ScanProductsV2(driver, "https://www.relictcg.com/collections/pokemon");
                    //var produits = await context.Products.ToListAsync();

                    foreach (var produit in produits)
                    {
                        var produitInDb = context.Products.SingleOrDefault(x => x.Site == produit.Site && x.Titre == produit.Titre);
                        if (produitInDb == null)
                        {
                            await context.AddAsync(produit);
                            await context.SaveChangesAsync();

                        }
                    }
                    await SendDiscordMessage(webhookUrl, produits.Where(x=>x.Etat==Etat.En_stock));

                }
                await SendDiscordMessage(webhookUrl, "Fin du scan V2");
            }

            Console.ReadLine();
        }

        private static async Task SendDiscordMessage(string webhookUrl, IEnumerable<Produit> produits)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    var batches = produits.Chunk(10);

                    foreach (var batch in batches)
                    {
                        var payload = new
                        {
                            embeds = batch.Select(produit => new
                            {
                                title = produit.Site,
                                description = produit.Titre,
                                //color = 16711680, // Rouge en RGB décimal
                                fields = new[]
                                {
                                new { name = "Prix", value = produit.Prix, inline = false },
                                new { name = "Stock", value = produit.Etat == Etat.En_stock ? "En stock" : "Épuisé", inline = false },
                            },
                                url = produit.Url,
                                image = new
                                {
                                    url = produit.Image
                                },
                                footer = new
                                {
                                    text = $"Scan effectué à {DateTime.Now:T}"
                                }
                            })

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
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                Debug.WriteLine(ex.Message);
            }
        }

        static async Task<Produit?> ScanProducts(IWebDriver driver, string searchUrl)
        {
            string title = string.Empty;

            try
            {
                driver.SwitchTo().NewWindow(WindowType.Window);

                await driver.Navigate().GoToUrlAsync(searchUrl);

                var titleElement = driver.FindElement(By.CssSelector("h1.product__title"));

                if (titleElement == null)
                {
                    Console.WriteLine($"Titre introuvable");
                }
                else
                {
                    title = titleElement?.Text;
                }

                var buyButtonElement = driver.FindElement(By.ClassName("product-form__submit"));
                var etat = buyButtonElement.Enabled ? "En stock" : "Epuisé";
                Etat etatEnum = buyButtonElement.Enabled ? Etat.En_stock : Etat.Epuise;

                var priceElement = driver.FindElement(By.ClassName("price-item"));
                var price = priceElement?.Text;

                string image = string.Empty;
                if (IsElementPresent(driver,By.ClassName("slide_nav")))
                {
                    var imageElement = driver.FindElement(By.ClassName("slide_nav"));
                    var a = imageElement.FindElement(By.TagName("img"));
                    image = "https:" + a.GetDomAttribute("src");
                }
                else
                {
                    var imageElement = driver.FindElement(By.ClassName("image_zoom_box_src"));
                    image = "https:" + imageElement.GetDomAttribute("src");
                }

                

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

                    return new Produit(Guid.NewGuid(), title, etatEnum, price, searchUrl, image, siteName);
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

                    if (IsElementPresent(driver, By.CssSelector("a.snize-pagination-next")))
                    {
                        var nextButtonElement = driver.FindElement(By.CssSelector("a.snize-pagination-next"));
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

        public static bool IsElementPresent(IWebDriver driver, By by)
        {
            return driver.FindElements(by).Count > 0;
        }
    }
}
