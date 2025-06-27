Usage

create payload.json with the following content:
```json
{
    "messages": [
        {
            "role": "system",
            "content": "As a software engineer migrating GRXML grammars to Microsoft Copilot Studio, analyze GRXML files and classify them as:\n\n1) Prebuilt entity (from: Age, Boolean, City, Color, Continent, Country, Date, Date and time, Date and time without timezone, Duration, Email, Event, File, Language, Money, Number, Ordinal, Organization, Percentage, Person name, Phone number, Point of interest, Speed, State, Street address, Temperature, URL, Weight, Zip code)\n\n2) Custom entity (ClosedListEntity: list of interchangeable synonyms; RegexEntity: regular expressions)\n\nYou will receive GRXML content along with a file name. Analyze the content and provide your response. \n\n For 'ClosedListEntity', ensure no words repeat between synonyms and make sure the synonyms do not repeat the IDs."
        },
        {
            "role": "system",
            "content": "Example 1:\nFile name: auth7561_IDVConfPhNum_QA.grxml\nGrxml input: \n    <?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<grammar version=\"1.0\" xmlns=\"http://www.w3.org/2001/06/grammar\" xml:lang=\"en-US\" root=\"eIDVConfPhNumYN\" mode=\"voice\">\n    <rule id=\"eIDVConfPhNumYN\" scope=\"public\">\n        <one-of>\n            <item>\n                <ruleref uri=\"#YES\"/>\n                <tag>eIDVConfPhNumYN=\"yes\";</tag>\n            </item>\n            <item>\n                <ruleref uri=\"#NO\"/>\n                <tag>eIDVConfPhNumYN=\"no\";</tag>\n            </item>\n        </one-of>\n    </rule>\n    <rule id=\"YES\" scope=\"private\">\n        <one-of>\n            <item>yes</item>\n            <item>yeah</item>\n            <item>yep</item>\n            <item>yup</item>\n            <item>sure</item>\n            <item>right</item>\n            <item>correct</item>\n            <item>okay</item>\n            <item>affirmative</item>\n        </one-of>\n    </rule>\n    <rule id=\"NO\" scope=\"private\">\n        <one-of>\n            <item>no</item>\n            <item>nope</item>\n            <item>absolutely not</item>\n            <item>wrong</item>\n            <item>negative</item>\n            <item>that's not correct</item>\n            <item>that is not right</item>\n        </one-of>\n    </rule>\n</grammar>\n\n\nOutput in pure YAML as follows:\nentity_type: CustomListEntity\nreason: although the GRXML input represents a Boolean, you should make it a CustomListEntity to enable multiple synonyms for the answers 'yes' and 'no'.\ncopilot_studio_code: |\n    - kind: CustomEntityComponent\n        displayName: auth7561_IDVConfPhNum_QA\n        state: Active\n        status: Active\n        schemaName: auth7561_IDVConfPhNum_QA\n        entity:\n        kind: ClosedListEntity\n        items:\n            - id: YES\n            displayName: YES\n            synonyms:\n                - yeah\n                - yep\n                - yup\n                - sure\n                - right\n                - correct\n                - okay\n                - affirmative\n            - id: NO\n            displayName: NO\n            synonyms:\n                - nope\n                - absolutely\n                - wrong\n                - negative\n                - that's not correct\n                - that is not right\n\n\nNote that the words 'yes' and 'no' must not be repeated in the synonyms. From now on, you will receive GRXML content and you will have to give me the expected output in YAML format."
        },
        {
            "role": "user",
            "content": "Convert the file anyprompt.grxml to Microsoft Copilot Studio Yaml: <grammar version=\"1.0\" xml:lang=\"en-US\" mode=\"voice\" xmlns=\"http://www.w3.org/2001/06/grammar\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:schemaLocation=\"http://www.w3.org/2001/06/grammar http://www.w3.org/TR/speech-grammar/grammar.xsd\" root=\"MAIN\" tag-format=\"semantics/1.0\">\r\n  <meta name=\"swirec_simple_result_key\" content=\"SWI_literal\" />\r\n  <rule id=\"MAIN\" scope=\"public\">\r\n    <ruleref special=\"GARBAGE\" />\r\n  </rule>\r\n</grammar>"
        }
    ],
    "model": "test",
    "stream": true,
    "stream_options": {
        "include_usage": true
    }
}
```
Then run the following command to send a POST request to the API:
```bash

curl -v -N -X POST http://localhost:5001/openai/deployments/test/chat/completions   -H "Content-Type: application/json"   -d @payload.json
