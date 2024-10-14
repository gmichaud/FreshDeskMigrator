// See https://aka.ms/new-console-template for more information
using Microsoft.Extensions.Configuration;
using RestSharp;
using RestSharp.Authenticators;
using System;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

class Program
{
    static Dictionary<string, string> categoriesHelpCenters = new()
    {
        { "153000023647", "help" },
        { "153000011067", "help" },
        { "153000013633", "helpintacct" },
        { "153000013634", "helpacumatica" },
        { "153000019148", "helpacumatica" },
    };

    static Dictionary<string, Dictionary<string, JsonElement>> categoriesArticles = new()
    {
        { "153000023647", new Dictionary<string, JsonElement>() },
        { "153000011067", new Dictionary<string, JsonElement>() },
        { "153000013633", new Dictionary<string, JsonElement>() },
        { "153000013634", new Dictionary<string, JsonElement>() },
        { "153000019148", new Dictionary<string, JsonElement>() },
    };


    static Dictionary<string, Dictionary<string, string>> categoriesFolders = new()
    {
        { "153000023647", new Dictionary<string, string>() },
        { "153000011067", new Dictionary<string, string>() },
        { "153000013633", new Dictionary<string, string>() },
        { "153000013634", new Dictionary<string, string>() },
        { "153000019148", new Dictionary<string, string>() },
    };

    static HashSet<string> processedFolders = new HashSet<string>();

    static RestClient client;

    static async Task Main(string[] args)
    {
        ConfigurationBuilder configurationBuilder = new ConfigurationBuilder();
        IConfiguration configuration = configurationBuilder.AddUserSecrets<Program>().Build();
        var secrets = configuration.GetSection("FreshDesk");

        client = new RestClient(new RestClientOptions("https://velixo.freshdesk.com")
        {
            Authenticator = new HttpBasicAuthenticator(secrets["Token"], "X")
        });

        // Load articles
        foreach (var category in categoriesArticles.Keys)
        {
            //Get all folders in this category
            var foldersRequest = new RestRequest($"/api/v2/solutions/categories/{category}/folders", Method.Get);
            RestResponse foldersResponse = await client.ExecuteAsync(foldersRequest);

            // Process each folder
            foreach (var folderID in JsonDocument.Parse(foldersResponse.Content).RootElement.DescendantPropertyValues("id"))
            {
                await ProcessFolder(category, folderID.ToString());
            }
        }

        var imgRegExp = new System.Text.RegularExpressions.Regex(@"src\s*=\s*""(.+?)""");
        var wc = new WebClient();

        //Process each articles
        HashSet<string> processedArticles = new HashSet<string>();
        foreach (var category in categoriesArticles)
        {
            foreach (var article in category.Value)
            {
                if (processedArticles.Contains(article.Key)) continue;

                Console.WriteLine($"Processing article https://help.velixo.com/support/solutions/articles/{article.Key}");
                System.IO.File.WriteAllText(article.Key + ".json", article.Value.ToString());

                //if(article.Value.TryGetProperty("description", out var articleDescription))
                //{
                //    bool foundOne = false;
                //    MatchCollection matches = imgRegExp.Matches(articleDescription.ToString());
                //    foreach(Match match in matches)
                //    {
                //        try
                //        {
                //            var fullPath = match.Value.Substring(5, match.Value.Length - 6);
                //            var uri = new System.Uri(fullPath);
                //            wc.DownloadFile(fullPath, System.IO.Path.GetFileName(uri.LocalPath));
                //        }
                //        catch(Exception ex)
                //        {
                //            Console.WriteLine(ex.ToString());
                //        }

                //        foundOne = true;
                //    }

                //    if(articleDescription.ToString().Contains("img src") && !foundOne)
                //    {
                //        System.Diagnostics.Debug.Assert(false);
                //    }
                //}

                //string originalDescription = article.Value.GetProperty("description").ToString();
                //var updatedDescription = originalDescription.Replace("MYOB Advanced", "MYOB Acumatica");
                //updatedDescription = updatedDescription.Replace("formerly MYOB Acumatica", "formerly MYOB Advanced");

                //if (originalDescription != updatedDescription)
                //{
                //    var updateArticleDescriptionRequest = new RestRequest($"/api/v2/solutions/articles/{article.Key}", Method.Put);
                //    updateArticleDescriptionRequest.RequestFormat = DataFormat.Json;
                //    updateArticleDescriptionRequest.AddJsonBody(new { description = updatedDescription, status = 2 });
                //    RestResponse updateArticleDescriptionResponse = await client.ExecuteAsync(updateArticleDescriptionRequest);
                //    if (updateArticleDescriptionResponse.IsSuccessful)
                //    {
                //        Console.WriteLine("SUCCESS! Updated article body " + article.Key);
                //    }
                //    else
                //    {
                //        Debug.Assert(false);
                //        Console.WriteLine("ERROR! Failed to update article body " + article.Key);
                //    }
                //}

                //string originalTitle = article.Value.GetProperty("title").ToString();
                //var updatedTitle = originalTitle.Replace("MYOB Advanced", "MYOB Acumatica");
                //updatedTitle = updatedTitle.Replace("formerly MYOB Acumatica", "formerly MYOB Advanced");

                //if (originalTitle != updatedTitle)
                //{
                //    var updateArticleTitleRequest = new RestRequest($"/api/v2/solutions/articles/{article.Key}", Method.Put);
                //    updateArticleTitleRequest.RequestFormat = DataFormat.Json;
                //    updateArticleTitleRequest.AddJsonBody(new { title = updatedTitle, status = 2 });
                //    RestResponse updateArticleTitleResponse = await client.ExecuteAsync(updateArticleTitleRequest);
                //    if (updateArticleTitleResponse.IsSuccessful)
                //    {
                //        Console.WriteLine("SUCCESS! Updated article title " + article.Key);
                //    }
                //    else
                //    {
                //        Debug.Assert(false);
                //        Console.WriteLine("ERROR! Failed to update article title " + article.Key);
                //    }
                //}

                processedArticles.Add(article.Key);
            }
        }

        Console.ReadKey();
    }

    private async static Task ProcessFolder(string category, string folderID)
    {
        categoriesFolders[category][folderID] = folderID;
        if (processedFolders.Contains(folderID.ToString())) return;

        //Get all articles in this folder
        var articlesRequest = new RestRequest($"/api/v2/solutions/folders/{folderID}/articles?per_page=100", Method.Get);
        RestResponse articlesResponse = await client.ExecuteAsync(articlesRequest);

        if (articlesResponse.IsSuccessStatusCode)
        {
            foreach (var article in JsonDocument.Parse(articlesResponse.Content).RootElement.EnumerateArray())
            {
                categoriesArticles[category][article.GetProperty("id").ToString()] = article;
            }
        }

        processedFolders.Add(folderID.ToString());

        //Look for subfolders
        var subfoldersRequest = new RestRequest($"/api/v2/solutions/folders/{folderID}/subfolders?per_page=100", Method.Get);
        RestResponse subfoldersResponse = await client.ExecuteAsync(subfoldersRequest);

        if (subfoldersResponse.IsSuccessStatusCode)
        {
            foreach (var subfolderID in JsonDocument.Parse(subfoldersResponse.Content).RootElement.DescendantPropertyValues("id"))
            {
                await ProcessFolder(category, subfolderID.ToString());
            }
        }
    }

    private static string FixLinks(string input)
    {
        var hrefRegexp = new System.Text.RegularExpressions.Regex("<a\\s+(?:[^>]*?\\s+)?href=([\"'])(.*?)\\1");
        var idRegexp = new System.Text.RegularExpressions.Regex("\\d+");

        var matches = hrefRegexp.Matches(input);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var href = match.Groups[2].Value;

            if (href.Contains("/support/solutions/"))
            {
                string articleOrFolderID = idRegexp.Match(href).Value;

                if (String.IsNullOrEmpty(articleOrFolderID))
                {
                    Console.WriteLine("NO ARTICLE ID FOUND: " + href);
                }
                else
                {
                    string newHref;
                    if (href.Contains("/folders"))
                    {
                        newHref = GetUrlForFreshDeskFolder(articleOrFolderID);
                    }
                    else
                    {
                        newHref = GetUrlForFreshDeskArticle(articleOrFolderID);
                    }

                    if (href != newHref)
                    {
                        Console.WriteLine("UPDATE: " + href + " to: " + newHref);
                        input = input.Replace(href, newHref);
                    }
                }
            }
            else if (href.StartsWith("https://help.velixo.com/articles") || href.StartsWith("https://help.velixo.com/en/articles"))
            {
                string oldArticleOrFolderID = idRegexp.Match(href).Value;
                if (String.IsNullOrEmpty(oldArticleOrFolderID))
                {
                    Console.WriteLine("NO ARTICLE ID FOUND: " + href);
                }
                else
                {
                    //Get redirection URl
                    HttpWebRequest? request = WebRequest.Create($"https://intercom.velixo.com/en/articles/{oldArticleOrFolderID}") as HttpWebRequest;
                    request.AllowAutoRedirect = false;
                    try
                    {
                        HttpWebResponse? response = request.GetResponse() as HttpWebResponse;
                        if (response.StatusCode == HttpStatusCode.Redirect ||
                            response.StatusCode == HttpStatusCode.MovedPermanently)
                        {
                            // Do something here...
                            string newUrl = response.Headers["Location"];
                            string newArticleOrFolderID = idRegexp.Match(newUrl).Value;

                            string newHref;
                            if (href.Contains("/folders"))
                            {
                                newHref = GetUrlForFreshDeskFolder(newArticleOrFolderID);
                            }
                            else
                            {
                                newHref = GetUrlForFreshDeskArticle(newArticleOrFolderID);
                            }

                            if (href != newHref)
                            {
                                Console.WriteLine("UPDATE: " + href + " to: " + newHref);
                                input = input.Replace(href, newHref);
                            }
                        }
                    }
                    catch
                    {
                        Console.WriteLine($"ERROR! Unable to find redirect for Intercom article {oldArticleOrFolderID}");
                    }
                }
            }
            else if (href.StartsWith("https://velixo.intercom-attachments") || href.StartsWith("https://downloads.intercomcdn.com"))
            {
                Console.WriteLine("WARNING! Image has hyperlink to Intercom: " + href);
            }
            else if (href.StartsWith("https://www.youtube.com/") || href.StartsWith("https://velixo.sharepoint.com")
                || href.StartsWith("https://secure.velixo.com")
                || href.Contains("acumatica.com/")
                || href.StartsWith("https://www.velixo.com"))
            {
                //Ignoring this
            }
            else if (href.StartsWith("http"))
            {
                Console.WriteLine("VERIFY: " + href);
            }
        }

        return input;
    }

    private static string GetUrlForFreshDeskArticle(string articleID)
    {
        List<string> matchingCategories = new List<string>();

        foreach(string category in categoriesArticles.Keys)
        {
            if (categoriesArticles[category].ContainsKey(articleID))
            {
                matchingCategories.Add(category);
            }
        }

        string helpcenter = ""; 
        if (matchingCategories.Count == 1)
        {
            helpcenter = categoriesHelpCenters[matchingCategories[0]];
        }
        else
        {
            if (matchingCategories.Count == 0)
            {
                //Shouldn't happen!
                Console.WriteLine($"ERROR! Freshdesk article {articleID} appears in NO CATEGORY -- possibly invalid redirect!");
            }

            helpcenter = "help";
        }

        return $"https://{helpcenter}.velixo.com/support/solutions/articles/" + articleID;
    }

    private static string GetUrlForFreshDeskFolder(string folderID)
    {
        List<string> matchingCategories = new List<string>();

        foreach (string category in categoriesFolders.Keys)
        {
            if (categoriesFolders[category].ContainsKey(folderID))
            {
                matchingCategories.Add(category);
            }
        }

        string helpcenter = "";
        if (matchingCategories.Count == 1)
        {
            helpcenter = categoriesHelpCenters[matchingCategories[0]];
        }
        else
        {
            if (matchingCategories.Count == 0)
            {
                //Shouldn't happen!
                Console.WriteLine($"ERROR! Freshdesk folder {folderID} appears in NO CATEGORY -- possibly invalid redirect!");
            }

            helpcenter = "help";
        }

        return $"https://{helpcenter}.velixo.com/support/solutions/folders/" + folderID;
    }
}
