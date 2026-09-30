---
title: Hyper Personalization Agent - Engineering Handoff and Learnings
description: Detailed handoff covering the Dataverse customization, traversal-path model, sample data, and Azure Functions deployment for the Hyper Personalization Agent, written so the next agent or engineer can continue without re-discovery.
author: Engineering
ms.date: 2026-09-30
ms.topic: reference
keywords:
  - hyper personalization
  - dataverse
  - power automate
  - azure functions
  - flex consumption
  - traversal path
estimated_reading_time: 20
---

## Purpose and audience

This document is a **handoff for the next agent/engineer**. It captures everything done and learned while wiring up the
Hyper Personalization Agent end to end: the Dynamics 365 / Dataverse customization, the placeholder traversal model,
the sample data that keeps traversal working, and the Azure Functions deployment (including every dead end and the fix
that actually worked). Read the "Gotchas and hard-won lessons" section first if you are debugging.

## Source repository

* **GitHub:** <https://github.com/KritikaAgarwal01/Hyper-Personalization-Agent>
* **Default branch:** `main`
* **State at handoff:** working tree clean; local `main` equal to `origin/main` (0 ahead / 0 behind); latest commit
  `09db3e8` "Hyperpersionmization changes".

## System overview

The Hyper Personalization Agent generates personalized email text for a Dataverse **Contact** by resolving
template placeholders against related records (Account, Tournament, Participating Teams, Top-1-Rated-Customer),
calling Azure OpenAI, evaluating the result, and writing it back to Dataverse.

End-to-end trigger chain:

```text
Contact form "Trigger HPA" command (modern command bar)
  -> JS web resource ms_hpso_contact  (HPSO.Contact.triggerHyperPersonalization)
    -> HTTP POST { contactid } to a Power Automate "instant" cloud flow
      -> Child flow / orchestration
        -> Azure Function App "HyperPersonalizationAgent" (PromptGenerationFunction)
          -> Placeholder resolution via Dataverse custom API ms_ResolveTraversalPath
          -> Azure OpenAI text generation
          -> Evaluate + publish + write OpenAI Text Output record back to Dataverse
```

### Code layout

| Path | What it is |
|------|------------|
| `CCH.HPSO.Azure/CCH.HPSO.Azure.sln` | .NET 8 Azure Functions solution (isolated worker, Functions v4) |
| `CCH.HPSO.Azure.PromptGenerationApp/` | Builds the prompt, resolves placeholders, calls OpenAI. **This is the app deployed to Azure.** |
| `CCH.HPSO.Azure.EmailTextGenerationApp/` | Email text generation function app |
| `CCH.HPSO.Azure.EvaluateAndPublishApp/` | Evaluation + publish function app |
| `CCH.HPSO.Azure.Shared/` | Shared contracts, services (`DataverseService`, `OpenAIService`, `EvaluationAPIService`), helpers (`PromptMessageBuilder`, `PlaceholderHelper`) |
| `CCH.HPSO.Azure.UnitTest/` | xUnit tests per function app |
| `CCH.HPSO.Dataverse.Plugins/` | Dataverse plugin assembly: custom API backing plugin + traversal resolver |

> Note: the solution has **three** Function projects but only **one** Azure Function App is provisioned
> (`HyperPersonalizationAgent`). Only `CCH.HPSO.Azure.PromptGenerationApp` was deployed to it. If the other two need
> hosting, they require their own Function Apps.

## Environments and identifiers

### Dataverse (Dynamics 365) org

| Field | Value |
|-------|-------|
| Friendly name | IGD_CS_Demo (Customer Insights - Journeys / Marketing org) |
| URL | `https://orgf48d3b56.crm.dynamics.com/` |
| Org ID | `d395a03e-3819-f011-9aed-6045bd027c1b` |
| Environment ID | `a3ace7c2-edb9-e4c5-bbb7-229ad1b7670b` |
| Unique name | `unqd395a03e3819f0119aed6045bd027` |
| Auth user | `IGDAdmin@axsolutionsarchitecture.com` (tenant `axsolutionsarchitecture.com`) |
| Customization publisher | `Microsoft` / prefix `ms` / OptionValuePrefix `12470` |
| Model-driven app | **Hyper Personalization Application** |

> This is a **shared sandbox/demo** org that was near capacity (< ~15% Dataverse storage remaining at the time).
> Prefer additive, minimal data changes and avoid table-wide metadata changes without confirmation.

### Azure Function App

| Field | Value |
|-------|-------|
| Name | `HyperPersonalizationAgent` |
| Subscription | Visual Studio Enterprise (`2bb8d490-69a9-4a84-8c3f-7e9edbd451b6`) |
| Resource group | `HyperPersonalizationAgent` |
| Region | East US |
| OS / Plan | Linux / **Flex Consumption** |
| Default domain | `hyperpersonalizationagent-bba3eahzb6a6agfk.eastus-01.azurewebsites.net` |
| SCM host | `hyperpersonalizationagent-bba3eahzb6a6agfk.scm.eastus-01.azurewebsites.net` |
| Deployed functions | `PromptGenerationFunction_Http`, `PromptGenerationFunction_ServiceBus` |

## Dynamics customization work completed

### 1. JavaScript web resource `ms_hpso_contact`

* Source: `CCH.HPSO.Dataverse.WebResources/hpso_contact.js`.
* Namespace `HPSO.Contact`, entry point `triggerHyperPersonalization(primaryControl)`.
* Reads the Contact GUID: `formContext.data.entity.getId().replace(/[{}]/g,"").toLowerCase()`.
* Sends `POST { "contactid": <guid> }` to the flow URL with **`Content-Type: text/plain;charset=UTF-8`**.
  This is deliberate: text/plain avoids a CORS preflight from the browser. (See the flow-side consequence below.)
* Shows a progress indicator and success/error dialogs.
* Deployed as web resource **`ms_hpso_contact`** (type 3 = JScript).

### 2. Deploying the web resource without `pac webresource`

`pac` (Power Platform CLI) has **no** `webresource` command. The web resource + button were deployed by building a
minimal **unmanaged solution** and importing it:

* Publisher node `Microsoft` (prefix `ms`).
* `pac solution pack --packagetype Unmanaged` then
  `pac solution import --path <zip> --publish-changes true`.
* Browser-console `Xrm.WebApi` / Web API scripts (run in F12) were the established pattern for ad-hoc create/update,
  because `pac` cannot do ad-hoc record CRUD.

### 3. The big lesson: legacy ribbon vs modern command bar

* The solution originally added the button via classic **RibbonDiffXml** at
  `Mscrm.Form.contact.MainTab.Save.Controls._children`.
* On the modern command bar for the system `contact` table (heavily customized Marketing app), the classic button is
  treated as a **"legacy button" and never renders**. The command designer showed it read-only with
  "Legacy button is not supported at the moment."
* **Fix that worked:** create a **modern command** in the command designer
  (make.powerapps.com -> Tables -> Contact -> Edit command bar -> Main form -> + New command):
  * Label: **Trigger HPA**
  * Action: **Run JavaScript**
  * Library: `ms_hpso_contact`
  * Function: `HPSO.Contact.triggerHyperPersonalization`
  * Parameter: **PrimaryControl**
  * Visibility: Show; Icon: Activate; Scope: **App**
  * Order: **100100010** (same as Save). An initial order of `100100060` pushed it into the overflow "..." menu and
    made it look "missing" -- lowering the order number made it visible on the main bar.

### 4. Flow "InvalidTemplate" 502 fix

* Flow name: **Hyperpersonalization Trigger** (instant cloud flow, "When an HTTP request is received").
* Symptom: 502 at the `ContactId` Compose action:
  "property 'contactid' cannot be selected. Property selection is not supported on values of type 'String'."
* Root cause: because the JS sends `Content-Type: text/plain`, Power Automate treats the body as a **String**, not JSON.
* **Fix:** in the `ContactId` Compose input use `json(triggerBody())?['contactid']` instead of
  `triggerBody()?['contactid']`.

## Placeholder traversal model (critical for data integrity)

This is the heart of the system and the part most easily broken by bad data.

### Custom API and plugin

* Custom API: **`ms_ResolveTraversalPath`**, backed by `ResolveTraversalPathPlugin` in `CCH.HPSO.Dataverse.Plugins`.
* Request params: `PlaceholderName` (e.g. `{tournament_name}`), `EntityName` (pivot table logical name),
  `EntityGuid`, `ValueMode` ("Formatted"/"Raw").
* Response: `Result` (JSON), `IsCollection`, `ValueCount`, `TraversalPath`.
* The plugin looks up the placeholder in the **mapping table** to get a dotted traversal path, then delegates to
  `TraversalPathResolver` to walk that path from the pivot record and return the leaf attribute value(s).

### Mapping table

* Table: **`ms_prompttemplateattributemapping`**
* Key columns: `ms_placeholdername` (the `{placeholder}`) and `ms_traversalpath` (the dotted path).

### How `TraversalPathResolver` walks a path

* Splits the path on `.`; the last token is the **attribute**, middle tokens are **hops**.
* Each hop resolves in this order: (1) relationship **schema name**, (2) **lookup attribute** name (N:1),
  (3) **target entity logical name** reachable by exactly one relationship. Ambiguity throws.
* Supports ManyToOne (via ReferencingAttribute), OneToMany (children via ReferencingAttribute IN), and
  ManyToMany (via intersect entity). Guardrails: MaxFrontier 5000, InBatchSize 500.
* Returns null gracefully when a mapping/path/data is missing.

### Configured placeholders (live data at handoff)

| Placeholder | Traversal path |
|-------------|----------------|
| `{accountname}` | `contact.contact_customer_accounts.name` |
| `{city}` | `contact.address1_city` |
| `{contact.fullname}` | `contact.fullname` |
| `{sponsor_name}` | `contact.contact_customer_accounts.ms_tournament.ms_sponsorname` |
| `{host_organization}` | `contact.contact_customer_accounts.ms_tournament.ms_hostorganizationid.name` |
| `{region}` | `contact.contact_customer_accounts.ms_tournament.ms_region` |
| `{country}` | `contact.contact_customer_accounts.ms_tournament.ms_country` |
| `{tournament_name}` | `contact.contact_customer_accounts.ms_tournament.ms_name` |
| `{tournament_type}` | `contact.contact_customer_accounts.ms_tournament.ms_tournamenttype` |
| `{start_date}` | `contact.contact_customer_accounts.ms_tournament.ms_tournamentstartdate` |
| `{end_date}` | `contact.contact_customer_accounts.ms_tournament.ms_tournamentenddate` |
| `{venue_name}` | `contact.contact_customer_accounts.ms_tournament.ms_venuename` |
| `{target_audience}` | `contact.contact_customer_accounts.ms_tournament.ms_targetaudience` |
| `{participating_teams}` | `contact.contact_customer_accounts.ms_tournament.ms_participatingteam.ms_teamname` |
| `{special_attractions}` | `contact.ms_top1ratedcustomer.ms_specialattraction` |
| `{promotion_reason}` | `contact.ms_top1ratedcustomer.ms_promotionreason` |

### Relationships the traversal depends on

These MUST be populated for placeholders to resolve. Breaking any of them breaks the corresponding placeholder(s).

| From -> To | Relationship schema | Lookup / FK | Web API nav property |
|------------|---------------------|-------------|----------------------|
| contact -> account | `contact_customer_accounts` | `parentcustomerid` | `parentcustomerid_account@odata.bind` |
| account -> tournament (1:N) | `ms_account_ms_tournament` | `ms_tournament.ms_hostorganizationid` | `ms_HostOrganizationId@odata.bind` |
| tournament -> host account | (same as above) | `ms_hostorganizationid` | -- |
| tournament -> participating team (1:N) | `ms_tournament_ms_participatingteam` | `ms_participatingteam.ms_tournamentid` | `ms_TournamentId@odata.bind` |
| contact -> top-1-rated-customer (1:N) | `ms_contact_ms_top1ratedcustomer` | `ms_top1ratedcustomer.ms_contactid` | `ms_ContactId@odata.bind` |

Table facts:

| Table | Entity set | Primary name | Primary id |
|-------|-----------|--------------|------------|
| `ms_tournament` | `ms_tournaments` | `ms_name` | `ms_tournamentid` |
| `ms_participatingteam` | `ms_participatingteams` | `ms_teamname` | `ms_participatingteamid` |
| `ms_top1ratedcustomer` | `ms_top1ratedcustomers` | `ms_name` | `ms_top1ratedcustomerid` |

> Key insight: `ms_top1ratedcustomer` is **not** an N:1 lookup on contact. It is the child side of a 1:N
> (`ms_top1ratedcustomer.ms_contactid` -> contact). Likewise participating teams are children of a tournament.
> When creating data, bind from the child (team/top1) up to its parent (tournament/contact), not the other way.

## Sample data created

Five self-contained "contact graphs" were created to exercise every placeholder. Each graph = 1 account + 1 contact +
1 tournament + 2 participating teams + 1 top-1-rated-customer, all suffixed `(HPA sample)`:

1. Mumbai Strikers Cricket Club -> Rohit Sharma -> Mumbai T20 Premier Cup 2026 (teams: Mumbai Strikers, Pune Warriors)
2. London Lions FC -> Emma Watson -> Thames Football Championship 2026 (teams: London Lions, Manchester United Academy)
3. Sydney Surf Riders -> Liam Chen -> Pacific Beach Volleyball Open 2026 (teams: Sydney Surf, Gold Coast Waves)
4. Toronto Thunder Hockey Club -> Olivia Martin -> Maple Leaf Ice Hockey Cup 2026 (teams: Toronto Thunder, Montreal Blizzard)
5. Berlin Eagles Basketball -> Noah Mueller -> Euro Hoops Invitational 2026 (teams: Berlin Eagles, Munich Falcons)

Creation approach (browser console `Xrm.WebApi` / Web API `fetch`):

* Self-discovers attribute types first (`EntityDefinitions/Attributes`) and only writes String/Memo/DateTime/Integer
  fields, so a choice/optionset field never causes the POST to fail (it is simply skipped, and the placeholder resolves
  empty rather than erroring).
* Creates records in dependency order and binds the lookups listed in the relationships table above.
* The script is **not idempotent** -- re-running creates duplicates.

To verify traversal end to end: open one of the sample contacts (e.g. Rohit Sharma) and click **Trigger HPA**; every
placeholder should now resolve to real data.

## Azure Functions deployment (detailed)

Goal: publish `CCH.HPSO.Azure.PromptGenerationApp` to the `HyperPersonalizationAgent` Function App.

### Build and package

```powershell
$pub = "$env:TEMP\hpso_publish\prompt"
dotnet publish `
  "CCH.HPSO.Azure\CCH.HPSO.Azure.PromptGenerationApp\CCH.HPSO.Azure.PromptGenerationApp.csproj" `
  -c Release -o $pub
Compress-Archive -Path (Join-Path $pub '*') -DestinationPath "$env:TEMP\hpso_publish\prompt.zip" -Force
```

Build succeeds with nullable (CS8604/CS8619) warnings only.

### What worked (final method)

Basic-auth publishing is **disabled** on the app, so the downloaded publish profile cannot be used. Deploy with
Entra ID via Azure CLI instead:

```powershell
az login --use-device-code
az account set --subscription 2bb8d490-69a9-4a84-8c3f-7e9edbd451b6
az functionapp deployment source config-zip `
  -g HyperPersonalizationAgent -n HyperPersonalizationAgent `
  --src "$env:TEMP\hpso_publish\prompt.zip"
# Verify
az functionapp function list -g HyperPersonalizationAgent -n HyperPersonalizationAgent --query "[].name" -o tsv
```

Result: `"Deployment was successful."` and the two functions appear:
`PromptGenerationFunction_Http`, `PromptGenerationFunction_ServiceBus`.

### Gotchas and hard-won lessons

* **Publish profile zip deploy returns 401.** Basic-auth publishing credentials are disabled on the app, so
  `POST https://<scm>/api/zipdeploy` with the profile's Basic auth fails. Use `az` (Entra ID) or enable SCM basic auth.
* **Flex Consumption changes deployment.** The app is on a **Flex Consumption** plan. `az functionapp deployment
  source config-zip` internally takes the Flex "OneDeploy" path (`enable_zip_deploy_flex`). A manual OneDeploy to
  `https://<app>.scm...azurewebsites.net/api/publish` must use the **full SCM host with the `-bba3eahzb6a6agfk`
  suffix** and an **AAD Bearer token** (`az account get-access-token`), not Basic auth. Using the bare
  `hyperpersonalizationagent.scm...` host fails with "No such host is known."
* **Transient `WinError 10053` during status polling.** The first `config-zip` uploaded the package but the
  deployment-status long-poll was aborted (looks like a proxy/firewall dropping the SSL connection). **Simply retry** --
  it succeeded on the next attempt. Wrap the deploy in a small retry loop.
* **Interactive `az login` in an agent-driven terminal.** Device-code login can be killed by idle-timeout Ctrl+C in an
  automated terminal ("Terminate batch job (Y/N)?"). Run `az login --use-device-code` in a **human-controlled**
  terminal, complete the browser step, then continue automated commands.
* **`func` Core Tools not installed.** Only `az` (v2.69) is available on this machine; `func azure functionapp publish`
  is not an option unless Core Tools are installed.
* **App settings are not part of the code zip.** The zip deploys code only. The app still needs its **application
  settings / environment variables** (Dataverse connection, Azure OpenAI endpoint + key, Service Bus connection,
  Application Insights). Verify with
  `az functionapp config appsettings list -g HyperPersonalizationAgent -n HyperPersonalizationAgent`.

## Security follow-ups (open)

* **`HyperPersonalizationAgent.PublishSettings`** (in the user's Downloads) contains real publish credentials in
  plain text. Delete it or use **Reset publish profile** in the portal, since it was shared during this work.
* The **flow trigger URL** embedded in `hpso_contact.js` is an "Anyone"-authorized endpoint with a SAS `sig` visible in
  client-side JavaScript. If security matters, move to a **Dataverse-triggered** flow instead of a public HTTP trigger.
  (The SAS signature is intentionally omitted from this document.)
* Rotate any previously exposed secrets from local settings: Azure AD client secret, Service Bus key, Azure OpenAI key.

## Quick command reference

```powershell
# Dataverse auth (device code)
pac auth create --environment https://orgf48d3b56.crm.dynamics.com/ --deviceCode

# Solution pack + import (web resources / ribbon)
pac solution pack --packagetype Unmanaged --zipfile <zip> --folder <src>
pac solution import --path <zip> --publish-changes true

# Build + deploy the Function App
dotnet publish CCH.HPSO.Azure\CCH.HPSO.Azure.PromptGenerationApp\CCH.HPSO.Azure.PromptGenerationApp.csproj -c Release -o <out>
az functionapp deployment source config-zip -g HyperPersonalizationAgent -n HyperPersonalizationAgent --src <zip>

# Run unit tests
dotnet test CCH.HPSO.Azure\CCH.HPSO.Azure.sln
```

## Suggested next steps

* Confirm the flow `json(triggerBody())?['contactid']` fix is saved and the end-to-end trigger returns 200.
* Verify Function App application settings are populated (see deployment gotchas).
* Decide whether `EmailTextGenerationApp` and `EvaluateAndPublishApp` need their own Function Apps.
* Optionally curate the Contact/Account "Related" tab to show only solution tables (table-wide metadata change --
  get sign-off first because the org is shared).
* Address the security follow-ups above.
