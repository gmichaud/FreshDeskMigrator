using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Polly;
using Polly.Retry;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FreshDeskMigrator
{
    internal class Program
    {
        /*
         * UPDATE: j’ai réussi à faire un parser qui clean le HTML d’Intercom/FreshDesk 
         * en enlevant en paquet de scrap, je met les anchor pour les réf locale, 
         * je fix les URL des images vers le CDN… 
         * il va rester à corriger les références vers les articles à l’intérieur du site, 
         * faire l’arborescence (j’ai une stratégie aussi en tête), 
         * et bâtir un look-up table entre les anciens et nouveaux articles 
         * en vue de règles de redirection… et je vais sûrement trouver d’autres choses à nettoyer.
         * + tags Intacct, Classic, Acumatica, etc...
        */

        private const string SpaceId = "25001988";
        private const string RootId = "24739848";

        private const string Url = "https://velixo.atlassian.net";

        private static HttpClient _client = new();
        private static ResiliencePipeline<HttpResponseMessage> _resiliencePipeline = null;

        private static HashSet<string> _titles = new();
        private static Dictionary<long, FreshDeskArticle> _freshDeskArticles = new();
        private static List<ConfluenceArticle> _confluenceArticles = new();

        private static Dictionary<long, Category> _categories = new();

        private static Dictionary<long, ConfluenceMap> _articlesMap = new();
        private static Dictionary<long, ConfluenceMap> _categoriesMap = new();
        private static Dictionary<long, ConfluenceMap> _foldersMap = new();


        static async Task Main()
        {
            ConfigurationBuilder configurationBuilder = new ConfigurationBuilder();
            IConfiguration configuration = configurationBuilder.AddUserSecrets<Program>().Build();
            var secrets = configuration.GetSection("atlassian");

            // Set up basic authentication by encoding username and password
            var authToken = Encoding.ASCII.GetBytes($"{secrets["username"]}:{secrets["password"]}");
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authToken));
            _client.Timeout = new TimeSpan(0, 30, 0);

            // define the retry policy with delay in Polly
            var pipelineOptions = new RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .HandleResult(r => r.StatusCode == HttpStatusCode.TooManyRequests), // retry on 429 errors
                MaxRetryAttempts = 7, // up to 7 attempts
                Delay = TimeSpan.FromSeconds(5) // 5-second delay
            };

            _resiliencePipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
                .AddRetry(pipelineOptions) // add retry logic with custom options
                .Build(); // build the resilience pipeline

            //LoadArticles();

            //await CreatePageStructure();
            //System.IO.File.WriteAllText("categories.json", JsonSerializer.Serialize(_categoriesMap.Values));
            //System.IO.File.WriteAllText("folders.json", JsonSerializer.Serialize(_foldersMap.Values));

            //await MigrateArticle();
            //System.IO.File.WriteAllText("articles.json", JsonSerializer.Serialize(_articlesMap.Values));

            _articlesMap = JsonSerializer.Deserialize<ConfluenceMap[]>(System.IO.File.ReadAllText("articles.json")).ToDictionary(x => x.Id);
            _categoriesMap = JsonSerializer.Deserialize<ConfluenceMap[]>(System.IO.File.ReadAllText("categories.json")).ToDictionary(x => x.Id);
            _foldersMap = JsonSerializer.Deserialize<ConfluenceMap[]>(System.IO.File.ReadAllText("folders.json")).ToDictionary(x => x.Id);
            await AdjustAndUpdateArticleContent();
        }

        private static void LoadArticles()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            foreach (string file in System.IO.Directory.GetFiles(@"C:\Mac\Home\Desktop\FreshDesk articles\", "*.json").Order())
            {
                string jsonString = File.ReadAllText(file);
                var article = JsonSerializer.Deserialize<FreshDeskArticle>(jsonString, options);
                _freshDeskArticles.Add(article.Id, article);

                Category category;
                if (!_categories.TryGetValue(article.Hierarchy[0].Data.Id, out category))
                {
                    category = new Category() { Id = article.Hierarchy[0].Data.Id, Name = article.Hierarchy[0].Data.Name };
                    _categories.Add(category.Id, category);
                }

                Folder folder;
                if (!category.Folders.TryGetValue(article.Hierarchy[1].Data.Id, out folder))
                {
                    folder = new Folder() { Id = article.Hierarchy[1].Data.Id, Name = article.Hierarchy[1].Data.Name };
                    category.Folders.Add(folder.Id, folder);
                }

                if (article.Hierarchy.Count > 2)
                {
                    Folder subfolder;
                    if (!category.Folders.TryGetValue(article.Hierarchy[2].Data.Id, out subfolder))
                    {
                        subfolder = new Folder() { Id = article.Hierarchy[2].Data.Id, Name = article.Hierarchy[2].Data.Name, Parent = folder };
                        category.Folders.Add(subfolder.Id, subfolder);
                    }
                }
            }
        }

        private static async Task CreatePageStructure()
        {
            //Hardcode labels
            _categories[153000013633].Labels = new string[] { "intacct" }; //Sage Intacct
            _categories[153000013634].Labels = new string[] { "acumatica" }; //Acumatica, MYOB, CEGID
            _categories[153000019148].Labels = new string[] { "classic" }; //Velixo Classic
            _categories[153000011067].Labels = new string[] { "acumatica", "intacct" }; //Velixo NX
            _categories[153000023647].Labels = new string[] { "general" }; //General

            foreach (var category in _categories.Values)
            {
                Console.WriteLine("Category: " + category.Id + " " + category.Name);
                var categoryArticle = await CreateArticle(new ConfluenceArticle()
                {
                    SpaceID = SpaceId,
                    ParentID = RootId,
                    Title = category.Name,
                    Status = "current"
                }, string.Empty);

                _categoriesMap.Add(category.Id, new ConfluenceMap() { Id = category.Id, ConfluenceArticle = categoryArticle });
                category.ConfluenceId = categoryArticle.ID;

                await AddLabels(category.ConfluenceId, category.Labels);

                foreach (var folder in category.Folders.Values)
                {
                    Console.WriteLine("Folder: " + folder.Id + " " + folder.Name);
                    var folderArticle = await CreateArticle(new ConfluenceArticle()
                    {
                        SpaceID = SpaceId,
                        ParentID = folder.Parent != null ? folder.Parent.ConfluenceId : category.ConfluenceId,
                        Title = folder.Name,
                        Status = "current"
                    }, folder.Parent != null ? folder.Parent.Name : category.Name);

                    _foldersMap.Add(folder.Id, new ConfluenceMap() { Id = folder.Id, ConfluenceArticle = categoryArticle });
                    folder.ConfluenceId = folderArticle.ID;
                    await AddLabels(folder.ConfluenceId, category.Labels);
                }
            }
        }

        private static async Task MigrateArticle()
        {
            int i = 0;
            foreach (var article in _freshDeskArticles.Values)
            {
                i++;
                Console.WriteLine("(" + i + "/" + _freshDeskArticles.Count + ") " + article.Id + ": " + article.Title);

                var category = _categories[article.Hierarchy[0].Data.Id];
                Folder folder;
                if (article.Hierarchy.Count > 2)
                {
                    folder = category.Folders[article.Hierarchy[2].Data.Id];
                }
                else
                {
                    folder = category.Folders[article.Hierarchy[1].Data.Id];
                }

                var confluenceArticle = await CreateArticle(new ConfluenceArticle()
                {
                    SpaceID = SpaceId,
                    ParentID = folder.ConfluenceId,
                    Title = article.Title,
                    Status = "current",
                    Body = new Body()
                    {
                        Storage = new Storage()
                        {
                            Representation = "storage",
                            Value = CleanHtml(article.Description)
                        }
                    }
                }, folder.Name);

                _articlesMap.Add(article.Id, new ConfluenceMap() { Id = article.Id, ConfluenceArticle = confluenceArticle });
                await AddLabels(confluenceArticle.ID, category.Labels);
            }
        }

        private static async Task AdjustAndUpdateArticleContent()
        {
            int i = 0;
            foreach (var article in _articlesMap.Values)
            {
                i++;
                Console.WriteLine("(" + i + "/" + _articlesMap.Count + ") " + article.Id + ": " + article.ConfluenceArticle.Title);

                ConfluenceArticle currentArticle = await GetArticle(article.ConfluenceArticle.ID);
                currentArticle.Body.Storage.Value = FixHelpDeskLinksAndDoFinalCleanup(article.ConfluenceArticle.Body.Storage.Value);
                currentArticle.Version.Number++;
                currentArticle.Version.Message = "Updated links";

                var afterUpdate = await UpdateArticle(currentArticle);
            }
        }

        private static void BuildRedirects()
        {
            /*Build redirects for Viewport
            literal /my-docs/my-page.123456.html /my-prod/another-page.654321.html temporary
            literal /my-docs/a-page.123456.html  https://www.some-other-site.com/  permanent
            */
        }

        private static async Task<ConfluenceArticle> GetArticle(string id)
        {
            // Make the POST request
            HttpResponseMessage response = await _resiliencePipeline.ExecuteAsync(async token => await _client.GetAsync(Url + $"/wiki/api/v2/pages/{id}?body-format=storage"));
            string responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<ConfluenceArticle>(responseBody);
            }
            else
            {
                Console.WriteLine(responseBody);
                throw new Exception();
            }
        }

        private static async Task<ConfluenceArticle> CreateArticle(ConfluenceArticle article, string parentFolderName)
        {
            //Deduplicate titles
            if (_titles.Contains(article.Title.ToLower()))
            {
                Console.WriteLine("Title is duplicate! " + article.Title);

                string titleWithFolder = article.Title + " (" + parentFolderName + ")";
                if (string.IsNullOrEmpty(parentFolderName) || _titles.Contains(titleWithFolder.ToLower()))
                {
                    int count = 2;
                    while (_titles.Contains(article.Title + " " + count.ToString()))
                    {
                        count++;
                    }

                    article.Title += " " + count.ToString();
                }
                else
                {
                    article.Title = titleWithFolder;
                }
            }
            _titles.Add(article.Title.ToLower());

            Console.WriteLine("Creating article " + article.Title);

            // Prepare the JSON content
            var json = JsonSerializer.Serialize<ConfluenceArticle>(article);
            HttpContent content = new StringContent(json, Encoding.UTF8, "application/json");

            // Make the POST request
            HttpResponseMessage response = await _resiliencePipeline.ExecuteAsync(async token => await _client.PostAsync(Url + "/wiki/api/v2/pages", content));
            string responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<ConfluenceArticle>(responseBody);
            }
            else
            {
                Console.WriteLine(responseBody);
                throw new Exception();
            }
        }

        private static async Task<ConfluenceArticle> UpdateArticle(ConfluenceArticle article)
        {
            Console.WriteLine("Updating article " + article.Title);

            // Prepare the JSON content
            var json = JsonSerializer.Serialize<ConfluenceArticle>(article);
            HttpContent content = new StringContent(json, Encoding.UTF8, "application/json");

            // Make the POST request
            HttpResponseMessage response = await _resiliencePipeline.ExecuteAsync(async token => await _client.PutAsync(Url + $"/wiki/api/v2/pages/{article.ID}", content));
            string responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<ConfluenceArticle>(responseBody);
            }
            else
            {
                Console.WriteLine(responseBody);
                throw new Exception();
            }
        }

        private static async Task AddLabels(string id, string[] labels)
        {
            List<LabelCreate> labelsToCreate = new();
            foreach (var label in labels)
            {
                labelsToCreate.Add(new LabelCreate()
                {
                    Prefix = "global",
                    Name = label
                });
            }

            // Prepare the JSON content
            var json = JsonSerializer.Serialize<LabelCreate[]>(labelsToCreate.ToArray());
            HttpContent content = new StringContent(json, Encoding.UTF8, "application/json");

            // Make the POST request
            HttpResponseMessage response = await _resiliencePipeline.ExecuteAsync(async token => await _client.PostAsync(Url + $"/wiki/rest/api/content/{id}/label", content));
            string responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine(responseBody);
                throw new Exception();
            }
        }

        private static string CleanHtml(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            //Remove left-over crap from Intercom, ZenDesk, FreshDesk...
            RemoveAttribute(doc, "class");
            RemoveAttribute(doc, "data-pm-slice");
            RemoveAttribute(doc, "data-colwidth");
            RemoveAttribute(doc, "data-insertable");
            RemoveAttribute(doc, "data-height");
            RemoveAttribute(doc, "data-width");
            RemoveAttribute(doc, "data-id");
            RemoveAttribute(doc, "data-attachment");
            RemoveAttribute(doc, "dir");
            FixAnchors(doc);
            FixAnchorRefs(doc);
            FixCdnImageLinks(doc);

            return doc.DocumentNode.OuterHtml;
        }
        private static void RemoveAttribute(HtmlDocument doc, string attribute)
        {
            var nodes = doc.DocumentNode.SelectNodes(".//*[@" + attribute + "]");

            if (nodes != null)
            {
                foreach (HtmlNode node in nodes)
                {
                    node.Attributes[attribute].Remove();
                }
            }
        }

        private static void FixAnchors(HtmlDocument doc)
        {
            var nodes = doc.DocumentNode.SelectNodes(".//*[@id]");

            if (nodes != null)
            {
                foreach (HtmlNode node in nodes)
                {
                    string anchor = node.Attributes["id"].Value;
                    node.Attributes["id"].Remove();
                    var newNode = HtmlNode.CreateNode("<ac:structured-macro ac:name=\"anchor\" ac:schema-version=\"1\" ac:macro-id=\"b025d617-d94e-4dfb-ae09-bf3eff6e7329\"><ac:parameter ac:name=\"\">" + CleanAnchor(anchor) + "</ac:parameter></ac:structured-macro>");

                    node.ParentNode.InsertBefore(newNode, node);
                }
            }
        }

        private static void FixAnchorRefs(HtmlDocument doc)
        {
            var nodes = doc.DocumentNode.SelectNodes(".//*[@href]");

            if (nodes != null)
            {
                foreach (HtmlNode node in nodes)
                {
                    string anchor = node.Attributes["href"].Value;
                    if (anchor.StartsWith("#"))
                    {
                        node.Attributes["href"].Value = CleanAnchor(node.Attributes["href"].Value);
                    }
                }
            }
        }

        private static string CleanAnchor(string value)
        {
            return value.Replace("'", "");
        }

        private static void FixCdnImageLinks(HtmlDocument doc)
        {
            //https://s3.amazonaws.com/cdn.freshdesk.com to https://s3.ca-central-1.amazonaws.com/cdn.velixo.com/helpdesk/cQmEmpx6Oz5tm5C2gDHJtcKpmjvysaXHkA.png
            var nodes = doc.DocumentNode.SelectNodes(".//*[@src]");
            if (nodes != null)
            {
                foreach (HtmlNode node in nodes)
                {
                    string url = node.Attributes["src"].Value;

                    if (url.Contains("https://s3.amazonaws.com/cdn.freshdesk.com"))
                    {
                        var uri = new Uri(url);
                        node.Attributes["src"].Value = "https://s3.ca-central-1.amazonaws.com/cdn.velixo.com/helpdesk/" + System.IO.Path.GetFileName(uri.AbsolutePath);
                    }
                }
            }
        }

        private static string FixHelpDeskLinksAndDoFinalCleanup(string content)
        {
            string cleaned = content.Replace("&amp;nbsp;", " ").Replace("&nbsp;", " ");

            var doc = new HtmlDocument();
            doc.LoadHtml(cleaned);

            Regex numberRegEx = new Regex(@"[\d]+");

            var nodes = doc.DocumentNode.SelectNodes(".//*[@href]");
            if (nodes != null)
            {
                foreach (HtmlNode node in nodes)
                {
                    string url = node.Attributes["href"].Value;

                    if (url.StartsWith("#") || url.StartsWith("https://velixo.sharepoint.com"))
                    {
                        //Ignore those links
                    }
                    else if (url.Contains("/solutions/articles"))
                    {
                        Console.WriteLine("Article: " + url);
                        var matchCollection = numberRegEx.Matches(url);
                        if (_articlesMap.TryGetValue(long.Parse(matchCollection[0].Value), out var confluenceMap))
                        {
                            ReplaceAhrefWithConfluenceLink(node, confluenceMap.ConfluenceArticle.Title);
                        }
                        else
                        {
                            System.Diagnostics.Debug.Assert(false, "Article not found in map");
                        }
                    }
                    else if (url.Contains("/support/solutions/folders"))
                    {
                        Console.WriteLine("Folder: " + url);
                        var matchCollection = numberRegEx.Matches(url);
                        if (_foldersMap.TryGetValue(long.Parse(matchCollection[0].Value), out var confluenceMap))
                        {
                            ReplaceAhrefWithConfluenceLink(node, confluenceMap.ConfluenceArticle.Title);
                        }
                        else
                        {
                            System.Diagnostics.Debug.Assert(false, "Folder not found in map");
                        }
                    }
                    else if (url.Contains("/support/solutions"))
                    {
                        var matchCollection = numberRegEx.Matches(url);
                        if (_categoriesMap.TryGetValue(long.Parse(matchCollection[0].Value), out var confluenceMap))
                        {
                            ReplaceAhrefWithConfluenceLink(node, confluenceMap.ConfluenceArticle.Title);
                        }
                        else
                        {
                            System.Diagnostics.Debug.Assert(false, "Category not found in map");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"UNKNOWN LINK: {url}");
                    }
                }
            }

            string output = doc.DocumentNode.OuterHtml;
            return output;
        }

        private static void ReplaceAhrefWithConfluenceLink(HtmlNode node, string pageTitle)
        {
            var newNode = HtmlNode.CreateNode($"<ac:link><ri:page ri:content-title=\"{System.Net.WebUtility.HtmlEncode(pageTitle)}\"/><ac:link-body>{node.InnerText}</ac:link-body></ac:link>");
            node.ParentNode.ReplaceChild(newNode, node);
        }
    }
}
