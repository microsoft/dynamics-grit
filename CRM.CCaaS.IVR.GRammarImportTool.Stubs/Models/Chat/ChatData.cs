namespace CRM.CCaaS.IVR.GRammarImportTool.Stubs.Models.Chat;

public static class ChatData
{
    public static readonly string[] YAML_REPLY_DATA =
    [
@"entity_type: CustomListEntity
reason: although the GRXML input represents a Boolean, you should make it a CustomListEntity to enable multiple synonyms for the answers 'yes' and 'no'.
copilot_studio_code: |
  - kind: CustomEntityComponent
      displayName: auth7561_IDVConfPhNum_QA
      state: Active
      status: Active
      schemaName: auth7561_IDVConfPhNum_QA
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
      displayName: auth2344_IDVConfPhNum_QA
      state: Active
      status: Active
      schemaName: auth23444_IDVConfPhNum_QA
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
      displayName: auth2355_IDVConfPhNum_QA
      state: Active
      status: Active
      schemaName: auth2355_IDVConfPhNum_QA
      entity:
        kind: ClosedListEntity
        items:
            - id: OUTDOOR
            displayName: Outdoor Gear
            synonyms:
                - tent
                - backpack
                - hiking boots",
@"entity_type: CustomListEntity
reason: although the GRXML input represents a Boolean, you should make it a CustomListEntity to enable multiple synonyms for the answers 'yes' and 'no'.
copilot_studio_code: |
  - kind: CustomEntityComponent
      displayName: auth2355_IDVConfPhNum_QA
      state: Active
      status: Active
      schemaName: auth2355_IDVConfPhNum_QA
      entity:
        kind: ClosedListEntity
        items:
            - id: SUPPORT
            displayName: Support
            synonyms:
                - help
                - assistance
                - customer service",
@"entity_type: CustomListEntity
reason: although the GRXML input represents a Boolean, you should make it a CustomListEntity to enable multiple synonyms for the answers 'yes' and 'no'.
copilot_studio_code: |
  - kind: CustomEntityComponent
    displayName: Location_Entity
    state: Active
    status: Active
    schemaName: Location_Entity
    entity:
      kind: ClosedListEntity
      items:
        - id: MONTREAL
          displayName: Montreal
          synonyms:
            - MTL
            - Montréal
            - Ville-Marie"
    ];
}
