namespace CRM.CCaaS.IVR.GRammarImportTool.Stubs.Models.Chat;

public static class ChatData
{
    public static string[] GRXML_PROMPTS =
    [
        @"Convert the file .*?\.grxml to Microsoft Copilot Studio Yaml.\s*?",
    ];

    public static readonly string YAML_ERROR_DATA =
        @"entity_type: ErrorEntity
        message: Conversion failed. The request contains invalid data.
        details: Ensure that the XML format is correct and adheres to the expected schema.";

    public static readonly string[] YAML_REPLY_DATA =
    [
@"entity_type: CustomListEntity
reason: although the GRXML input represents a Boolean, you should make it a CustomListEntity to enable multiple synonyms for the answers 'yes' and 'no'.
copilot_studio_code: |
  - kind: CustomEntityComponent
      displayName: auth7561_STUB_QA
      state: Active
      status: Active
      schemaName: auth7561_STUB_QA
      entity:
          kind: ClosedListEntity
          items:
              - id: YES
              displayName: YES
              synonyms:
                  - yeah
                  - yep
                  - yup
                  - sure
                  - right
                  - correct
                  - okay
                  - affirmative
              - id: NO
              displayName: NO
              synonyms:
                  - nope
                  - absolutely
                  - wrong
                  - negative
                  - that's not correct
                  - that is not right",
@"entity_type: CustomListEntity
reason: although the GRXML input represents a Boolean, you should make it a CustomListEntity to enable multiple synonyms for the answers 'yes' and 'no'.
copilot_studio_code: |
  - kind: CustomEntityComponent
      displayName: auth2344_STUB_QA
      state: Active
      status: Active
      schemaName: auth23444_STUB_QA
      entity:
          kind: ClosedListEntity
          items:
              - id: HELLO
              displayName: HELLO
              synonyms:
                  - Hi
                  - Heya
                  - Hey
                  - Greetings
              - id: Good Morning
              displayName: GoodMorning
              synonyms:
                  - Good afternoon
                  - Greetings
                  - Good evening",
@"entity_type: CustomListEntity
reason: although the GRXML input represents a Boolean, you should make it a CustomListEntity to enable multiple synonyms for the answers 'yes' and 'no'.
copilot_studio_code: |
  - kind: CustomEntityComponent
      displayName: auth2356_STUB_QA
      state: Active
      status: Active
      schemaName: auth2356_STUB_QA
      entity:
          kind: ClosedListEntity
          items:
              - id: OUTDOOR
              displayName: OUTDOOR
              synonyms:
                  - tent
                  - backpack
                  - hiking boots",
@"entity_type: CustomListEntity
reason: although the GRXML input represents a Boolean, you should make it a CustomListEntity to enable multiple synonyms for the answers 'yes' and 'no'.
copilot_studio_code: |
  - kind: CustomEntityComponent
      displayName: auth2355_STUB_QA
      state: Active
      status: Active
      schemaName: auth2355_STUB_QA
      entity:
          kind: ClosedListEntity
          items:
              - id: SUPPORT
              displayName: SUPPORT
              synonyms:
                  - help
                  - assistance
                  - customer service",
@"entity_type: CustomListEntity
reason: although the GRXML input represents a Boolean, you should make it a CustomListEntity to enable multiple synonyms for the answers 'yes' and 'no'.
copilot_studio_code: |
  - kind: CustomEntityComponent
      displayName: Location_Entity_STUB
      state: Active
      status: Active
      schemaName: Location_Entity_STUB
      entity:
          kind: ClosedListEntity
          items:
              - id: MONTREAL
              displayName: MONTREAL
              synonyms:
                  - MTL
                  - Montréal
                  - Ville-Marie"
    ];
}
