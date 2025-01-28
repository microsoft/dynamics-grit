using Microsoft.Extensions.AI;
using Azure.AI.OpenAI;
using Microsoft.SemanticKernel;
using Azure;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Utilities
{
    public class ChatGPTPrompter
    {
        public static async Task<string> GetFileEntityTypeAsync(string fileContent)
        {
            var config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();
            string endpoint = config["AZURE_OPENAI_ENDPOINT"];
            string deployment = config["AZURE_OPENAI_GPT_NAME"];
            string key = config["AZURE_OPENAI_GPT_KEY"];

            IKernelBuilder builder = Kernel.CreateBuilder();
            builder.Services.AddAzureOpenAIChatCompletion(
                deployment,
                endpoint,
                "service-key"); // Secret key
            var kernel = builder.Build();

            IChatClient chatClient =
                new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key))
                    .AsChatClient(deployment);


            // Start the conversation with context for the AI model
            List<ChatMessage> chatHistory = new()
            {
                new ChatMessage(ChatRole.System, """
                    You are a software engineer who is migrating an old IVR application which uses GRXML grammars into a more up to date Microsoft Copilot Studio bot.
                    You will receive a GRXML file from the user and you must classify it as a Microsoft Copilot Studio prebuilt entity.
                    The prebuilt entity types are: Age, Boolean, City, Color, Continent, Country (or region), Date, Date and time, Date and time without timezone, Duration, Email, Event, File, Language, Money, Number, Ordinal, Organization, Percentage, Person name, Phone number, Point of interest, Speed, State, Street address, Temperature, URL, Weight, Zip code.
                    For each GRXML file, analyze it and tell me if that would semantically fit a prebuilt entity. 
                    Name the prebuilt entity type on a column named 'Entity Type'.
                    If you can't fit the GRXML file into a prebuilt entity type, then that is a 'custom entity'.
                    Custom entities can be of types ClosedListEntity or RegexEntity. 
                    So use the 'Entity Type' column to tell me if it is a ClosedListEntity or a RegexEntity.
                    A ClosedListEntity is a list of synonyms that are interchangeable. A RegexEntity is a list of regular expressions that match the entity.
                    Output the answer in three lines: one for the entity type, another for the reason for choosing that type and another for a YAML code representing the new entity.
                    The YAML code is only needed for custom entities.
                """)
            };

            //string directoryPath = @"C:\dotnet\grammars\gpt-convert";
            //string[] filePaths = Directory.GetFiles(directoryPath, "*.grxml");

            //foreach (string filePath in filePaths)
            //{
                // Get user prompt and add to chat history
                // Console.WriteLine("Answer for file: " + filePath);
                //Console.Write(filePath + ";");
                // var userPrompt = Console.ReadLine();
                //string fileContents = File.ReadAllText(filePath);
                chatHistory.Add(new ChatMessage(ChatRole.User, fileContent));

                // Stream the AI response and add to chat history
                // Console.WriteLine("AI Response:");
                var response = "";
                await foreach (var item in
                    chatClient.CompleteStreamingAsync(chatHistory))
                {
                    Console.Write(item.Text);
                    response += item.Text;
                }
                chatHistory.Add(new ChatMessage(ChatRole.Assistant, response));
                Console.WriteLine();
            //}

            return response;
        }
    }
}
