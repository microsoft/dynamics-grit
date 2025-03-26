using Microsoft.Extensions.AI;
using Azure.AI.OpenAI;
using Microsoft.SemanticKernel;
using Azure;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Utilities
{
    public class GPTPrompter
    {
        private readonly IChatClient _chatClient;
        private readonly List<ChatMessage> _initialChatHistory;
        private List<ChatMessage> _chatHistory;

        public GPTPrompter(IConfiguration configuration)
        {
            var config = configuration;
            string? endpoint = config["AZURE_OPENAI_ENDPOINT"];
            string? deployment = config["AZURE_OPENAI_GPT_NAME"];
            string? key = config["AZURE_OPENAI_GPT_KEY"];

            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(deployment) || string.IsNullOrEmpty(key))
            {
                throw new InvalidOperationException("Azure OpenAI configuration is missing.");
            }

            IKernelBuilder builder = Kernel.CreateBuilder();
            builder.Services.AddAzureOpenAIChatCompletion(
                deployment,
                endpoint,
                "service-key"); // Secret key
            var kernel = builder.Build();

            _chatClient =
                new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key))
                    .AsChatClient(deployment);

            _initialChatHistory = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, """
                    You are a software engineer who is migrating an old IVR application which uses GRXML grammars into a more up to date Microsoft Copilot Studio bot.
                    You will receive a GRXML file from the user and you must classify it as a Microsoft Copilot Studio prebuilt entity.
                    The prebuilt entity types are: Age, Boolean, City, Color, Continent, Country (or region), Date, Date and time, Date and time without timezone, Duration, Email, Event, File, Language, Money, Number, Ordinal, Organization, Percentage, Person name, Phone number, Point of interest, Speed, State, Street address, Temperature, URL, Weight, Zip code.
                    For each GRXML file, analyze it and tell me if that would semantically fit a prebuilt entity. 
                    Name the prebuilt entity type on a column named 'Entity Type'.
                    If you can't fit the GRXML file into a prebuilt entity type, then that is a 'custom entity'.
                    Custom entities can be of types ClosedListEntity or RegexEntity. 
                    So, use the 'Entity Type' column to tell me if it is a ClosedListEntity or a RegexEntity.
                    A ClosedListEntity is a list of synonyms that are interchangeable. A RegexEntity is a list of regular expressions that match the entity.
                    Output the answer in a JSON format having three fields: ‘Entity Type’, having the entity type that you found; ‘Reason’, explaining why you chose that entity type; ‘YAML’, the YAML code representing the new entity.
                    The YAML code is only needed for custom entities.
                    I will provide some examples of GRXML conversions for you to train yourself.
                """),
                new ChatMessage(ChatRole.System, """
                    Example 1:
                    File name: auth7561_IDVConfPhNum_QA.grxml
                    Grxml input: 
                        <?xml version="1.0" encoding="UTF-8"?>

                            <!--

                            ;   File: yesno.grxml
                            ;
                            ;   Description:
                            ;       Grammar rules for Yes/No recognition states.
                            ;       Includes words for confirmation dialogs.
                            ;
                            ;   Author: Pranav Chadha
                            ;
                            ;   Last Edit Date: 02/28/2017
                            ;
                            ;   Version History:
                            ;       02/28/2017: Initial Version
                            ;
                            -->

                            <grammar version="1.0" xmlns="http://www.w3.org/2001/06/grammar"
                            xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:schemaLocation="http://www.w3.org/2001/06/grammar" 
                            tag-format="swi-semantics/1.0" xml:lang="en-US" root="eIDVConfPhNumYN" mode="voice">

                            <meta name="swirec_application_name" content="EComm" /> 
                            <meta name="swirec_company_name" content="Walgreen" />


                            <rule id="eIDVConfPhNumYN" scope="public">
                                <item repeat="0-1" repeat-prob="0.01">
                                    <ruleref uri="#PREFILLER"/>
                                </item>
                                <one-of>
                                    <item>
                                        <ruleref uri="#YES"/>
                                        <tag> eIDVConfPhNumYN="yes"; </tag>
                                    </item>
                                    <item>
                                        <ruleref uri="#NO"/>
                                        <tag> eIDVConfPhNumYN="no"; </tag>
                                    </item>
                                </one-of>
                                <item repeat="0-1" repeat-prob="0.01">
                                    <ruleref uri="#POSTFILLER"/>
                                </item>
                            </rule>

                            <!-- filler -->

                            <rule id="PREFILLER" scope="private">
                                <one-of>
                                    <item> @hes@ </item>
                                </one-of>
                            </rule>

                            <!--
                            ;;
                            ;; Rule for Slot Value: YES
                            ;;
                            -->

                            <rule id="YES" scope="private">
                                <one-of>
                                    <item>
                                        <one-of>
                                            <item repeat="1-"> yes </item>
                                            <item repeat="1-"> yeah </item>
                                            <item> yep </item>
                                            <item> yup </item>
                                        </one-of>
                                        <item repeat="0-1">
                                            <one-of>
                                                <item> it is </item>
                                                <item> i do </item>
                                                <item> i am </item>
                                                <item> i would </item>
                                                <item> you should </item>
                                            </one-of>
                                        </item>
                                    </item>
                                    <item>
                                        <item repeat="0-1"> it </item>
                                        sure
                                        <item repeat="0-1"> is </item>
                                    </item>
                                    <item> yeah yes </item>
                                    <item> 
                                        <one-of>
                                            <item> right </item>
                                            <item> correct </item>
                                            <item> sure </item>
                                        </one-of>
                                        <item repeat="0-1">
                                            <one-of>
                                                <item> yes </item>
                                                <item> yeah </item>
                                            </one-of>
                                        </item>
                                    </item>
                                    <item> okay </item>
                                    <item> affirmative </item>
                                    <item> exactly </item>
                                    <item> good </item>
                                    <item> cool </item>
                                    <item> alright </item>
                                    <item> you got it </item>
                                    <item>
                                        <item repeat="0-1">
                                            <one-of>
                                                <item> yes </item>
                                                <item> yep </item>
                                                <item> yeah </item>
                                            </one-of>
                                        </item>
                                        that&apos;s
                                        <one-of>
                                            <item> it </item>
                                            <item> right </item>
                                            <item> correct </item>
                                            <item> what i want </item>
                                        </one-of>
                                    </item>
                                    <item> 
                                        that is correct
                                        <item repeat="0-1"> yes </item>
                                    </item>
                                    <item> that would be fine </item>
                                    <item>
                                        <item repeat="0-1"> i </item>
                                        <one-of>
                                            <item> guess </item>
                                            <item> think </item>
                                        </one-of>
                                        so
                                    </item>
                                    <item>
                                        you
                                        <one-of>
                                            <item> bet </item>
                                            <item> betcha </item>
                                        </one-of>
                                    </item>
                                    <item> thanks </item>
                                </one-of>
                            </rule>

                            <!--
                            ;;
                            ;; Rule for Slot Value: NO
                            ;;
                            -->

                            <rule id="NO" scope="private">
                                <one-of>
                                    <item repeat="1-"> no </item>
                                    <item> nope </item>
                                    <item> absolutely not </item>
                                    <item> wrong </item>
                                    <item> negative </item>
                                    <item>
                                        <item repeat="0-1"> no </item>
                                        <one-of>
                                            <item> that&apos;s not </item>
                                            <item> that is not </item>
                                        </one-of>
                                        <item repeat="0-1">
                                            <one-of>
                                                <item> correct </item>
                                                <item> right </item>
                                            </one-of>
                                        </item>
                                    </item>
                                    <item>
                                        <item repeat="0-1"> no </item>
                                        <one-of>
                                            <item> that&apos;s </item>
                                            <item> that is</item>
                                        </one-of>
                                        incorrect
                                    </item>
                                    <item> 
                                        no i 
                                        <one-of>
                                            <item> do not </item>
                                            <item> don&apos;t </item>
                                            <item> would not </item>
                                            <item> wouldn&apos;t </item>
                                        </one-of>
                                    </item>
                                    <item> not at this time </item>
                                    <item> no way </item>
                                    <item> not really </item>
                                    <item>
                                        not
                                        <item repeat="0-1"> right </item>
                                        now
                                    </item>
                                    <item> no it isn&apos;t </item>
                                    <item> no it&apos;s not </item>
                                    <item>
                                        no you
                                        <one-of>
                                            <item> should not </item>
                                            <item> shouldn&apos;t </item>
                                        </one-of>
                                    </item>
                                </one-of>
                            </rule>

                            <!-- filler -->
                            <rule id="POSTFILLER" scope="private">
                                <one-of>
                                    <item> maam </item>
                                    <item> sir </item>
                                    <item> please </item>
                                    <item> thanks </item>
                                    <item> thank you </item>
                                </one-of>
                            </rule>

                            </grammar>


                            Output:
                                            Entity type: CustomListEntity
                                            Reason: although the GRXML input represents a Boolean, you should make it a CustomListEntity to enable multiple synonyms for the answers ‘yes’ and ‘no’.
                                            YAML:
                                            {
                                                "kind": "CustomEntityComponent",
                                                "displayName": "auth7561_IDVConfPhNum_QA",
                                                "state": "Active",
                                                "status": "Active",
                                                "schemaName": "auth7561_IDVConfPhNum_QA",
                                                "entity": {
                                                    "kind": "ClosedListEntity",
                                                    "items": [
                                                        {
                                                            "id": "YES",
                                                            "displayName": "YES",
                                                            "synonyms": [
                                                                "yep",
                                                                "yup",
                                                                "it is",
                                                                "i do",
                                                                "i would",
                                                                "you should",
                                                                "it",
                                                                "is",
                                                                "yeah yes",
                                                                "right",
                                                                "correct",
                                                                "sure",
                                                                "yes",
                                                                "yeah",
                                                                "okay",
                                                                "affirmative",
                                                                "exactly",
                                                                "good",
                                                                "cool",
                                                                "alright",
                                                                "you got it",
                                                                "what i want",
                                                                "that is correct",
                                                                "that would be fine",
                                                                "i guess so",
                                                                "i think so",
                                                                "you bet",
                                                                "betcha",
                                                                "thanks"
                                                            ]

                                                        },
                                                        {
                                                            "id": "NO",
                                                            "displayName": "NO",
                                                            "synonyms": [
                                                                "no",
                                                                "nope",
                                                                "absolutely not",
                                                                "wrong",
                                                                "negative",
                                                                "that's not",
                                                                "that is not",
                                                                "no that's not",
                                                                "no that is not",
                                                                "correct",
                                                                "right",
                                                                "no that's incorrect",
                                                                "no that is incorrect",
                                                                "no i do not",
                                                                "no i don't",
                                                                "no i would not",
                                                                "no i wouldn't",
                                                                "not at this time",
                                                                "no way",
                                                                "not really",
                                                                "not right now",
                                                                "no it isn't",
                                                                "no it's not",
                                                                "no you should not",
                                                                "no you shouldn't"
                                                            ]
                                                        }
                                                    ]
                                                }
                                            }
                """),
                new ChatMessage(ChatRole.System, """
                    From now on, you will receive GRXML content and you will have to give me the expected output in JSON format.
                """),
            };

            _chatHistory = new List<ChatMessage>(_initialChatHistory);
        }

        public async Task<string> GetFileEntityTypeAsync(string fileContent)
        {
            _chatHistory.Add(new ChatMessage(ChatRole.User, fileContent));

            // Stream the AI response and add to chat history
            var response = "";
            await foreach (var item in _chatClient.CompleteStreamingAsync(_chatHistory))
            {
                Console.Write(item.Text);
                response += item.Text;
            }
            Console.WriteLine();


            // Reset chat history to initial state
            _chatHistory = new List<ChatMessage>(_initialChatHistory);

            return response;
        }
    }
}
