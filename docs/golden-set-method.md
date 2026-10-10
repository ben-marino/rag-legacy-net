# Golden set candidates: method and verification

This document goes with `data/golden.candidates.json`, which holds 55 **candidate** questions for the retrieval eval. Nothing here is final. A human reviewer cuts the list to 40 and writes `data/golden.json` by hand.

**How to review one candidate (target: under 3 minutes).**

1. Open `corpus/Umbraco-CMS/<evidence.file>` at `evidence.lines`. Search for `evidence.snippet`; it is copied verbatim and is guaranteed to be inside that range (see section 6).
2. Read the question and check that the member answers it.
3. For cross-cutting questions, repeat step 1 for each entry in `evidence_additional`, one per extra correct file.
4. Read `drafting_notes` for alternative files I considered and rejected. Most disagreements will be about whether one of those should be added to `correct_files`.

**Fields to strip before copying into `golden.json`.** Only `id`, `question`, `correct_files`, `category` and `lsp_trivial` are in the SPEC schema. Everything else is review scaffolding: `subsystem`, `discriminator`, `evidence`, `evidence_additional`, `leakage_check`, `structural`, `why`, `confidence`, `drafting_notes`. I added two fields beyond the brief's object shape:

- `evidence_additional`: evidence for the second and third file of a cross-cutting question.
- `structural`: file length, declaration line, doc-comment and body length, all computed from the source.

Candidate ids are `c01`–`c55`, grouped by subsystem. Rename them to `qNN` when you promote them.

---

## 1. Corpus and scope

| Item | Value |
|---|---|
| Corpus path | `corpus/Umbraco-CMS` |
| Tag | `release-7.15.11` (`git -C corpus/Umbraco-CMS describe --tags`) |
| Include | `src/**/*.cs` |
| Exclude (path contains) | `/Umbraco.Tests`, `/Umbraco.Web.UI.Client/`, `/obj/`, `/bin/` |
| Files indexed | **3,292** |
| Path convention in JSON | relative to `corpus/Umbraco-CMS`, e.g. `src/Umbraco.Core/Services/ContentService.cs` |

Command used for the file list, run from `corpus/Umbraco-CMS`:

```
find src -name '*.cs' | grep -v -e /Umbraco.Tests -e /Umbraco.Web.UI.Client/ -e /obj/ -e /bin/ | wc -l
3292
```

The `/Umbraco.Tests` filter also excludes `src/Umbraco.Tests.Benchmarks`.

Indexed `.cs` files per project:

```
1523 Umbraco.Core          164 umbraco.editorControls     43 umbraco.datalayer    27 umbraco.interfaces
1164 Umbraco.Web            56 umbraco.MacroEngines       43 UmbracoExamine        10 SQLCE4Umbraco
 146 umbraco.cms            39 umbraco.businesslogic      38 Umbraco.Web.UI        10 umbraco.providers
  28 umbraco.controls        1 SolutionInfo.cs (src root)
```

**Spec file name.** The brief says "SPEC.md, section Golden set format". There is no `SPEC.md` in the repo. The section exists in `spec-chunking-ablation.md` (heading at line 88), and I used that schema.

## 2. Orientation: subsystem map

`tree` is not installed on this machine, so I listed the projects with `find src -maxdepth 2 -type d | grep -v -e /obj -e /bin -e Umbraco.Web.UI.Client | sort`.

Projects under `src/`: SQLCE4Umbraco, umbraco.businesslogic, umbraco.cms, umbraco.controls, Umbraco.Core, umbraco.datalayer, umbraco.editorControls, umbraco.interfaces, umbraco.MacroEngines, umbraco.providers, Umbraco.Tests (excluded), Umbraco.Tests.Benchmarks (excluded), Umbraco.Web, Umbraco.Web.UI, Umbraco.Web.UI.Client (excluded), UmbracoExamine.

I read every file below (the relevant regions, at least) before drafting anything from it.

**1. Core services** (`src/Umbraco.Core/Services/`)
- `ContentService.cs` (2,949 lines): document CRUD, publish, unpublish, sort, copy, rollback and recycle bin. Every operation opens a scoped unit of work and raises `Saving`/`Saved`/… through `uow.Events`.
- `MediaService.cs` (1,566 lines): the same pattern for media, plus media-file helpers (`GetMediaByPath`, file streams, thumbnails).
- `MemberService.cs` (1,454 lines): members, roles, a default member type and a password wrapper that defers to the membership provider.
- `../Publishing/PublishingStrategy.cs`: does the actual publish and **declares** `Publishing`/`Published`. `ContentService`'s events of those names only proxy to it.

**2. Persistence: repositories and unit of work**
- `Persistence/UnitOfWork/ScopeUnitOfWork.cs`: queues Insert/Update/Delete operations. `Commit()` flushes them to the repositories, and disposing the unit completes or abandons its scope.
- `Scoping/Scope.cs`: the ambient scope. The outermost scope commits or aborts the DB transaction, and any uncompleted child forces an abort.
- `Persistence/PetaPoco.cs` (2,554 lines): the vendored micro-ORM, covering SQL building, paging and transactions. `Persistence/UmbracoDatabase.cs` subclasses it.
- `Persistence/Repositories/SimilarNodeName.cs`: generates unique sibling names ("Name (n)").

**3. Cache** (`src/Umbraco.Web/Cache/`, `src/Umbraco.Web/PublishedCache/`)
- `Cache/CacheRefresherEventHandler.cs`: subscribes to service events and turns them into `DistributedCache` calls.
- `Cache/DistributedCache.cs` + `DistributedCacheExtensions.cs`: refresher GUIDs, and broadcasting refresh/remove calls to all servers.
- `Cache/PageCacheRefresher.cs`: runs on each server and updates the legacy XML content cache, then clears the partial-view, macro and XSLT caches.
- `PublishedCache/XmlPublishedCache/PublishedContentCache.cs` (+ `RoutesCache.cs`, `XmlCacheFilePersister.cs`): route↔id lookups over the XML cache, and the background writer of `umbraco.config`.

**4. Events**
- `Core/Events/ScopeEventDispatcherBase.cs`: queues events raised inside a scope, filters superseded ones, and discards them all if the scope is not completed.
- `Core/Events/ScopeEventDispatcher.cs`: raises queued events on scope completion and deletes media files listed on delete args.
- `Core/Events/CancellableEventArgs.cs`, `DeleteEventArgs.cs`, `SupersedeEventAttribute.cs`: the args hierarchy, cancel support, and the "Delete supersedes Save/Publish/Move/Copy" rule.
- `Core/Strategies/RelateOnTrashHandler.cs`, `RelateOnCopyHandler.cs`: examples of application event handlers subscribing to `ContentService` events.

**5. Web routing** (`src/Umbraco.Web/Routing/`)
- `PublishedContentRequest.cs`: request state (content, template, redirect, 404). `Prepare()` delegates to the engine.
- `PublishedContentRequestEngine.cs`: finds the domain, runs the content finders, then handles 404/internal redirects/access and finds the template.
- `ContentFinderBy*.cs` (e.g. `ContentFinderByNiceUrlAndTemplate.cs`, `ContentFinderByRedirectUrl.cs`): the `IContentFinder` implementations.
- `RedirectTrackingEventHandler.cs`: records old URLs on publish/move for automatic 301s.

**6. HttpModules and request pipeline**
- `Umbraco.Web/UmbracoModule.cs`: the main `IHttpModule`. `BeginRequest` creates `UmbracoContext`; `PostResolveRequestCache` calls `ProcessRequest` (routable checks, then `PublishedContentRequest`); `EndRequest` disposes request items.
- `Umbraco.Web/UmbracoContext.cs`: the per-request singleton; `EnsureContext` creates or replaces it.
- `umbraco.presentation/requestModule.cs`: the legacy module, with first-request init, timers and log scrubbing.
- `umbraco.presentation/viewstateMoverModule.cs`: a legacy module that moves viewstate via a response filter.

**7. Backoffice API controllers** (`src/Umbraco.Web/Editors/`)
- `ContentController.cs`: the content editor API (save/publish/delete/sort/permissions) and the publish-status → message mapping.
- `MediaController.cs`: the media editor API, including multipart upload (`PostAddFile`).
- `AuthenticationController.cs`: back-office login, 2FA and password reset.
- `ContentPostValidateAttribute.cs`, `PasswordChanger.cs`: the permission filter for content saves, and the shared password-change rules.

**8. Legacy layer**
- `Umbraco.Web/umbraco.presentation/library.cs` (1,992 lines): the `umbraco.library` XSLT extension statics.
- `Umbraco.Web/umbraco.presentation/macro.cs` (1,927 lines): the legacy macro renderer (XSLT/user control/script), its caching and in-editor HTTP rendering.
- `umbraco.cms/businesslogic/web/Document.cs` (1,924 lines): an obsolete business-logic wrapper that forwards to `ContentService` and fires the old events.
- `Umbraco.Web/umbraco.presentation/umbraco/**.aspx.cs`: WebForms back-office code-behind, e.g. `dialogs/republish.aspx.cs` and `dashboard/FeedProxy.aspx.cs`.

## 3. Structural facts (Step 2)

All commands were run from `corpus/Umbraco-CMS` over the 3,292-file list above (saved as `files.txt` in my scratch directory). I did not use `src/RagAblation.Chunking`.

### 3a. Long doc comments (8+ lines)

An awk scan counts runs of consecutive `///` lines, skips attribute lines between the doc and the declaration, and reports the declaration that follows:

```awk
FNR==1{n=0}
/^[ \t]*\/\/\// { if(n==0) start=FNR; n++; next }
{ if(n>=8){ if($0 ~ /^[ \t]*\[/){ next } decl=$0; gsub(/^[ \t]+/,"",decl);
    printf "%d\t%s:%d-%d\t%s\n", n, FILENAME, start, FNR, substr(decl,1,110) } n=0 }
```
```
tr '\n' '\0' < files.txt | LC_ALL=C xargs -0 awk -f longdoc.awk > longdoc.tsv
```

(`LC_ALL=C` is needed because `umbraco.editorControls/DefaultData.cs` contains non-UTF-8 bytes.)

**Result: 1,525 declarations (members and types) with an 8+ line doc comment.** The top 20:

```
lines  file:doc-start-decl                                      declaration
66	src/umbraco.cms/businesslogic/utilities/Diff.cs:9-75	
58	src/Umbraco.Web/UrlHelperRenderExtensions.cs:148-206	public static IHtmlString GetCropUrl(this UrlHelper urlHelper,
58	src/Umbraco.Web/UrlHelperRenderExtensions.cs:67-125	public static IHtmlString GetCropUrl(this UrlHelper urlHelper,
53	src/Umbraco.Core/Persistence/BulkDataReader.cs:159-212	protected void AddSchemaTableRow(string columnName,
53	src/Umbraco.Web/ImageCropperTemplateExtensions.cs:161-214	public static string GetCropUrl(
53	src/Umbraco.Web/ImageCropperTemplateExtensions.cs:53-106	public static string GetCropUrl(
51	src/Umbraco.Web/ImageCropperTemplateExtensions.cs:242-293	public static string GetCropUrl(
41	src/Umbraco.Core/Xml/DynamicContext.cs:147-188	public void AddVariable(string name, object value)
31	src/Umbraco.Core/Publishing/PublishingStrategy.cs:117-148	IEnumerable<Attempt<PublishStatus>> IPublishingStrategy2.PublishWithChildren(ISc
29	src/Umbraco.Core/Persistence/BulkDataReader.cs:853-882	public long GetBytes(int i,
29	src/Umbraco.Core/Persistence/BulkDataReader.cs:942-971	public long GetChars(int i,
28	src/umbraco.businesslogic/BasePages/ClientTools.cs:220-248	public ClientTools SyncTree(string path, bool forceReload)
28	src/Umbraco.Core/Logging/RollingFileCleanupAppender.cs:8-36	public class RollingFileCleanupAppender : RollingFileAppender
28	src/Umbraco.Web/UI/Pages/ClientTools.cs:226-254	public ClientTools SyncTree(string path, bool forceReload)
26	src/Umbraco.Core/UriExtensions.cs:15-41	internal static bool IsBackOfficeRequest(this Uri url, string applicationPath)
25	src/umbraco.cms/businesslogic/Packager/PackageActions/addDashboardSection.cs:15-40	public bool Execute(string packageName, XmlNode xmlData)
25	src/Umbraco.Web/Mvc/AreaRegistrationExtensions.cs:15-40	internal static Route RouteControllerPlugin(this AreaRegistration area, string c
24	src/Umbraco.Web/Mvc/UmbracoVirtualNodeRouteHandler.cs:16-40	protected virtual UmbracoContext GetUmbracoContext(RequestContext requestContext
23	src/Umbraco.Web/UmbracoContext.cs:91-114	public static UmbracoContext EnsureContext(
22	src/Umbraco.Core/ApplicationContext.cs:259-281	public string UmbracoApplicationUrl
```

Members used for the `long-doc` discriminator. Doc-comment line counts were re-computed per member by the build script and are in `structural.doc_comment_lines`:

| id | member | doc lines |
|---|---|---|
| c18 | CacheRefresherEventHandler.ContentService_Copied | 9 |
| c22 | EventExtensions.IsRaisedEventCancelled | 10 |
| c23 | EventNameExtractor.FindEvent | 11 (+14-line class doc) |
| c38 | UmbracoContext.EnsureContext (6th overload, L114) | 23 |
| c39 | UriExtensions.IsBackOfficeRequest | 26 |
| c41 | ContentController.DeleteById | 9 |
| c44 | MediaController.GetParentIdAsInt | 9 |
| c53 | Document.PublishWithResult | 13 |

These also have 8+ line docs but carry another label: c02 Rollback (12), c09/c11 BuildSqlDbSpecificPagingQuery (11) and c48 MacroNeedsToBeClearedFromCache (11), all `deep-in-large-class`; c15 XmlCacheFilePersister has a 9-line *type-level* doc, labelled `none`.

### 3b. Large files (> 1500 lines)

```
tr '\n' '\0' < files.txt | xargs -0 wc -l | awk '$1>1500 && $2!="total"' | sort -rn
```
```
    3628 src/Umbraco.Core/Strings/Utf8ToAsciiConverter.cs
    2949 src/Umbraco.Core/Services/ContentService.cs
    2554 src/Umbraco.Core/Persistence/PetaPoco.cs
    2310 src/Umbraco.Web/Dynamics/ExpressionParser.cs
    2070 src/Umbraco.Web/PublishedContentExtensions.cs
    1992 src/Umbraco.Web/umbraco.presentation/library.cs
    1977 src/Umbraco.Core/Services/PackagingService.cs
    1937 src/umbraco.MacroEngines/RazorDynamicNode/DynamicNode.cs
    1927 src/Umbraco.Web/umbraco.presentation/macro.cs
    1924 src/umbraco.cms/businesslogic/web/Document.cs
    1785 src/umbraco.cms/businesslogic/ContentType.cs
    1732 src/Umbraco.Web/UmbracoHelper.cs
    1604 src/Umbraco.Core/Services/ContentTypeService.cs
    1603 src/Umbraco.Core/StringExtensions.cs
    1566 src/Umbraco.Core/Services/MediaService.cs
    1511 src/Umbraco.Core/Persistence/BulkDataReader.cs
```

`deep-in-large-class` candidates have their member in one of these files, declared after line 100:

| id | file | decl line / file lines |
|---|---|---|
| c01 | ContentService.cs | 1939 / 2949 |
| c02 | ContentService.cs | 1974 / 2949 |
| c09 | PetaPoco.cs | 753 / 2554 |
| c10 | PetaPoco.cs | 385 / 2554 |
| c11 | PetaPoco.cs (+ UmbracoDatabase.cs) | 753 / 2554 |
| c42 | ContentService.cs (+ ContentController.cs) | 2717 / 2949 |
| c48 | macro.cs | 734 / 1927 |
| c50 | library.cs | 1539 / 1992 |

Also deep but labelled otherwise: c04 (MediaService.cs 730/1566), c49 (macro.cs 1785), c51 (library.cs 1687), c53 (Document.cs 798/1924), c54 (library.cs 204).

### 3c. Short members (≤ 6 body lines)

A Python scan finds method-shaped declarations (access modifier, `(`, no `;`/`=`) whose next line is `{`, then counts lines up to the matching `}` at the same indent:

```python
decl=re.compile(r'^(\s*)(public|private|protected|internal)\b[^;=]*\([^;]*$')
# ... if lines[i+1].strip()=='{': walk to the line that is indent+'}' ; body = j-(i+2) ; keep 1<=body<=6
```

**Result: 7,467 short methods, 657 of them in files over 1,500 lines.** The scan cannot judge "unrelated neighbours", so I checked adjacency by reading the surrounding code for each one I used:

| id | member | body lines | neighbours (above / below) | verdict |
|---|---|---|---|---|
| c04 | MediaService.GetMediaByPath | 5 | GetMediaInRecycleBin / HasChildren | unrelated |
| c49 | macro.ShowNoMacroContent | 2 | MacroContentByHttp / ValidateRemoteCertificate (TLS callback) | unrelated |
| c51 | library.ChangeContentType | 4 | Request(key) / ContextKey(key) | unrelated |
| c37 | requestModule.AddTask | 4 | GetLogScrubbingMaximumAge / CacheItemRemoved | weak: same #region, different concerns |
| c54 | library.RefreshContent | 1 | PublishSingleNode / RePublishNodes | weak: sibling publish helpers |

Two of the five (c37, c54) are weak fits for "unrelated neighbours", and both are flagged medium confidence. If you need five strong ones, `library.ReplaceLineBreaks` (L1011, between ShortDate and RenderMacroContent) is a clean spare. It is not drafted, because `library.cs` already has one candidate in each category.

## 4. Drafting method

For each candidate:

1. **Open a file from the section 2 map and read a specific member.** The question is written only after I understand what that member does. No question was written first and matched to a file afterwards.
2. **Record the member's declaration line** (a verbatim substring) and a verbatim **proof snippet**. The build script (`build.py`, scratch directory) finds the declaration in the source and computes `evidence.lines`. The range runs from the first `///` line above the declaration (or the declaration itself if there is no doc) to its matching closing brace. Four ranges are special:
   - c13 is a single-line constant.
   - c15 is a manual range from the class doc to the two constants.
   - c21 covers the whole class, because the evidence is the class attributes.
   - c53 extends through the internal overload directly below.
3. **Write the question.**
   - Navigation questions name the identifier on purpose and have exactly one correct file.
   - Semantic questions describe behaviour in plain words and have exactly one correct file.
   - Cross-cutting questions have 2–3 files, each with its own evidence entry.
4. **Leakage check (semantic and cross-cutting only).** Extract the 3–9 content nouns of the question and test each against every correct file:
   - Tokenise the code with `/* */`, `//`, `///` comments and string/char literals stripped.
   - Split every identifier into camelCase parts.
   - A noun **leaks** if any part (or any suffix-join of parts, e.g. `cachehelper`) **starts with** the noun, case-insensitive.
   - If anything leaks, rephrase and re-run until `found_as_identifier` is empty.

   The check is deliberately stricter than "grep -i". It catches C# keywords (`public`, `string`, `delegate` are tokens) and prefix stems (`page` → `GetPagedChildren`). That is why some questions use slightly unusual words ("webpage", "job", "machine in a farm", "rich-text box"). If you think a word is fine, re-run the check by hand with `grep -iwn`.
5. **Collision rules.** One semantic question per file, and one cross-cutting question per file. A file may appear once per category. This is why some cross-cutting candidates omit a file that is also on the code path; each omission is stated in `drafting_notes`.

### Worked example A: navigation (c20)

- **File.** `src/Umbraco.Core/Services/ContentService.cs`, event region.
- **Member.** `ContentService.Published` at L2805–2809 is an event with custom accessors: `add { PublishingStrategy.Published += value; }` (L2807). So `ContentService` never raises it.
- **Follow.** `src/Umbraco.Core/Publishing/PublishingStrategy.cs` declares `public static event … Published;` at L511. `IPublishingStrategy2.PublishingFinalized` (L527–531) dispatches it: `uow.Events.Dispatch(Published, this, new PublishEventArgs<IContent>(content, false, false, evtMsgs), "Published");`
- **Question.** "ContentService.Published is only a proxy. Which class actually declares the Published event and dispatches it in PublishingFinalized?"
- **Correct file.** `PublishingStrategy.cs` only. `ContentService.cs` is the trap, because it has the event name but not the dispatch.
- **Leakage.** Skipped, since this is a navigation question. The disambiguation grep in section 6 shows `PublishingFinalized(` in 4 files (ContentService.cs calls it; BasePublishingStrategy/IPublishingStrategy declare signatures). Only PublishingStrategy.cs dispatches `Published`.

### Worked example B: semantic (c29)

- **File.** `src/Umbraco.Web/Routing/PublishedContentRequestEngine.cs`.
- **Member.** `HandlePublishedContent()` (L456) handles not-found lookup, internal redirects and access checks inside one `do … while` loop, guarded by `const int maxLoop = 8;` (L462) and `} while (_pcr.HasPublishedContent == false && i++ < maxLoop);` (L499). If the guard trips, the content is nulled.
- **Draft 1.** "What stops a chain of internal forwards, login bounces and not-found fallbacks from looping forever while serving a front-end page?"

  Leakage output:
  ```
  ok   chain []
  LEAK internal ['FollowInternalRedirects', 'InternalRedirectId', 'InternalRedirectPreservesTemplate', ...]
  ok   forward []
  LEAK login ['GetRolesForLogin', 'LoginNodeId', 'loginPageId']
  ok   bounce []
  ok   not-found []
  ok   fallback []
  LEAK loop ['maxLoop']
  ok   front-end []
  LEAK page ['UmbracoPage', 'errorPageId', 'loginPageId', 'page']
  ```
- **Draft 2.** Swapped in "public page". `public` leaked, because it is a C# keyword and the checker counts it.
- **Final.** "What stops a chain of forwards, sign-in bounces and dead-end fallbacks from cycling endlessly while a visitor is being served?"
  ```
ok   chain []
ok   forward []
ok   sign-in []
ok   bounce []
ok   dead-end []
ok   fallback []
ok   visitor []
  ```
  `found_as_identifier: []`.

## 5. Quota and coverage

| Requirement | Target | Actual |
|---|---|---|
| total | 55 | **55** |
| navigation (lsp_trivial: true) | 20 | **20** |
| semantic (lsp_trivial: false) | 20 | **20** |
| cross-cutting (lsp_trivial: false) | 15 | **15** |
| long-doc (member doc ≥ 8 lines) | ≥ 8 | **8** (c18, c22, c23, c38, c39, c41, c44, c53) |
| deep-in-large-class (file > 1500 lines, decl after L100) | ≥ 8 | **8** (c01, c02, c09, c10, c11, c42, c48, c50) |
| short-member (≤ 6 body lines, unrelated neighbours) | ≥ 5 | **5** (c04, c37, c49, c51, c54; c37 and c54 weak, see 3c) |
| per subsystem | 4–10 | 6–8 (table below) |
| same file in two candidates of the same category | 0 | **0** |
| semantic/cross with non-empty leakage | 0 | **0** |

Each candidate carries exactly one `discriminator` label; the counts above use only that label. Several members qualify for two labels, and the extra qualification is recorded in `drafting_notes`.

| Subsystem | nav | sem | cross | total |
|---|---|---|---|---|
| Core services | 3 | 2 | 1 | 6 |
| Persistence | 2 | 2 | 2 | 6 |
| Cache | 2 | 3 | 2 | 7 |
| Events | 3 | 2 | 3 | 8 |
| Web routing | 2 | 2 | 2 | 6 |
| HttpModules and request pipeline | 4 | 2 | 1 | 7 |
| Backoffice API controllers | 2 | 3 | 2 | 7 |
| Legacy layer | 2 | 4 | 2 | 8 |

A note on subsystem boundaries: cross-cutting c42 (the "parent not published" rule in ContentService plus its wording in ContentController) is filed under Backoffice, not Core services. If you file it under Core services instead, both subsystems stay within 4–10.

### Candidate index

| id | cat | subsystem | discriminator | evidence (file:lines) | member |
|---|---|---|---|---|---|
| c01 | navigation | Core services | deep-in-large-class | `src/Umbraco.Core/Services/ContentService.cs:L1933-L1960` | ContentService.SendToPublication |
| c02 | semantic | Core services | deep-in-large-class | `src/Umbraco.Core/Services/ContentService.cs:L1962-L2003` | ContentService.Rollback |
| c03 | semantic | Core services | none | `src/Umbraco.Core/Services/MemberService.cs:L49-L74` | MemberService.GetDefaultMemberType |
| c04 | navigation | Core services | short-member | `src/Umbraco.Core/Services/MediaService.cs:L725-L737` | MediaService.GetMediaByPath |
| c05 | navigation | Core services | none | `src/Umbraco.Core/Services/MemberService.cs:L1144-L1172` | MemberService.DeleteRole |
| c06 | cross-cutting | Core services | none | `src/Umbraco.Core/Services/MemberService.cs:L90-L121` | MemberService.SavePassword |
| c07 | navigation | Persistence | none | `src/Umbraco.Core/Persistence/UnitOfWork/ScopeUnitOfWork.cs:L118-L161` | ScopeUnitOfWork.Commit(Action<UmbracoDatabase>) |
| c08 | semantic | Persistence | none | `src/Umbraco.Core/Persistence/Repositories/SimilarNodeName.cs:L93-L116` | SimilarNodeName.GetUniqueName |
| c09 | semantic | Persistence | deep-in-large-class | `src/Umbraco.Core/Persistence/PetaPoco.cs:L742-L789` | Database.BuildSqlDbSpecificPagingQuery |
| c10 | navigation | Persistence | deep-in-large-class | `src/Umbraco.Core/Persistence/PetaPoco.cs:L385-L402` | Database.GetSqlForTransactionLevel |
| c11 | cross-cutting | Persistence | deep-in-large-class | `src/Umbraco.Core/Persistence/PetaPoco.cs:L742-L789` | Database.BuildSqlDbSpecificPagingQuery |
| c12 | cross-cutting | Persistence | none | `src/Umbraco.Core/Persistence/UnitOfWork/ScopeUnitOfWork.cs:L245-L259` | ScopeUnitOfWork.DisposeResources |
| c13 | navigation | Cache | none | `src/Umbraco.Web/Cache/DistributedCache.cs:L29-L29` | DistributedCache.PageCacheRefresherId |
| c14 | semantic | Cache | none | `src/Umbraco.Web/Suspendable.cs:L43-L54` | Suspendable.PageCacheRefresher.ResumeDocumentCache |
| c15 | semantic | Cache | none | `src/Umbraco.Web/PublishedCache/XmlPublishedCache/XmlCacheFilePersister.cs:L11-L38` | XmlCacheFilePersister (WaitMilliseconds / MaxWaitMilliseconds) |
| c16 | semantic | Cache | none | `src/Umbraco.Web/PublishedCache/XmlPublishedCache/PublishedContentCache.cs:L69-L84` | PublishedContentCache.AddToCacheIfDeepestRoute |
| c17 | cross-cutting | Cache | none | `src/Umbraco.Web/Cache/CacheRefresherEventHandler.cs:L260-L280` | CacheRefresherEventHandler.ContentService_Published |
| c18 | navigation | Cache | long-doc | `src/Umbraco.Web/Cache/CacheRefresherEventHandler.cs:L347-L360` | CacheRefresherEventHandler.ContentService_Copied |
| c19 | cross-cutting | Cache | none | `src/Umbraco.Web/PublishedCache/XmlPublishedCache/PublishedContentCache.cs:L86-L109` | PublishedContentCache.GetRouteById |
| c20 | navigation | Events | none | `src/Umbraco.Core/Publishing/PublishingStrategy.cs:L527-L531` | PublishingStrategy.PublishingFinalized |
| c21 | navigation | Events | none | `src/Umbraco.Core/Events/DeleteEventArgs.cs:L7-L143` | DeleteEventArgs<TEntity> (class attributes) |
| c22 | navigation | Events | long-doc | `src/Umbraco.Core/Events/EventExtensions.cs:L18-L34` | EventExtensions.IsRaisedEventCancelled |
| c23 | semantic | Events | long-doc | `src/Umbraco.Core/Events/EventNameExtractor.cs:L26-L91` | EventNameExtractor.FindEvent |
| c24 | semantic | Events | none | `src/Umbraco.Core/Strategies/RelateOnTrashHandler.cs:L50-L94` | RelateOnTrashHandler.ContentService_Trashed |
| c25 | cross-cutting | Events | none | `src/Umbraco.Core/Strategies/RelateOnCopyHandler.cs:L16-L42` | RelateOnCopyHandler.ContentServiceCopied |
| c26 | cross-cutting | Events | none | `src/Umbraco.Core/Events/ScopeEventDispatcher.cs:L15-L32` | ScopeEventDispatcher.ScopeExitCompleted |
| c27 | cross-cutting | Events | none | `src/Umbraco.Core/Events/ScopeEventDispatcherBase.cs:L129-L243` | ScopeEventDispatcherBase.FilterSupersededAndUpdateToLatestEntity |
| c28 | navigation | Web routing | none | `src/Umbraco.Web/Routing/ContentFinderByNiceUrlAndTemplate.cs:L17-L77` | ContentFinderByNiceUrlAndTemplate.TryFindContent |
| c29 | semantic | Web routing | none | `src/Umbraco.Web/Routing/PublishedContentRequestEngine.cs:L449-L508` | PublishedContentRequestEngine.HandlePublishedContent |
| c30 | navigation | Web routing | none | `src/Umbraco.Web/Routing/PublishedContentRequestEngine.cs:L664-L757` | PublishedContentRequestEngine.FindTemplate |
| c31 | cross-cutting | Web routing | none | `src/Umbraco.Web/Routing/RedirectTrackingEventHandler.cs:L286-L296` | RedirectTrackingEventHandler.CreateRedirect |
| c32 | semantic | Web routing | none | `src/Umbraco.Web/Routing/RedirectTrackingEventHandler.cs:L33-L61` | RedirectTrackingEventHandler.ApplicationStarting |
| c33 | cross-cutting | Web routing | none | `src/Umbraco.Web/Routing/PublishedContentRequestEngine.cs:L586-L662` | PublishedContentRequestEngine.EnsurePublishedContentAccess |
| c34 | semantic | HttpModules and request pipeline | none | `src/Umbraco.Web/UmbracoModule.cs:L245-L273` | UmbracoModule.EnsureIsReady |
| c35 | navigation | HttpModules and request pipeline | none | `src/Umbraco.Web/UmbracoModule.cs:L433-L472` | UmbracoModule.DisposeHttpContextItems |
| c36 | navigation | HttpModules and request pipeline | none | `src/Umbraco.Web/umbraco.presentation/viewstateMoverModule.cs:L38-L45` | viewstateMoverModule.context_BeginRequest |
| c37 | navigation | HttpModules and request pipeline | short-member | `src/Umbraco.Web/umbraco.presentation/requestModule.cs:L449-L455` | requestModule.AddTask |
| c38 | navigation | HttpModules and request pipeline | long-doc | `src/Umbraco.Web/UmbracoContext.cs:L91-L142` | UmbracoContext.EnsureContext (full overload) |
| c39 | semantic | HttpModules and request pipeline | long-doc | `src/Umbraco.Core/UriExtensions.cs:L15-L102` | UriExtensions.IsBackOfficeRequest |
| c40 | cross-cutting | HttpModules and request pipeline | none | `src/Umbraco.Web/UmbracoModule.cs:L72-L147` | UmbracoModule.ProcessRequest |
| c41 | semantic | Backoffice API controllers | long-doc | `src/Umbraco.Web/Editors/ContentController.cs:L800-L844` | ContentController.DeleteById |
| c42 | cross-cutting | Backoffice API controllers | deep-in-large-class | `src/Umbraco.Core/Services/ContentService.cs:L2717-L2748` | ContentService.CheckAndLogIsPublishable |
| c43 | semantic | Backoffice API controllers | none | `src/Umbraco.Web/Editors/MediaController.cs:L689-L856` | MediaController.PostAddFile |
| c44 | navigation | Backoffice API controllers | long-doc | `src/Umbraco.Web/Editors/MediaController.cs:L858-L919` | MediaController.GetParentIdAsInt |
| c45 | semantic | Backoffice API controllers | none | `src/Umbraco.Web/Editors/ContentPostValidateAttribute.cs:L68-L160` | ContentPostValidateAttribute.OnActionExecuting |
| c46 | navigation | Backoffice API controllers | none | `src/Umbraco.Web/Editors/ContentController.cs:L863-L899` | ContentController.PostSort |
| c47 | cross-cutting | Backoffice API controllers | none | `src/Umbraco.Web/Editors/PasswordChanger.cs:L35-L147` | PasswordChanger.ChangePasswordWithIdentityAsync |
| c48 | semantic | Legacy layer | deep-in-large-class | `src/Umbraco.Web/umbraco.presentation/macro.cs:L723-L753` | macro.MacroNeedsToBeClearedFromCache |
| c49 | navigation | Legacy layer | short-member | `src/Umbraco.Web/umbraco.presentation/macro.cs:L1785-L1789` | macro.ShowNoMacroContent |
| c50 | semantic | Legacy layer | deep-in-large-class | `src/Umbraco.Web/umbraco.presentation/library.cs:L1533-L1556` | library.GetXmlDocumentByUrl(string, int) |
| c51 | navigation | Legacy layer | short-member | `src/Umbraco.Web/umbraco.presentation/library.cs:L1683-L1693` | library.ChangeContentType |
| c52 | semantic | Legacy layer | none | `src/Umbraco.Web/umbraco.presentation/umbraco/dashboard/FeedProxy.aspx.cs:L18-L60` | FeedProxy.Page_Load |
| c53 | semantic | Legacy layer | long-doc | `src/umbraco.cms/businesslogic/web/Document.cs:L784-L822` | Document.PublishWithResult |
| c54 | cross-cutting | Legacy layer | short-member | `src/Umbraco.Web/umbraco.presentation/library.cs:L201-L207` | library.RefreshContent |
| c55 | cross-cutting | Legacy layer | none | `src/Umbraco.Web/umbraco.presentation/macro.cs:L1690-L1783` | macro.MacroContentByHttp |

## 6. Self-verification output (Step 4)

All output below is verbatim. The scripts are `verify.py` and the shell loop shown, run against the written `data/golden.candidates.json`.

### 6a. `test -f` on every recorded path

Every path in `correct_files`, `evidence.file` and `evidence_additional[].file` (50 distinct):

```
cd corpus/Umbraco-CMS && while read p; do test -f "$p" && echo "ok   $p" || echo "MISSING $p"; done < paths.txt
```
```
ok   src/Umbraco.Core/Events/CopyEventArgs.cs
ok   src/Umbraco.Core/Events/DeleteEventArgs.cs
ok   src/Umbraco.Core/Events/EventExtensions.cs
ok   src/Umbraco.Core/Events/EventNameExtractor.cs
ok   src/Umbraco.Core/Events/ScopeEventDispatcher.cs
ok   src/Umbraco.Core/Events/ScopeEventDispatcherBase.cs
ok   src/Umbraco.Core/Events/SupersedeEventAttribute.cs
ok   src/Umbraco.Core/Persistence/PetaPoco.cs
ok   src/Umbraco.Core/Persistence/Repositories/SimilarNodeName.cs
ok   src/Umbraco.Core/Persistence/UmbracoDatabase.cs
ok   src/Umbraco.Core/Persistence/UnitOfWork/ScopeUnitOfWork.cs
ok   src/Umbraco.Core/Publishing/PublishingStrategy.cs
ok   src/Umbraco.Core/Scoping/Scope.cs
ok   src/Umbraco.Core/Security/MembershipProviderExtensions.cs
ok   src/Umbraco.Core/Services/ContentService.cs
ok   src/Umbraco.Core/Services/MediaService.cs
ok   src/Umbraco.Core/Services/MemberService.cs
ok   src/Umbraco.Core/Services/PublicAccessServiceExtensions.cs
ok   src/Umbraco.Core/Strategies/RelateOnCopyHandler.cs
ok   src/Umbraco.Core/Strategies/RelateOnTrashHandler.cs
ok   src/Umbraco.Core/UriExtensions.cs
ok   src/Umbraco.Web/Cache/CacheRefresherEventHandler.cs
ok   src/Umbraco.Web/Cache/DistributedCache.cs
ok   src/Umbraco.Web/Cache/DistributedCacheExtensions.cs
ok   src/Umbraco.Web/Cache/PageCacheRefresher.cs
ok   src/Umbraco.Web/Editors/ContentController.cs
ok   src/Umbraco.Web/Editors/ContentPostValidateAttribute.cs
ok   src/Umbraco.Web/Editors/MediaController.cs
ok   src/Umbraco.Web/Editors/PasswordChanger.cs
ok   src/Umbraco.Web/Editors/UsersController.cs
ok   src/Umbraco.Web/PropertyEditors/FileUploadPropertyEditor.cs
ok   src/Umbraco.Web/PublishedCache/XmlPublishedCache/PublishedContentCache.cs
ok   src/Umbraco.Web/PublishedCache/XmlPublishedCache/RoutesCache.cs
ok   src/Umbraco.Web/PublishedCache/XmlPublishedCache/XmlCacheFilePersister.cs
ok   src/Umbraco.Web/Routing/ContentFinderByNiceUrlAndTemplate.cs
ok   src/Umbraco.Web/Routing/ContentFinderByRedirectUrl.cs
ok   src/Umbraco.Web/Routing/PublishedContentRequest.cs
ok   src/Umbraco.Web/Routing/PublishedContentRequestEngine.cs
ok   src/Umbraco.Web/Routing/RedirectTrackingEventHandler.cs
ok   src/Umbraco.Web/Suspendable.cs
ok   src/Umbraco.Web/UmbracoContext.cs
ok   src/Umbraco.Web/UmbracoModule.cs
ok   src/Umbraco.Web/umbraco.presentation/library.cs
ok   src/Umbraco.Web/umbraco.presentation/macro.cs
ok   src/Umbraco.Web/umbraco.presentation/requestModule.cs
ok   src/Umbraco.Web/umbraco.presentation/umbraco/dashboard/FeedProxy.aspx.cs
ok   src/Umbraco.Web/umbraco.presentation/umbraco/dialogs/republish.aspx.cs
ok   src/Umbraco.Web/umbraco.presentation/umbraco/macroResultWrapper.aspx.cs
ok   src/Umbraco.Web/umbraco.presentation/viewstateMoverModule.cs
ok   src/umbraco.cms/businesslogic/web/Document.cs
```

`verify.py` repeats the check with `subprocess.run(['test','-f',p])` and also confirms each path is in scope (under `src/`, ends in `.cs`, contains none of the excluded segments):

```
OK   in-scope  src/Umbraco.Core/Events/CopyEventArgs.cs
OK   in-scope  src/Umbraco.Core/Events/DeleteEventArgs.cs
OK   in-scope  src/Umbraco.Core/Events/EventExtensions.cs
OK   in-scope  src/Umbraco.Core/Events/EventNameExtractor.cs
OK   in-scope  src/Umbraco.Core/Events/ScopeEventDispatcher.cs
OK   in-scope  src/Umbraco.Core/Events/ScopeEventDispatcherBase.cs
OK   in-scope  src/Umbraco.Core/Events/SupersedeEventAttribute.cs
OK   in-scope  src/Umbraco.Core/Persistence/PetaPoco.cs
OK   in-scope  src/Umbraco.Core/Persistence/Repositories/SimilarNodeName.cs
OK   in-scope  src/Umbraco.Core/Persistence/UmbracoDatabase.cs
OK   in-scope  src/Umbraco.Core/Persistence/UnitOfWork/ScopeUnitOfWork.cs
OK   in-scope  src/Umbraco.Core/Publishing/PublishingStrategy.cs
OK   in-scope  src/Umbraco.Core/Scoping/Scope.cs
OK   in-scope  src/Umbraco.Core/Security/MembershipProviderExtensions.cs
OK   in-scope  src/Umbraco.Core/Services/ContentService.cs
OK   in-scope  src/Umbraco.Core/Services/MediaService.cs
OK   in-scope  src/Umbraco.Core/Services/MemberService.cs
OK   in-scope  src/Umbraco.Core/Services/PublicAccessServiceExtensions.cs
OK   in-scope  src/Umbraco.Core/Strategies/RelateOnCopyHandler.cs
OK   in-scope  src/Umbraco.Core/Strategies/RelateOnTrashHandler.cs
OK   in-scope  src/Umbraco.Core/UriExtensions.cs
OK   in-scope  src/Umbraco.Web/Cache/CacheRefresherEventHandler.cs
OK   in-scope  src/Umbraco.Web/Cache/DistributedCache.cs
OK   in-scope  src/Umbraco.Web/Cache/DistributedCacheExtensions.cs
OK   in-scope  src/Umbraco.Web/Cache/PageCacheRefresher.cs
OK   in-scope  src/Umbraco.Web/Editors/ContentController.cs
OK   in-scope  src/Umbraco.Web/Editors/ContentPostValidateAttribute.cs
OK   in-scope  src/Umbraco.Web/Editors/MediaController.cs
OK   in-scope  src/Umbraco.Web/Editors/PasswordChanger.cs
OK   in-scope  src/Umbraco.Web/Editors/UsersController.cs
OK   in-scope  src/Umbraco.Web/PropertyEditors/FileUploadPropertyEditor.cs
OK   in-scope  src/Umbraco.Web/PublishedCache/XmlPublishedCache/PublishedContentCache.cs
OK   in-scope  src/Umbraco.Web/PublishedCache/XmlPublishedCache/RoutesCache.cs
OK   in-scope  src/Umbraco.Web/PublishedCache/XmlPublishedCache/XmlCacheFilePersister.cs
OK   in-scope  src/Umbraco.Web/Routing/ContentFinderByNiceUrlAndTemplate.cs
OK   in-scope  src/Umbraco.Web/Routing/ContentFinderByRedirectUrl.cs
OK   in-scope  src/Umbraco.Web/Routing/PublishedContentRequest.cs
OK   in-scope  src/Umbraco.Web/Routing/PublishedContentRequestEngine.cs
OK   in-scope  src/Umbraco.Web/Routing/RedirectTrackingEventHandler.cs
OK   in-scope  src/Umbraco.Web/Suspendable.cs
OK   in-scope  src/Umbraco.Web/UmbracoContext.cs
OK   in-scope  src/Umbraco.Web/UmbracoModule.cs
OK   in-scope  src/Umbraco.Web/umbraco.presentation/library.cs
OK   in-scope  src/Umbraco.Web/umbraco.presentation/macro.cs
OK   in-scope  src/Umbraco.Web/umbraco.presentation/requestModule.cs
OK   in-scope  src/Umbraco.Web/umbraco.presentation/umbraco/dashboard/FeedProxy.aspx.cs
OK   in-scope  src/Umbraco.Web/umbraco.presentation/umbraco/dialogs/republish.aspx.cs
OK   in-scope  src/Umbraco.Web/umbraco.presentation/umbraco/macroResultWrapper.aspx.cs
OK   in-scope  src/Umbraco.Web/umbraco.presentation/viewstateMoverModule.cs
OK   in-scope  src/umbraco.cms/businesslogic/web/Document.cs
distinct paths: 50, failures: 0
```

### 6b. `grep -F` for every evidence snippet

`grep -nF -- "<snippet>" <file>` for the primary evidence and every additional evidence entry. The script prints the hit line numbers and fails if no hit falls inside `evidence.lines`. A tag like `c06+1` means c06's first additional evidence entry.

```
OK   c01    ContentService.cs:1953  range L1933-L1960
OK   c02    ContentService.cs:1967  range L1962-L2003
OK   c03    MemberService.cs:68  range L49-L74
OK   c04    MediaService.cs:735  range L725-L737
OK   c05    MemberService.cs:1153  range L1144-L1172
OK   c06    MemberService.cs:102  range L90-L121
OK   c06+1  MembershipProviderExtensions.cs:160  range L153-L161
OK   c07    ScopeUnitOfWork.cs:141,174  range L118-L161
OK   c08    SimilarNodeName.cs:115  range L93-L116
OK   c09    PetaPoco.cs:781  range L742-L789
OK   c10    PetaPoco.cs:392  range L385-L402
OK   c11    PetaPoco.cs:781  range L742-L789
OK   c11+1  UmbracoDatabase.cs:267  range L242-L276
OK   c12    ScopeUnitOfWork.cs:256  range L245-L259
OK   c12+1  Scope.cs:409  range L392-L428
OK   c13    DistributedCache.cs:29  range L29-L29
OK   c14    Suspendable.cs:53  range L43-L54
OK   c15    XmlCacheFilePersister.cs:38  range L11-L38
OK   c16    PublishedContentCache.cs:76  range L69-L84
OK   c17    CacheRefresherEventHandler.cs:277  range L260-L280
OK   c17+1  DistributedCacheExtensions.cs:160  range L158-L161
OK   c17+2  PageCacheRefresher.cs:72,101  range L96-L107
OK   c18    CacheRefresherEventHandler.cs:359  range L347-L360
OK   c19    PublishedContentCache.cs:102  range L86-L109
OK   c19+1  RoutesCache.cs:91  range L81-L92
OK   c20    PublishingStrategy.cs:530  range L527-L531
OK   c21    DeleteEventArgs.cs:7  range L7-L143
OK   c22    EventExtensions.cs:28  range L18-L34
OK   c23    EventNameExtractor.cs:35,102  range L26-L91
OK   c24    RelateOnTrashHandler.cs:80,126  range L50-L94
OK   c25    RelateOnCopyHandler.cs:34  range L16-L42
OK   c25+1  CopyEventArgs.cs:48  range L48-L48
OK   c26    ScopeEventDispatcher.cs:30  range L15-L32
OK   c26+1  FileUploadPropertyEditor.cs:184  range L171-L198
OK   c27    ScopeEventDispatcherBase.cs:148  range L129-L243
OK   c27+1  DeleteEventArgs.cs:7  range L7-L143
OK   c27+2  SupersedeEventAttribute.cs:11  range L5-L19
OK   c28    ContentFinderByNiceUrlAndTemplate.cs:36  range L17-L77
OK   c29    PublishedContentRequestEngine.cs:462  range L449-L508
OK   c30    PublishedContentRequestEngine.cs:725  range L664-L757
OK   c31    RedirectTrackingEventHandler.cs:295  range L286-L296
OK   c31+1  ContentFinderByRedirectUrl.cs:61  range L17-L63
OK   c32    RedirectTrackingEventHandler.cs:45  range L33-L61
OK   c33    PublishedContentRequestEngine.cs:619,643  range L586-L662
OK   c33+1  PublicAccessServiceExtensions.cs:44  range L44-L54
OK   c34    UmbracoModule.cs:257  range L245-L273
OK   c35    UmbracoModule.cs:447  range L433-L472
OK   c36    viewstateMoverModule.cs:43  range L38-L45
OK   c37    requestModule.cs:454  range L449-L455
OK   c38    UmbracoContext.cs:134  range L91-L142
OK   c39    UriExtensions.cs:36  range L15-L102
OK   c40    UmbracoModule.cs:136  range L72-L147
OK   c40+1  PublishedContentRequest.cs:93  range L88-L94
OK   c41    ContentController.cs:801  range L800-L844
OK   c42    ContentService.cs:2726  range L2717-L2748
OK   c42+1  ContentController.cs:1072  range L1056-L1109
OK   c43    MediaController.cs:786  range L689-L856
OK   c44    MediaController.cs:863  range L858-L919
OK   c45    ContentPostValidateAttribute.cs:127  range L68-L160
OK   c46    ContentController.cs:868  range L863-L899
OK   c47    PasswordChanger.cs:82  range L35-L147
OK   c47+1  UsersController.cs:586  range L586-L586
OK   c48    macro.cs:744  range L723-L753
OK   c49    macro.cs:1788  range L1785-L1789
OK   c50    library.cs:1551  range L1533-L1556
OK   c51    library.cs:1691  range L1683-L1693
OK   c52    FeedProxy.aspx.cs:33  range L18-L60
OK   c53    Document.cs:811  range L784-L822
OK   c54    library.cs:206,216,228,240  range L201-L207
OK   c54+1  republish.aspx.cs:53  range L25-L57
OK   c55    macro.cs:1712  range L1690-L1783
OK   c55+1  macroResultWrapper.aspx.cs:47  range L23-L49
snippet failures: 0
```

### 6c. Quotas, coverage and file sharing

```
## 3. Quotas and coverage
total 55
category {'navigation': 20, 'semantic': 20, 'cross-cutting': 15}
lsp_trivial by category {('navigation', True): 20, ('semantic', False): 20, ('cross-cutting', False): 15}
discriminator {'deep-in-large-class': 8, 'none': 34, 'short-member': 5, 'long-doc': 8}
subsystem {'Core services': 6, 'Persistence': 6, 'Cache': 7, 'Events': 8, 'Web routing': 6, 'HttpModules and request pipeline': 7, 'Backoffice API controllers': 7, 'Legacy layer': 8}
confidence {'high': 45, 'medium': 10}
  Core services                        nav=3 sem=2 cross=1 total=6
  Persistence                          nav=2 sem=2 cross=2 total=6
  Cache                                nav=2 sem=3 cross=2 total=7
  Events                               nav=3 sem=2 cross=3 total=8
  Web routing                          nav=2 sem=2 cross=2 total=6
  HttpModules and request pipeline     nav=4 sem=2 cross=1 total=7
  Backoffice API controllers           nav=2 sem=3 cross=2 total=7
  Legacy layer                         nav=2 sem=4 cross=2 total=8
leakage non-empty: []
nav with >1 correct file: []
semantic with >1 correct file: []
cross with <2 or >3 files: []

## 4. Shared correct files (allowed only across different categories)
ok        src/Umbraco.Core/Events/DeleteEventArgs.cs: [('c21', 'navigation'), ('c27', 'cross-cutting')]
ok        src/Umbraco.Core/Persistence/PetaPoco.cs: [('c09', 'semantic'), ('c10', 'navigation'), ('c11', 'cross-cutting')]
ok        src/Umbraco.Core/Persistence/UnitOfWork/ScopeUnitOfWork.cs: [('c07', 'navigation'), ('c12', 'cross-cutting')]
ok        src/Umbraco.Core/Services/ContentService.cs: [('c01', 'navigation'), ('c02', 'semantic'), ('c42', 'cross-cutting')]
ok        src/Umbraco.Core/Services/MemberService.cs: [('c03', 'semantic'), ('c05', 'navigation'), ('c06', 'cross-cutting')]
ok        src/Umbraco.Web/Cache/CacheRefresherEventHandler.cs: [('c17', 'cross-cutting'), ('c18', 'navigation')]
ok        src/Umbraco.Web/Editors/ContentController.cs: [('c41', 'semantic'), ('c42', 'cross-cutting'), ('c46', 'navigation')]
ok        src/Umbraco.Web/Editors/MediaController.cs: [('c43', 'semantic'), ('c44', 'navigation')]
ok        src/Umbraco.Web/PublishedCache/XmlPublishedCache/PublishedContentCache.cs: [('c16', 'semantic'), ('c19', 'cross-cutting')]
ok        src/Umbraco.Web/Routing/PublishedContentRequestEngine.cs: [('c29', 'semantic'), ('c30', 'navigation'), ('c33', 'cross-cutting')]
ok        src/Umbraco.Web/Routing/RedirectTrackingEventHandler.cs: [('c31', 'cross-cutting'), ('c32', 'semantic')]
ok        src/Umbraco.Web/UmbracoModule.cs: [('c34', 'semantic'), ('c35', 'navigation'), ('c40', 'cross-cutting')]
ok        src/Umbraco.Web/umbraco.presentation/library.cs: [('c50', 'semantic'), ('c51', 'navigation'), ('c54', 'cross-cutting')]
ok        src/Umbraco.Web/umbraco.presentation/macro.cs: [('c48', 'semantic'), ('c49', 'navigation'), ('c55', 'cross-cutting')]
violations: 0
```

### 6d. Navigation disambiguation

This check is not required by the brief, but it helps review. For each navigation identifier, it lists every indexed file that contains it (`grep -lF`). Where the count is above 1, the question names the owning class, and the other files are listed in that candidate's `drafting_notes`.

```
4  SendToPublication( :: Umbraco.Core/Services/ContentService.cs Umbraco.Core/Services/IContentService.cs Umbraco.Web/Editors/ContentController.cs umbraco.cms/businesslogic/web/Document.cs 
5  GetMediaByPath( :: Umbraco.Core/Services/IMediaService.cs Umbraco.Core/Services/MediaService.cs umbraco.editorControls/tinyMCE3/webcontrol/TinyMCEWebControl.cs Umbraco.Core/Persistence/Repositories/MediaRepository.cs Umbraco.Core/Persistence/Repositories/Interfaces/IMediaRepository.cs 
6  DeleteRole( :: umbraco.providers/UsersRoleProvider.cs Umbraco.Core/Services/MemberService.cs Umbraco.Core/Services/IMembershipRoleService.cs umbraco.providers/members/UmbracoRoleProvider.cs Umbraco.Web/Security/Providers/MembersRoleProvider.cs Umbraco.Web/umbraco.presentation/umbraco/create/MemberGroupTasks.cs 
3  void Commit( :: Umbraco.Core/Persistence/LockedRepository.cs Umbraco.Core/Persistence/UnitOfWork/ScopeUnitOfWork.cs Umbraco.Core/Persistence/UnitOfWork/IUnitOfWork.cs 
1  GetSqlForTransactionLevel :: Umbraco.Core/Persistence/PetaPoco.cs 
1  PageCacheRefresherId = :: Umbraco.Web/Cache/DistributedCache.cs 
1  ContentService_Copied :: Umbraco.Web/Cache/CacheRefresherEventHandler.cs 
4  PublishingFinalized( :: Umbraco.Core/Services/ContentService.cs Umbraco.Core/Publishing/PublishingStrategy.cs Umbraco.Core/Publishing/BasePublishingStrategy.cs Umbraco.Core/Publishing/IPublishingStrategy.cs 
1  SupersedeEvent(typeof(SaveEventArgs :: Umbraco.Core/Events/DeleteEventArgs.cs 
1  IsRaisedEventCancelled< :: Umbraco.Core/Events/EventExtensions.cs 
1  class ContentFinderByNiceUrlAndTemplate :: Umbraco.Web/Routing/ContentFinderByNiceUrlAndTemplate.cs 
1  void FindTemplate( :: Umbraco.Web/Routing/PublishedContentRequestEngine.cs 
1  DisposeHttpContextItems( :: Umbraco.Web/UmbracoModule.cs 
1  class viewstateMoverModule :: Umbraco.Web/umbraco.presentation/viewstateMoverModule.cs 
1  void AddTask( :: Umbraco.Web/umbraco.presentation/requestModule.cs 
1  UmbracoContext EnsureContext( :: Umbraco.Web/UmbracoContext.cs 
1  GetParentIdAsInt( :: Umbraco.Web/Editors/MediaController.cs 
2  PostSort( :: Umbraco.Web/Editors/ContentController.cs Umbraco.Web/Editors/MediaController.cs 
1  ShowNoMacroContent( :: Umbraco.Web/umbraco.presentation/macro.cs 
5  void ChangeContentType( :: Umbraco.Web/umbraco.presentation/library.cs Umbraco.Core/Models/Media.cs Umbraco.Core/Models/IMedia.cs Umbraco.Core/Models/Content.cs Umbraco.Core/Models/IContent.cs 
```

## 7. Medium-confidence candidates

| id | why medium |
|---|---|
| c03 | Second plausible file: MembersMembershipProvider.cs reads a web.config default first and only then calls this method. Also the method's doc comment ('writer', non-admin) contradicts the code ('Member', else first). |
| c12 | Leakage rule forced generic wording ('job', 'pending edits') because call/commit/work/operation/transaction/dispose are all identifiers here. It may read vague. ScopeProvider.cs is arguably relevant. |
| c14 | No indexed caller of SuspendDocumentCache (presumably external tooling). PageCacheRefresher.cs checks the flags and could be a second correct file. |
| c25 | ContentService.Copy builds the args and is on the path, but is excluded by the one-cross-cutting-per-file rule. A retriever returning ContentService.cs would be scored a miss. |
| c26 | ImageCropperPropertyEditor.cs registers identical Deleted handlers and is equally valid as the 'who adds the files' half. |
| c33 | PublicAccessService.cs (IsProtected) is also on the path. The frozen/unapproved logic is only in the engine. |
| c37 | Short-member adjacency is weak (same #region). requestModule may not be registered by default in 7.15. |
| c40 | PublishedContentRequestEngine.cs does the real work but is omitted under the collision rule (it is c33's cross-cutting file). |
| c47 | CurrentUserController.cs also calls PasswordChanger (the self-service path) and could be a correct second file instead of UsersController.cs. |
| c54 | Short-member adjacency is weak: RefreshContent's neighbours are other publish helpers. |

## 8. Known gaps: things the questions touch that are not indexed

The index is `.cs` only, so config, views, markup, language files and JS are invisible to retrieval. Where a question's full answer depends on one of these, it is noted here and in the candidate's `drafting_notes`. In every case the `.cs` correct file alone is enough to answer what the question asks.

| id | non-indexed artefact involved |
|---|---|
| c03 | `web.config` membership provider `defaultMemberTypeAlias` (read by MembersMembershipProvider before falling back to the service) |
| c06 | `web.config` `<membership>`: which provider is active |
| c17 | `umbracoSettings.config` / load-balancing (server registrar) configuration |
| c32 | `umbracoSettings.config` `webRouting/@disableRedirectUrlTracking` |
| c34 | `~/config/splashes/booting.aspx` (splash page) and `umbracoSettings.config` `EnableSplashWhileLoading` |
| c36 | `umbracoSettings.config` `ViewStateMoverModule/@enable` |
| c37 | `web.config` module registration (whether the legacy `requestModule` is active at all), `umbracoSettings.config` logging settings |
| c39 | `umbracoPath` appSetting in `web.config` (the `/umbraco` segment) |
| c42 | `Umbraco/config/lang/*.xml`: the actual warning text (`publish/contentPublishedFailedByParent`) |
| c43 | `umbracoSettings.config` allowed/disallowed upload extensions and image file types |
| c52 | `config/feedProxy.config` (host allow-list) and `FeedProxy.aspx` markup |
| c54 | `dialogs/republish.aspx` markup |
| c55 | `macroResultWrapper.aspx` markup; the TinyMCE legacy plugin JS that triggers the preview |

No candidate involves a `.cshtml` file.

## 9. What I did NOT do

- **No retrieval runs.** I did not run the RagAblation app (`index`, `eval` or `diff`) and did not build it.
- **No database.** I did not connect to Postgres.
- **No results.** I did not read `data/results/`. A plain `ls` of `data/` at the start showed that the directory exists. I did not list or open its contents.
- **No chunker use.** I did not use or read `src/RagAblation.Chunking`. All structural facts come from grep/awk/wc/Python over the raw corpus files.
- **No golden.json.** I did not create, read or modify `data/golden.json`. It does not exist, and I did not create it.
- **Files written.** Only `data/golden.candidates.json` and this document were written in the repo. Helper scripts (`spec.py`, `patch.py`, `build.py`, `verify.py`, `nountest.py`) and intermediate outputs live in my session scratch directory, not the repo.
