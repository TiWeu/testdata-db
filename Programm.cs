using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Azure.AI.OpenAI;

class Program
{
    // Konfiguration
    static readonly string searchEndpoint = Environment.GetEnvironmentVariable("AZURE_SEARCH_ENDPOINT");
    static readonly string searchApiKey = Environment.GetEnvironmentVariable("AZURE_SEARCH_API_KEY");
    static readonly string searchIndexName = Environment.GetEnvironmentVariable("AZURE_SEARCH_INDEX");

    static readonly string openAiEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
    static readonly string openAiApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
    static readonly string openAiDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT");

    static async Task Main(string[] args)
    {
        // 1. Kontext aus Azure AI Search holen
        string tableQuery = "employees jobs"; // oder dynamisch aus User Prompt ableiten
        string context = await GetContextFromSearchAsync(tableQuery);

        // 2. System Prompt & User Prompt
        string systemPrompt = @"You are a database assistant.
Generate realistic test data as valid JSON arrays for all tables provided in the context, including any referenced tables required for foreign key constraints.
Output only the JSON arrays for each table, without any explanations, comments, or extra text.
If multiple tables are present, output a JSON object where each key is the table name and the value is the array of records for that table.
If the context is missing or incomplete, respond only with: {}";

        string userPrompt = "employees"; // oder beliebiger Tabellenname

        // 3. LLM-Aufruf
        string result = await GetOpenAICompletionAsync(systemPrompt, context, userPrompt);

        Console.WriteLine(result);
    }

    static async Task<string> GetContextFromSearchAsync(string query)
    {
        var client = new SearchClient(new Uri(searchEndpoint), searchIndexName, new AzureKeyCredential(searchApiKey));
        var options = new SearchOptions
        {
            Size = 5,
            IncludeTotalCount = true
        };
        options.Select.Add("content"); // oder alle Felder, die du brauchst
        options.Select.Add("columns");
        options.Select.Add("relations");
        options.Select.Add("constraints");
        options.Select.Add("table_name");
        options.Select.Add("title");
        options.Select.Add("id");
        options.Select.Add("description");

        var response = await client.SearchAsync<SearchDocument>(query, options);
        var contextList = new List<string>();
        await foreach (var result in response.Value.GetResultsAsync())
        {
            // Baue ein vollständiges JSON-Objekt für jede Tabelle
            var doc = result.Document;
            var tableJson = new
            {
                id = doc["id"],
                title = doc["title"],
                table_name = doc["table_name"],
                content = doc["content"],
                columns = doc["columns"],
                relations = doc["relations"],
                constraints = doc["constraints"],
                description = doc["description"]
            };
            contextList.Add(System.Text.Json.JsonSerializer.Serialize(tableJson));
        }
        return string.Join("\n", contextList);
    }

    static async Task<string> GetOpenAICompletionAsync(string systemPrompt, string context, string userPrompt)
    {
        var client = new OpenAIClient(new Uri(openAiEndpoint), new AzureKeyCredential(openAiApiKey));
        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, systemPrompt),
            new ChatMessage(ChatRole.User, context),
            new ChatMessage(ChatRole.User, userPrompt)
        };

        var chatCompletionsOptions = new ChatCompletionsOptions
        {
            Temperature = 0.2f,
            MaxTokens = 1024
        };
        foreach (var msg in messages)
            chatCompletionsOptions.Messages.Add(msg);

        var response = await client.GetChatCompletionsAsync(openAiDeployment, chatCompletionsOptions);
        return response.Value.Choices[0].Message.Content;
    }
}