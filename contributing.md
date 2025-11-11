## Contributing with Responsible AI in Mind

We welcome contributions: code, prompts, data, docs, and evaluations. Because this project involves AI, all contributors agree to uphold the principles and practices below to help us ship **safe, transparent, and accountable** AI features.

### Our Responsible AI Principles

- **Safety & Robustness** - Design for fail?safe behavior, resilience, and abuse resistance.
- **Privacy & Security** - Minimize data collection, protect secrets, and respect user privacy expectations.
- **Fairness & Inclusion** - Test for disparate performance and avoid unfair impacts on people or groups.
- **Transparency** - Document what the system can/can't do, intended/unsupported uses, and known limitations.
- **Accountability & Oversight** - Keep a human in the loop for consequential actions and establish clear ownership.

---

## What Kinds of Contributions Are In?Scope?

- **Code & Prompts:** model pipelines, guardrails, inference/runtime logic, tool integrations.
- **Data:** training/finetuning/eval datasets, prompts, red?team sets, and annotations.
- **Evaluations:** quality metrics, safety tests, adversarial suites, and reproducible harnesses.
- **Docs:** model cards, data cards, transparency notes, usage guidance, and incident playbooks.

---

## Before You Start

1. **Define the scope & users.** What problem is the contribution solving? Who are the intended users?  
2. **Check restrictions.** Avoid sensitive/high?risk use cases (e.g., medical diagnosis, legal advice) unless you also contribute appropriate safeguards and documentation.  
3. **Mind licenses & IP.** Only contribute assets you have rights to share under this repo's license.  
4. **Protect privacy.** Do not submit personal data or secrets (API keys, tokens, connection strings). Use synthetic or public, properly licensed data.

---

## Data Contributions

- **No personal/sensitive data.** Remove or anonymize; provide a **Data Card** (template below).  
- **Provenance & license.** State where the data came from and under what terms it's provided.  
- **Bias & representativeness.** Describe known skews, coverage gaps, and any mitigation you applied.  
- **Reproducibility.** Include scripts for generation/curation where feasible.

**Data Card (drop in `/docs/rai/data/<name>.md`):**
```text
# Data Card - <dataset name>
- Source & License:
- Intended Use(s):
- Not for Use In (Unsupported):
- Collection/Generation Process:
- Known Limitations & Skews:
- Privacy/Safety Considerations:
- Versioning & Changes:
