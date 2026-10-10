# Golden set review: candidates → proposed final 40

Companion to `data/golden.proposed.json`. Inputs were `data/golden.candidates.json` (55 candidates), `docs/golden-set-method.md` and the *Golden set format* section of `spec-chunking-ablation.md`. Nothing is frozen: a human verifies section 8 and then writes `data/golden.json` by hand.

**Step 0, loader check.** `src/RagAblation/Golden/GoldenLoader.cs` deserialises with `System.Text.Json` and a `JsonSerializerOptions` that only sets `PropertyNameCaseInsensitive`. `UnmappedMemberHandling` is left at its default (`Skip`) and the DTO carries no `[JsonUnmappedMemberHandling]` attribute, so unknown fields such as `evidence` and `subsystem` are ignored. No code change was needed and none was made; `src/` and `tests/` are untouched.

**Tooling note.** macOS has no `shuf`. I installed GNU coreutils via Homebrew (`brew install coreutils`, provides `/opt/homebrew/opt/coreutils/libexec/gnubin/shuf`, coreutils 9.12) so the Step 3 command could be run exactly as specified. That is the only change made outside the repo.

All line numbers are 1-based in `corpus/Umbraco-CMS` (tag `release-7.15.11`). Corpus-wide searches were restricted to the 3,292 indexed `.cs` files (same filter as the spec).


## 1. Answer-key changes (Step 1: the medium-confidence ten)

The once-per-category file rule now applies to the primary file only. For each medium-confidence candidate I re-opened the evidence and searched the indexed corpus for other files that answer the question as well as the primary. Every added file has its own evidence entry in the JSON (file, lines, verbatim snippet; all confirmed by `grep -nF` in section 7).

| id | q-id | files before | files after | what changed and why |
|---|---|---|---|---|
| c03 | — | MemberService.cs | MemberService.cs, **+ Security/Providers/MembersMembershipProvider.cs** (DefaultMemberTypeAlias, L99–L120) | Added. The provider property is the entry point a developer actually hits: it returns the `defaultMemberTypeAlias` from web.config if set (Initialize, L72–L80), otherwise calls `MemberService.GetDefaultMemberType()` (L109). Both files answer "which type is used". Cut later in Step 5 (see §5), so this change is recorded but not in the final 40. |
| c12 | q08 | ScopeUnitOfWork.cs, Scoping/Scope.cs | unchanged | `ScopeProvider.cs` only manages the ambient-scope stack (AmbientScope, L320–L354). The commit/abort decision is `Scope.DisposeLastScope` (L392–L428) and nested-child handling is `Scope.Dispose` → `parent.ChildCompleted` (L358–L390). The two listed files already cover the question; nothing equal was found. |
| c14 | — | Suspendable.cs | unchanged | `PageCacheRefresher.cs` only reads `Suspendable.PageCacheRefresher.CanUpdateDocumentCache` at four call sites (L69, L86, L98, L111). The pause / resume / full-reload mechanism is entirely in Suspendable.cs L37–L54. Not equal. Cut later in Step 5. |
| c25 | — | RelateOnCopyHandler.cs, Events/CopyEventArgs.cs | RelateOnCopyHandler.cs, CopyEventArgs.cs, **+ Services/ContentService.cs** (Copy, L1853–L1930) | Added. `ContentService.Copy` is where the flag enters the event args: `new CopyEventArgs<IContent>(content, copy, true, parentId, relateToOriginal)` at L1868. It answers "how does the choice travel there" at least as well as CopyEventArgs. `ContentController.PostCopy` (L928) and `Models/ContentEditing/MoveOrCopy.cs` only forward the DTO flag and were not added. Cut later in Step 5. |
| c26 | q18 | PropertyEditors/FileUploadPropertyEditor.cs, Events/ScopeEventDispatcher.cs | FileUploadPropertyEditor.cs, ScopeEventDispatcher.cs, **+ PropertyEditors/ImageCropperPropertyEditor.cs** (OnApplicationStarted, L268–L296) | Added. ImageCropperPropertyEditor registers the identical `MediaService.Deleted` / `ContentService.Deleted` / `MemberService.Deleted` handlers that append to `MediaFilesToDelete` (L282–L296); it is an equally valid "who adds the files" half. `ScopeEventDispatcherBase.ScopeExit` (L334–L340) holds the literal "ignored unless completed" rule but a fourth file would exceed the 2–3 limit, and `ScopeEventDispatcher.ScopeExitCompleted` already implies it. |
| c33 | — | Routing/PublishedContentRequestEngine.cs, Services/PublicAccessServiceExtensions.cs | unchanged | `PublicAccessService.IsProtected` (L94–L111) only looks up whether an entry exists for the path. The inactive-member redirect is in the engine (L627–L648) and the role rule in `PublicAccessServiceExtensions.HasAccess` (L44–L54). Not equal. Cut later in Step 5. |
| c37 | — | requestModule.cs | replaced | Weak short-member fit; replaced in Step 4 (§4). |
| c40 | q27 | UmbracoModule.cs, Routing/PublishedContentRequest.cs | UmbracoModule.cs, PublishedContentRequest.cs, **+ Routing/PublishedContentRequestEngine.cs** (PrepareRequest, L104–L160) | Added. `PublishedContentRequest.Prepare` is a one-liner that calls `_engine.PrepareRequest()`; the engine method runs `FindDomain()` (L121) and `FindPublishedContentAndTemplate()` (L139). It answers "where are the page and its template worked out" better than the wrapper does. The old omission was only the collision rule. |
| c47 | — | Editors/PasswordChanger.cs, Editors/UsersController.cs | PasswordChanger.cs, UsersController.cs, **+ Editors/CurrentUserController.cs** (PostChangePassword, L138–L166) | Added. `CurrentUserController.PostChangePassword` passes `Security.CurrentUser` as both current and saving user (L141), which is exactly the self-reset case the question asks about; it is a caller symmetric to `UsersController.PostSaveUser` (L586). Cut later in Step 5. |
| c54 | — | dialogs/republish.aspx.cs, library.cs | replaced | Weak short-member fit; replaced in Step 4 (§4). |

No candidate was left ambiguous after this pass, so nothing was marked `drop-candidate`. Confidence on c12, c14, c33 was raised to high on the strength of the re-check; the others became high by gaining the missing file.


## 2. Leakage policy change and rephrasing (Step 2)

**Old policy.** Every content noun was checked as a camelCase prefix against all identifiers, and C# keywords counted as identifiers. That blocked `page`, `public`, `loop`, `cache`, `provider`, `reset` … and produced the odd substitutes ("webpage", "machine in a farm", "job", "rich-text box").

**New policy (applied to every semantic and cross-cutting question).**

- Identify the 1–2 **crux nouns**: the words that name the mechanism the question is about. A crux noun must not appear as any part of an identifier in any correct file. Checked by `grep -i -c <noun> <file>` (recorded) and, to separate identifiers from comments and string literals, by tokenising the file with comments and string/char literals stripped and testing the noun as a case-insensitive substring of each identifier. Both results are in section 7.
- **Generic words are allowed** even when they appear inside identifiers: page, public, content, item, node, id, name, type, get, set, list, request, user, member, media, document, property, value, cache, save, publish. Exception: a generic word that is also the crux is treated as crux.
- Words that are neither crux nor on the generic list (e.g. *template*, *redirect*, *SQL Server*, *XSLT*) are not checked. Where such a word is lexically strong I say so in the table.

Navigation questions are unchanged (they name identifiers on purpose). The before/after table below covers all 25 semantic and cross-cutting questions in the final set. Changes that were only `webpage → page` or similar are still listed so the reviewer can see every edit.

| q-id | cand | before | after | crux nouns | result |
|---|---|---|---|---|---|
| q02 | c02 | When someone restores an earlier draft from a webpage's history, is the webpage still live afterwards, and which parts of it actually revert? | When someone restores an earlier version of a page from its history, is the page still published afterwards, and which parts of it actually revert? | restore, revert | clean |
| q04 | c06 | Why does setting a front-end visitor's new secret from code throw when the site is wired to a third-party credential store, and how does the code tell the built-in store apart? | Why does setting a member's password from code throw when the site uses a custom login system for members, and how does the code tell Umbraco's built-in one apart? | custom, builtin, built-in | clean |
| q05 | c08 | When two siblings in the same folder end up with an identical title, how is the counter in brackets chosen, and why might you get 'About (3)' rather than 'About (2)'? | When two siblings in the same folder end up with the same name, how is the counter in brackets chosen, and why might you get 'About (3)' rather than 'About (2)'? | counter, bracket | clean |
| q06 | c09 | How does the hand-rolled ORM fetch only rows 41 to 60 of a listing, and why does the generated wording differ between older Microsoft engines, the embedded edition and everything else? | How does the vendored micro-ORM fetch only rows 41 to 60 of a result set, and why does the generated SQL differ between older SQL Server versions, SQL CE and everything else? | rows, vendored | clean |
| q08 | c12 | How do pending edits queued during one job get saved to SQL, or discarded, once the job finishes, and what happens when such jobs are nested inside each other? | When a unit of work finishes, what decides whether the repository changes it has been holding get written to the database or thrown away, and what happens when one scope is nested inside another? | nested, thrown, holding, written, finish | clean |
| q10 | c15 | After someone edits something, how long is the delay before the site writes its snapshot of the live pages to disk, and what is the longest it will hold off during a continuous stream of edits? | After an edit, how long is the delay before the published XML cache is written to disk, and what's the longest it will hold off during a continuous stream of edits? | delay, disk | clean |
| q11 | c17 | After an editor pushes an edit live, how does it travel from the notification to every machine in a farm and into each machine's in-memory snapshot of the site? | After an editor publishes a page, how does the change get from the publish event to every node in a load-balanced cluster and into each node's in-memory XML cache? | cluster, balanc | clean |
| q13 | c19 | Resolved address-to-webpage lookups are memoised in both directions. Why are some stored entries trusted only one way, used for building outbound links but never for matching incoming hits? | The published content cache memoises URL-to-node lookups in both directions. Why are some entries kept only one way, usable for building outbound links but never for matching incoming requests? | oneway, outbound, direction | clean |
| q16 | c23 | When a 'before' or 'after' notification is queued without a label, how does the code work out which one it was (for example 'Saving' versus 'Saved'), and when can it not tell? | When a 'before' or 'after' event is raised without being told which one it is, how does the code work out whether it was, say, Saving or Saved, and when can it not tell? | before, tell | clean |
| q17 | c24 | When a webpage is discarded but not yet purged, how does the platform remember where it originally lived so it can be put back in the right place later? | When a page is deleted but not yet purged, how does Umbraco remember where it used to live so it can be restored to the right place later? | purge, restore, remember | clean |
| q18 | c26 | When an item holding an attachment is removed permanently, at what point are its bytes wiped from disk, and what happens to them if the surrounding work never finishes? | When content or media with an uploaded file is permanently deleted, at what point is the file removed from disk, and what happens to it if the surrounding transaction never completes? | disk, transaction | clean |
| q19 | c27 | If something is persisted and then erased inside the same transaction, why don't subscribers to the 'persisted' notification ever hear about it? | If something is saved and then deleted inside the same transaction, why do subscribers to the Saved event never hear about it? | transaction, subscriber | clean |
| q21 | c29 | What stops a chain of forwards, sign-in bounces and dead-end fallbacks from cycling endlessly while a visitor is being served? | What stops a chain of redirects, login bounces and 404 fallbacks from going round forever while a front-end page is being served? | chain, forever | clean |
| q22 | c31 | When an editor renames or relocates a live webpage, how do visitors who hit the previous address end up at the new one? | When an editor renames or moves a published page, how do visitors who hit the old URL end up at the new one? | rename, visitor | clean |
| q26 | c39 | How does the platform decide whether an incoming address is meant for the admin UI rather than the visitor-facing site, e.g. /umbraco/Surface/... versus /umbraco/BackOffice/...? | How does Umbraco decide whether an incoming URL is meant for the admin UI rather than the public site, e.g. /umbraco/Surface/... versus /umbraco/BackOffice/...? | admin, incoming | clean |
| q27 | c40 | For an ordinary visitor-facing hit, at which ASP.NET pipeline stage is it intercepted, and where are the matching webpage and its layout worked out before control is handed off? | For an ordinary front-end request, at which ASP.NET pipeline event does Umbraco take over, and where are the matching page and its template worked out before the request is handed to a handler? | pipeline, matching | clean |
| q28 | c41 | In the admin screens, what happens when someone discards a webpage that was previously discarded? | In the back office, what happens when someone throws away a page that has already been thrown away once? | thrown, throws | clean |
| q29 | c42 | Why is someone told a webpage cannot go live because the one above it isn't live, and where is that rule enforced versus where the wording is chosen? | Why is a user told a page can't be published because the page above it isn't published, and where is that rule enforced versus where the wording is chosen? | rule, wording | clean |
| q30 | c43 | When someone drags a batch onto the admin library with the kind set to automatic, how is each one classed as a picture or a generic attachment, and what happens to ones whose suffix isn't allowed? | When someone drags a batch of files onto the media section with the media type set to auto-detect, how does it pick a media type for each one, and what happens to files of a blocked type? | detect, blocked, pick | clean |
| q32 | c45 | When a writer creates a brand-new webpage and pushes it live in one click, which rights are verified, and against the new webpage or the one above it? | When an editor creates a brand-new page and saves and publishes it in one go, which rights are checked, and are they checked against the new page or the one above it? | rights, brand | clean |
| q34 | c48 | If a developer modifies a server-side widget on disk, how does the old WebForms renderer know to stop serving its stale saved markup? | If a developer edits a user control or XSLT file on disk, how does the old WebForms rendering path know to stop serving the stale cached output it built from it? | disk, stale, webforms | clean |
| q35 | c50 | From legacy transforms, how can I pull a remote feed and keep a copy for a fixed period that isn't flushed when things go live? | From XSLT, how do I fetch a remote XML feed and keep a cached copy for a set period that doesn't get flushed on publish? | feed, period, remote | clean |
| q37 | c52 | How does the admin landing screen pull in outside news without letting it be abused to fetch arbitrary websites? | How does the back-office dashboard pull in outside news without letting itself be abused to fetch arbitrary URLs? | news, arbitrary | clean |
| q38 | c53 | When old v4-style code asks a wrapper to go live, which old-style notifications fire, and where is the real work passed on to? | When old v4-style code publishes a page through the legacy wrapper class, which legacy events fire, and where is the real work passed on to? | wrapper, v4 | clean |
| q39 | c55 | How does the old rich-text box render an embedded widget while someone is editing? It appears to make a loopback hop to itself carrying the author's login. | How does the legacy rich-text editor render an embedded macro while someone is editing? It seems to make an HTTP request back to itself carrying the author's login. | login, itself, richtext, macro | HIT: `macro` in macro.cs (AddMacroParameter, AddMacroResultToCache, AddMacroXmlNode…); `macro` in macroResultWrapper.aspx.cs (GetMacro, Macro, macro…) |

Notes on individual rewrites (why a word was chosen, in one line each):

- q02 (c02): *rollback*, *version* name the mechanism (`Rollback`, `versionId`); "restores an earlier version" keeps *version* as context and uses *restore*/*revert* as crux. Both clean.
- q04 (c06): *membership provider* is the crux and leaks on both files (`IsUmbracoMembershipProvider`), so it is described as "a custom login system for members"; *custom* and *built-in* are clean.
- q05 (c08): *number* leaks (`uniqueNumber`); "counter in brackets" kept from the draft because it is already plain.
- q06 (c09): the engine names (SQL Server, SQL CE) are named outright; they are context, not the mechanism. The crux (*rows*, the row-slice) only appears in a string literal. *paging* was avoided because it is the crux and leaks (`BuildSqlDbSpecificPagingQuery`).
- q08 (c12): first rewrite avoided *scope* and *unit of work* entirely ("a piece of work"); replaced in the final-fixes pass (§10) with plain wording that names them. Crux nouns are *nested*, *thrown*, *holding*, *written*, *finish*: all clean.
- q11 (c17): *server* leaks (`ClearXsltCacheOnCurrentServer`), so "every node in a load-balanced cluster"; *node* is generic.
- q13 (c19): *trust* is the parameter name in `RoutesCache.Store`, so "kept only one way". *route* avoided (it is the cached thing).
- q16 (c23): *name* is the crux (`EventNameExtractor`) and *ambiguous* is an identifier; "without being told which one it is … when can it not tell".
- q17 (c24): *recycle bin*, *trashed*, *bin*, *original* all leak; "deleted but not yet purged … where it used to live". *delete* appears in identifiers (`RelateParentDocumentOnDeleteAlias`) but is the trigger, not the mechanism.
- q18 (c26): *scope* leaks, *transaction* is clean on all three files, so "if the surrounding transaction never completes". *file* appears in `MediaFilesToDelete` but is the object, not the mechanism (which is the deferral).
- q21 (c29): *loop* (`maxLoop`) and *internal* (`FollowInternalRedirects`) are the crux and leak; "going round forever". *redirects* is kept as context.
- q27 (c40): *template* is kept (`FindTemplate` exists in the engine); it is what is found, not the mechanism. Flagged in sign-off row 7.
- q28 (c41): *delete* (`DeleteById`), *trash* (`FailedIsTrashed`), *bin* (`MoveToRecycleBin`) all leak, so "throws away … thrown away once". `throw` is a C# keyword, not an identifier.
- q30 (c43): *image* (`ImageFileTypes`), *extension* (`AutoMapperExtensions`) and *allowed* (`notAllowedAtRoot` …) leak, so "pick a media type for each one … files of a blocked type"; *media type* is generic.
- q34 (c48): *macro* was dropped entirely; "a user control or XSLT file" plus "old WebForms rendering path" still identifies `MacroNeedsToBeClearedFromCache` uniquely (no other indexed file compares `LastWriteTime` against a cache date for rendering; `Umbraco.Web/Macros/*` has no such check).
- q35 (c50): *seconds* leaks (`CacheInSeconds`), so "for a set period"; *cache* is generic and not the crux (the crux is the remote feed + fixed lifetime).
- q39 (c55): *cookie* (`CookieContainer`) and *preview* (`InPreviewMode`) leak and were dropped. *macro* remains and hits 403 lines in macro.cs: it is the subject of the question, there is no other word for it, and the mechanism (the loopback request) is what the crux rule guards. This is the one recorded hit in the set and is now a recorded decision (§10), so it is not in the sign-off table.


## 3. Independent spot-check of five high-confidence candidates (Step 3)

Sorted high-confidence ids (45) were written one per line to `high.txt`. Command, run with GNU shuf 9.12:

```
shuf -n 5 --random-source=<(yes 42) high.txt
c09
c53
c02
c48
c19
```

The draw is deterministic (re-run gave the same five). For each I opened the file at the lines before re-reading the candidate.

| cand | q-id | file:lines | what the member does (one sentence) | answers the question? | another file equally good? | result |
|---|---|---|---|---|---|---|
| c09 | q06 | PetaPoco.cs:L742–L789 `Database.BuildSqlDbSpecificPagingQuery` | Builds the page-slice SQL in three branches: `ROW_NUMBER() OVER … peta_rn` for SqlServer/Oracle, `OFFSET … ROWS FETCH NEXT` for SqlServerCE, `LIMIT … OFFSET` otherwise. | Yes: both halves (how rows 41–60 are fetched, why the SQL differs). | `UmbracoDatabase.cs` overrides it (L254) but only re-routes SQL Server ≥2012 to the CE branch; it contains none of the SQL. Not equal. | **pass** |
| c53 | q38 | Document.cs:L784–L822 `Document.PublishWithResult` (public + internal overload) | Fires the legacy `FireBeforePublish`, calls `ContentService.PublishWithStatus(ContentEntity, u.Id)`, then `FireAfterPublish`. | Yes: names the old events and the hand-off target. | `ContentService.cs` is the hand-off target but does not say which legacy events fire. Other `Document` publish overloads (L779, L825, L860, L976) are in the same file. Not equal. | **pass** |
| c02 | q02 | ContentService.cs:L1962–L2003 `ContentService.Rollback` | Loads the old version, calls `ChangePublishedState(PublishedState.Unpublished)`, saves it as a new version; the doc comment says only properties roll back, not Name/Alias. | Yes: still published → no; which parts revert → properties only. | `Document.RollBack` (L839–L847) is an obsolete wrapper that calls this method and holds none of the rule. Not equal. | **pass** |
| c48 | q34 | macro.cs:L723–L753 `macro.MacroNeedsToBeClearedFromCache` | For file-based macros, compares `macroFile.LastWriteTime` with the cached date-added and returns true (clear the cache) if the file is newer. | Yes. | `grep -l LastWriteTime` over the index: only `content.cs` (XML cache file) and `getXsltStatus.asmx.cs` (dev tool) are near; `Umbraco.Web/Macros/*` has no file-change check. Not equal. | **pass** |
| c19 | q13 | PublishedContentCache.cs:L86–L109 `GetRouteById` + RoutesCache.cs:L81–L92 `Store` | `GetRouteById` stores the computed route with `trust: false` ('do NOT trust it as it can be a colliding route'); `Store` only adds the route→id entry when `trust` is true. | Yes: both files together explain the one-way entries. | `DetermineRouteById` / `AddToCacheIfDeepestRoute` are in the same file (L69–L84). No third file. | **pass** |

Five of five passed, so the seed-43 extension was not triggered. (For the record, `shuf -n 10 --random-source=<(yes 43) high.txt` would have given c09 c55 c53 c48 c19 c41 c17 c21 c29 c13.)


## 4. Short-member swaps (Step 4)

Criteria: body ≤ 6 lines, and the members immediately above **and** below are a different concern. `c37` (requestModule.AddTask, neighbours in the same log-scrubbing region) and `c54` (library.RefreshContent, between two publish helpers) were replaced.

| replaces | new cand → q-id | file | member (decl / body) | neighbour above | neighbour below | why unrelated |
|---|---|---|---|---|---|---|
| c37 | n1 → q40 | `src/Umbraco.Web/umbraco.presentation/library.cs` | `ReplaceLineBreaks(string text)`: doc L1006–L1010, decl L1011, body L1013 (1 line), close L1014 | `ShortDate(string Date, bool WithTime, string TimeSplitter)` L994–L1004: parses a date string and formats it | `RenderMacroContent(string Text, int PageId)` L1024–L1042: renders macro markup through the legacy `template`/`page` classes | Date formatting / HTML line-break substitution / macro rendering share nothing but the static class. |
| c54 | n2 → q23 | `src/Umbraco.Web/Routing/PublishedContentRequest.cs` | `UpdateOnMissingTemplate()`: doc L109–L111, decl L112, body L114–L117 (4 lines), close L118 | `ConfigureRequest()` L104–L107 (`[Obsolete]`, forwards to `_engine.ConfigureRequest()`) | `OnPreparing()` L123–L128 (raises the `Preparing` event and locks the Uri) | Legacy configure wrapper / missing-template fix-up / event trigger are three separate responsibilities; UpdateOnMissingTemplate's only collaborator is the engine, not either neighbour. |

The `n1` question is navigation (Legacy layer, lsp_trivial true), the `n2` question is navigation (Web routing, lsp_trivial true). `library.ReplaceLineBreaks` is the spare named in the method doc §3c. `UriExtensions.MakeAbsolute` (L291–L297) was considered and rejected because the member directly below it, `GetSafeQuery`, is its own private helper.

Disambiguation: `ReplaceLineBreaks(` appears in 4 indexed files (library.cs plus three callers: `umbraco/controls/ContentControl.cs`, `dialogs/rollBack.aspx.cs`, `translation/details.aspx.cs`); `UpdateOnMissingTemplate(` appears in 2 (PublishedContentRequest.cs and its caller `Mvc/RenderRouteHandler.cs`). Both questions name the owning class.


## 5. Cut list and id mapping (Step 5)

Pool after Step 4: 55 (53 originals + n1 + n2). Targets 15 / 15 / 10. Cuts needed: navigation 6, semantic 5, cross-cutting 4.

| dropped | cat | subsystem | rule | reason |
|---|---|---|---|---|
| c37 | navigation | HttpModules | swap | Replaced by n1 (Step 4): weak short-member fit, and whether the legacy module is registered depends on web.config. |
| c54 | cross-cutting | Legacy | swap | Replaced by n2 (Step 4): neighbours are sibling publish helpers. |
| c11 | cross-cutting | Persistence | (2) near-duplicate | Same subsystem and same mechanism as c09 (PetaPoco paging SQL); c09 keeps the deep-in-large-class label and the fuller question. |
| c16 | semantic | Cache | (2) near-duplicate | Same file and mechanism as c19 (route caching in PublishedContentCache); c19 passed the spot-check and covers both files. |
| c21 | navigation | Events | (2) near-duplicate | Same mechanism as c27 (`[SupersedeEvent]` on DeleteEventArgs); c27 keeps DeleteEventArgs.cs as a correct file. |
| c32 | semantic | Web routing | (2) near-duplicate + (3) | Same file and mechanism as c31 (redirect tracking); its answer also leans on `umbracoSettings.config`. |
| c07 | navigation | Persistence | (2) near-duplicate | Same subsystem and mechanism as c12 (ScopeUnitOfWork flushing queued operations). |
| c30 | navigation | Web routing | (2) near-duplicate | Template selection is already covered by c28 (ContentFinderByNiceUrlAndTemplate / IsAllowedTemplate), and the engine file is already a correct file of c29 and c40. |
| c25 | cross-cutting | Events | (2) near-duplicate | Same subsystem and mechanism family as c24 (a Core/Strategies relation handler subscribed to a ContentService event); after Step 1 it also needed three files. |
| c49 | navigation | Legacy | (2) near-duplicate | Same subsystem and mechanism as c55 (in-editor macro rendering: ShowNoMacroContent is the fallback of MacroContentByHttp). Short-member minimum still met (4 ≥ 3). |
| c36 | navigation | HttpModules | (4) low value | Lower-case legacy module that is disabled unless `ViewStateMoverModule/@enable` is set; the subsystem keeps c35, c38, c40 on the active module. |
| c14 | semantic | Cache | (4) low value | No indexed code calls `SuspendDocumentCache`; the mechanism exists for external tooling. Cache keeps c13, c15, c17, c18, c19. |
| c03 | semantic | Core services | (4) low value | The doc comment contradicts the code and the real default comes from web.config first; it needed a second file to be fair. Core keeps c01, c02, c04, c06. |
| c34 | semantic | HttpModules | (3)/(4) | Half the answer is the non-indexed splash page and `EnableSplashWhileLoading`; HttpModules keeps c39 as its semantic question and stays at the minimum of 4. |
| c05 | navigation | Core services | (4) low value | Plain service-method navigation with nothing structural to test; c01 (deep) and c04 (short) already cover ContentService/MediaService navigation. Core stays at 4. |
| c47 | cross-cutting | Backoffice | (4) low value | After Step 1 it needed three files, and the natural wording ("reset their own password") is blocked by the crux rule; Backoffice keeps six others. |
| c33 | cross-cutting | Web routing | (4) borderline | Same file and request flow as c29 (HandlePublishedContent → EnsurePublishedContentAccess); the natural words (locked out, approved, protected) all leak. Web routing stays at 4. |

Dropped: 17 (2 swaps + 15 cuts). Kept: 40.

No discriminator minimum was breached by a cut: long-doc 8 → 8 (none cut), deep-in-large-class 8 → 7 (c11), short-member 5 → 4 (c49; c37/c54 replaced).

### Candidate id → q-id mapping

| q-id | cand | category | subsystem | discriminator |
|---|---|---|---|---|
| q01 | c01 | navigation | Core services | deep-in-large-class |
| q02 | c02 | semantic | Core services | deep-in-large-class |
| q03 | c04 | navigation | Core services | short-member |
| q04 | c06 | cross-cutting | Core services | none |
| q05 | c08 | semantic | Persistence | none |
| q06 | c09 | semantic | Persistence | deep-in-large-class |
| q07 | c10 | navigation | Persistence | deep-in-large-class |
| q08 | c12 | cross-cutting | Persistence | none |
| q09 | c13 | navigation | Cache | none |
| q10 | c15 | semantic | Cache | none |
| q11 | c17 | cross-cutting | Cache | none |
| q12 | c18 | navigation | Cache | long-doc |
| q13 | c19 | cross-cutting | Cache | none |
| q14 | c20 | navigation | Events | none |
| q15 | c22 | navigation | Events | long-doc |
| q16 | c23 | semantic | Events | long-doc |
| q17 | c24 | semantic | Events | none |
| q18 | c26 | cross-cutting | Events | none |
| q19 | c27 | cross-cutting | Events | none |
| q20 | c28 | navigation | Web routing | none |
| q21 | c29 | semantic | Web routing | none |
| q22 | c31 | cross-cutting | Web routing | none |
| q23 | n2 | navigation | Web routing | short-member |
| q24 | c35 | navigation | HttpModules and request pipeline | none |
| q25 | c38 | navigation | HttpModules and request pipeline | long-doc |
| q26 | c39 | semantic | HttpModules and request pipeline | long-doc |
| q27 | c40 | cross-cutting | HttpModules and request pipeline | none |
| q28 | c41 | semantic | Backoffice API controllers | long-doc |
| q29 | c42 | cross-cutting | Backoffice API controllers | deep-in-large-class |
| q30 | c43 | semantic | Backoffice API controllers | none |
| q31 | c44 | navigation | Backoffice API controllers | long-doc |
| q32 | c45 | semantic | Backoffice API controllers | none |
| q33 | c46 | navigation | Backoffice API controllers | none |
| q34 | c48 | semantic | Legacy layer | deep-in-large-class |
| q35 | c50 | semantic | Legacy layer | deep-in-large-class |
| q36 | c51 | navigation | Legacy layer | short-member |
| q37 | c52 | semantic | Legacy layer | none |
| q38 | c53 | semantic | Legacy layer | long-doc |
| q39 | c55 | cross-cutting | Legacy layer | none |
| q40 | n1 | navigation | Legacy layer | short-member |

## 6. Final quota table

| requirement | target | actual |
|---|---|---|
| total | 40 | **40** |
| navigation / semantic / cross-cutting | 15 / 15 / 10 | **15 / 15 / 10** |
| lsp_trivial true / false | ≈15 / 25 | **15 / 25** |
| long-doc | ≥ 5 | **8** (q12, q15, q16, q25, q26, q28, q31, q38) |
| deep-in-large-class | ≥ 5 | **7** (q01, q02, q06, q07, q29, q34, q35) |
| short-member | ≥ 3 | **4** (q03, q23, q36, q40) |
| every subsystem | 4–8 | **4–7** |

| subsystem | nav | sem | cross | total |
|---|---|---|---|---|
| Core services | 2 | 1 | 1 | 4 |
| Persistence | 1 | 2 | 1 | 4 |
| Cache | 2 | 1 | 2 | 5 |
| Events | 2 | 2 | 2 | 6 |
| Web routing | 2 | 1 | 1 | 4 |
| HttpModules and request pipeline | 2 | 1 | 1 | 4 |
| Backoffice API controllers | 2 | 3 | 1 | 6 |
| Legacy layer | 2 | 4 | 1 | 7 |

Cross-cutting file counts: q04=2, q08=2, q11=3, q13=2, q18=3, q19=3, q22=2, q27=3, q29=2, q39=2. Navigation and semantic questions each have exactly one correct file.

Files shared by more than one question (allowed only across categories; no violations):

- `src/Umbraco.Core/Persistence/PetaPoco.cs`: q06 (semantic), q07 (navigation)
- `src/Umbraco.Core/Services/ContentService.cs`: q01 (navigation), q02 (semantic), q29 (cross-cutting)
- `src/Umbraco.Web/Cache/CacheRefresherEventHandler.cs`: q11 (cross-cutting), q12 (navigation)
- `src/Umbraco.Web/Editors/ContentController.cs`: q28 (semantic), q29 (cross-cutting), q33 (navigation)
- `src/Umbraco.Web/Editors/MediaController.cs`: q30 (semantic), q31 (navigation)
- `src/Umbraco.Web/Routing/PublishedContentRequest.cs`: q23 (navigation), q27 (cross-cutting)
- `src/Umbraco.Web/Routing/PublishedContentRequestEngine.cs`: q21 (semantic), q27 (cross-cutting)
- `src/Umbraco.Web/UmbracoModule.cs`: q24 (navigation), q27 (cross-cutting)
- `src/Umbraco.Web/umbraco.presentation/library.cs`: q35 (semantic), q36 (navigation), q40 (navigation)
- `src/Umbraco.Web/umbraco.presentation/macro.cs`: q34 (semantic), q39 (cross-cutting)

## 7. Verification output (Step 6)

Run from `corpus/Umbraco-CMS` against the written `data/golden.proposed.json` (`verify.py`, scratch directory). Every path in `correct_files`, `evidence.file` and `evidence_additional[].file` is tested with `test -f` and checked to be in scope; every snippet is searched with `grep -nF` and must hit inside its stated range.

### 7a. `test -f` on every path
```
ok   src/Umbraco.Core/Events/DeleteEventArgs.cs
ok   src/Umbraco.Core/Events/EventExtensions.cs
ok   src/Umbraco.Core/Events/EventNameExtractor.cs
ok   src/Umbraco.Core/Events/ScopeEventDispatcher.cs
ok   src/Umbraco.Core/Events/ScopeEventDispatcherBase.cs
ok   src/Umbraco.Core/Events/SupersedeEventAttribute.cs
ok   src/Umbraco.Core/Persistence/PetaPoco.cs
ok   src/Umbraco.Core/Persistence/Repositories/SimilarNodeName.cs
ok   src/Umbraco.Core/Persistence/UnitOfWork/ScopeUnitOfWork.cs
ok   src/Umbraco.Core/Publishing/PublishingStrategy.cs
ok   src/Umbraco.Core/Scoping/Scope.cs
ok   src/Umbraco.Core/Security/MembershipProviderExtensions.cs
ok   src/Umbraco.Core/Services/ContentService.cs
ok   src/Umbraco.Core/Services/MediaService.cs
ok   src/Umbraco.Core/Services/MemberService.cs
ok   src/Umbraco.Core/Strategies/RelateOnTrashHandler.cs
ok   src/Umbraco.Core/UriExtensions.cs
ok   src/Umbraco.Web/Cache/CacheRefresherEventHandler.cs
ok   src/Umbraco.Web/Cache/DistributedCache.cs
ok   src/Umbraco.Web/Cache/DistributedCacheExtensions.cs
ok   src/Umbraco.Web/Cache/PageCacheRefresher.cs
ok   src/Umbraco.Web/Editors/ContentController.cs
ok   src/Umbraco.Web/Editors/ContentPostValidateAttribute.cs
ok   src/Umbraco.Web/Editors/MediaController.cs
ok   src/Umbraco.Web/PropertyEditors/FileUploadPropertyEditor.cs
ok   src/Umbraco.Web/PropertyEditors/ImageCropperPropertyEditor.cs
ok   src/Umbraco.Web/PublishedCache/XmlPublishedCache/PublishedContentCache.cs
ok   src/Umbraco.Web/PublishedCache/XmlPublishedCache/RoutesCache.cs
ok   src/Umbraco.Web/PublishedCache/XmlPublishedCache/XmlCacheFilePersister.cs
ok   src/Umbraco.Web/Routing/ContentFinderByNiceUrlAndTemplate.cs
ok   src/Umbraco.Web/Routing/ContentFinderByRedirectUrl.cs
ok   src/Umbraco.Web/Routing/PublishedContentRequest.cs
ok   src/Umbraco.Web/Routing/PublishedContentRequestEngine.cs
ok   src/Umbraco.Web/Routing/RedirectTrackingEventHandler.cs
ok   src/Umbraco.Web/UmbracoContext.cs
ok   src/Umbraco.Web/UmbracoModule.cs
ok   src/Umbraco.Web/umbraco.presentation/library.cs
ok   src/Umbraco.Web/umbraco.presentation/macro.cs
ok   src/Umbraco.Web/umbraco.presentation/umbraco/dashboard/FeedProxy.aspx.cs
ok   src/Umbraco.Web/umbraco.presentation/umbraco/macroResultWrapper.aspx.cs
ok   src/umbraco.cms/businesslogic/web/Document.cs
distinct paths: 41, failures: 0
```

### 7b. `grep -nF` on every snippet (`qNN+k` = k-th additional evidence entry)
```
OK   q01/c01   ContentService.cs:1953  range L1933-L1960
OK   q02/c02   ContentService.cs:1967  range L1962-L2003
OK   q03/c04   MediaService.cs:735  range L725-L737
OK   q04/c06   MemberService.cs:102  range L90-L121
OK   q04+1     MembershipProviderExtensions.cs:160  range L153-L161
OK   q05/c08   SimilarNodeName.cs:115  range L93-L116
OK   q06/c09   PetaPoco.cs:781  range L742-L789
OK   q07/c10   PetaPoco.cs:392  range L385-L402
OK   q08/c12   ScopeUnitOfWork.cs:256  range L245-L259
OK   q08+1     Scope.cs:409  range L392-L428
OK   q09/c13   DistributedCache.cs:29  range L29-L29
OK   q10/c15   XmlCacheFilePersister.cs:38  range L11-L38
OK   q11/c17   CacheRefresherEventHandler.cs:277  range L260-L280
OK   q11+1     DistributedCacheExtensions.cs:160  range L158-L161
OK   q11+2     PageCacheRefresher.cs:72,101  range L96-L107
OK   q12/c18   CacheRefresherEventHandler.cs:359  range L347-L360
OK   q13/c19   PublishedContentCache.cs:102  range L86-L109
OK   q13+1     RoutesCache.cs:91  range L81-L92
OK   q14/c20   PublishingStrategy.cs:530  range L527-L531
OK   q15/c22   EventExtensions.cs:28  range L18-L34
OK   q16/c23   EventNameExtractor.cs:35,102  range L26-L91
OK   q17/c24   RelateOnTrashHandler.cs:80,126  range L50-L94
OK   q18/c26   ScopeEventDispatcher.cs:30  range L15-L32
OK   q18+1     FileUploadPropertyEditor.cs:184  range L171-L198
OK   q18+2     ImageCropperPropertyEditor.cs:282  range L268-L296
OK   q19/c27   ScopeEventDispatcherBase.cs:148  range L129-L243
OK   q19+1     DeleteEventArgs.cs:7  range L7-L143
OK   q19+2     SupersedeEventAttribute.cs:11  range L5-L19
OK   q20/c28   ContentFinderByNiceUrlAndTemplate.cs:36  range L17-L77
OK   q21/c29   PublishedContentRequestEngine.cs:462  range L449-L508
OK   q22/c31   RedirectTrackingEventHandler.cs:295  range L286-L296
OK   q22+1     ContentFinderByRedirectUrl.cs:61  range L17-L63
OK   q23/n2    PublishedContentRequest.cs:116  range L109-L118
OK   q24/c35   UmbracoModule.cs:447  range L433-L472
OK   q25/c38   UmbracoContext.cs:134  range L91-L142
OK   q26/c39   UriExtensions.cs:36  range L15-L102
OK   q27/c40   UmbracoModule.cs:136  range L72-L147
OK   q27+1     PublishedContentRequest.cs:93  range L88-L94
OK   q27+2     PublishedContentRequestEngine.cs:139  range L104-L160
OK   q28/c41   ContentController.cs:801  range L800-L844
OK   q29/c42   ContentService.cs:2726  range L2717-L2748
OK   q29+1     ContentController.cs:1072  range L1056-L1109
OK   q30/c43   MediaController.cs:786  range L689-L856
OK   q31/c44   MediaController.cs:863  range L858-L919
OK   q32/c45   ContentPostValidateAttribute.cs:127  range L68-L160
OK   q33/c46   ContentController.cs:868  range L863-L899
OK   q34/c48   macro.cs:744  range L723-L753
OK   q35/c50   library.cs:1551  range L1533-L1556
OK   q36/c51   library.cs:1691  range L1683-L1693
OK   q37/c52   FeedProxy.aspx.cs:33  range L18-L60
OK   q38/c53   Document.cs:811  range L784-L822
OK   q39/c55   macro.cs:1712  range L1690-L1783
OK   q39+1     macroResultWrapper.aspx.cs:47  range L23-L49
OK   q40/n1    library.cs:1013  range L1006-L1014
snippet failures: 0
files in correct_files without an evidence entry: []
```

### 7c. Crux-noun check on the final question text (identifier parts after stripping comments and literals; `grep -i -c` is the raw line count)
```
ok   q02/c02 'restore'      ContentService.cs                    grep -i -c =   0  identifiers=[]
ok   q02/c02 'revert'       ContentService.cs                    grep -i -c =   0  identifiers=[]
ok   q04/c06 'custom'       MemberService.cs                     grep -i -c =   0  identifiers=[]
ok   q04/c06 'builtin'      MemberService.cs                     grep -i -c =   0  identifiers=[]
ok   q04/c06 'built-in'     MemberService.cs                     grep -i -c =   0  identifiers=[]
ok   q04/c06 'custom'       MembershipProviderExtensions.cs      grep -i -c =   0  identifiers=[]
ok   q04/c06 'builtin'      MembershipProviderExtensions.cs      grep -i -c =   0  identifiers=[]
ok   q04/c06 'built-in'     MembershipProviderExtensions.cs      grep -i -c =   2  identifiers=[]
ok   q05/c08 'counter'      SimilarNodeName.cs                   grep -i -c =   0  identifiers=[]
ok   q05/c08 'bracket'      SimilarNodeName.cs                   grep -i -c =   0  identifiers=[]
ok   q06/c09 'rows'         PetaPoco.cs                          grep -i -c =   1  identifiers=[]
ok   q06/c09 'vendored'     PetaPoco.cs                          grep -i -c =   0  identifiers=[]
ok   q08/c12 'nested'       ScopeUnitOfWork.cs                   grep -i -c =   0  identifiers=[]
ok   q08/c12 'thrown'       ScopeUnitOfWork.cs                   grep -i -c =   0  identifiers=[]
ok   q08/c12 'holding'      ScopeUnitOfWork.cs                   grep -i -c =   0  identifiers=[]
ok   q08/c12 'written'      ScopeUnitOfWork.cs                   grep -i -c =   0  identifiers=[]
ok   q08/c12 'finish'       ScopeUnitOfWork.cs                   grep -i -c =   0  identifiers=[]
ok   q08/c12 'nested'       Scope.cs                             grep -i -c =   3  identifiers=[]
ok   q08/c12 'thrown'       Scope.cs                             grep -i -c =   0  identifiers=[]
ok   q08/c12 'holding'      Scope.cs                             grep -i -c =   0  identifiers=[]
ok   q08/c12 'written'      Scope.cs                             grep -i -c =   0  identifiers=[]
ok   q08/c12 'finish'       Scope.cs                             grep -i -c =   0  identifiers=[]
ok   q10/c15 'delay'        XmlCacheFilePersister.cs             grep -i -c =   2  identifiers=[]
ok   q10/c15 'disk'         XmlCacheFilePersister.cs             grep -i -c =   0  identifiers=[]
ok   q11/c17 'cluster'      CacheRefresherEventHandler.cs        grep -i -c =   0  identifiers=[]
ok   q11/c17 'balanc'       CacheRefresherEventHandler.cs        grep -i -c =   0  identifiers=[]
ok   q11/c17 'cluster'      DistributedCacheExtensions.cs        grep -i -c =   0  identifiers=[]
ok   q11/c17 'balanc'       DistributedCacheExtensions.cs        grep -i -c =   1  identifiers=[]
ok   q11/c17 'cluster'      PageCacheRefresher.cs                grep -i -c =   0  identifiers=[]
ok   q11/c17 'balanc'       PageCacheRefresher.cs                grep -i -c =   3  identifiers=[]
ok   q13/c19 'oneway'       PublishedContentCache.cs             grep -i -c =   0  identifiers=[]
ok   q13/c19 'outbound'     PublishedContentCache.cs             grep -i -c =   0  identifiers=[]
ok   q13/c19 'direction'    PublishedContentCache.cs             grep -i -c =   0  identifiers=[]
ok   q13/c19 'oneway'       RoutesCache.cs                       grep -i -c =   0  identifiers=[]
ok   q13/c19 'outbound'     RoutesCache.cs                       grep -i -c =   0  identifiers=[]
ok   q13/c19 'direction'    RoutesCache.cs                       grep -i -c =   0  identifiers=[]
ok   q16/c23 'before'       EventNameExtractor.cs                grep -i -c =   1  identifiers=[]
ok   q16/c23 'tell'         EventNameExtractor.cs                grep -i -c =   0  identifiers=[]
ok   q17/c24 'purge'        RelateOnTrashHandler.cs              grep -i -c =   0  identifiers=[]
ok   q17/c24 'restore'      RelateOnTrashHandler.cs              grep -i -c =   2  identifiers=[]
ok   q17/c24 'remember'     RelateOnTrashHandler.cs              grep -i -c =   0  identifiers=[]
ok   q18/c26 'disk'         FileUploadPropertyEditor.cs          grep -i -c =   0  identifiers=[]
ok   q18/c26 'transaction'  FileUploadPropertyEditor.cs          grep -i -c =   0  identifiers=[]
ok   q18/c26 'disk'         ScopeEventDispatcher.cs              grep -i -c =   0  identifiers=[]
ok   q18/c26 'transaction'  ScopeEventDispatcher.cs              grep -i -c =   0  identifiers=[]
ok   q18/c26 'disk'         ImageCropperPropertyEditor.cs        grep -i -c =   0  identifiers=[]
ok   q18/c26 'transaction'  ImageCropperPropertyEditor.cs        grep -i -c =   0  identifiers=[]
ok   q19/c27 'transaction'  ScopeEventDispatcherBase.cs          grep -i -c =   0  identifiers=[]
ok   q19/c27 'subscriber'   ScopeEventDispatcherBase.cs          grep -i -c =   0  identifiers=[]
ok   q19/c27 'transaction'  DeleteEventArgs.cs                   grep -i -c =   0  identifiers=[]
ok   q19/c27 'subscriber'   DeleteEventArgs.cs                   grep -i -c =   0  identifiers=[]
ok   q19/c27 'transaction'  SupersedeEventAttribute.cs           grep -i -c =   0  identifiers=[]
ok   q19/c27 'subscriber'   SupersedeEventAttribute.cs           grep -i -c =   0  identifiers=[]
ok   q21/c29 'chain'        PublishedContentRequestEngine.cs     grep -i -c =   0  identifiers=[]
ok   q21/c29 'forever'      PublishedContentRequestEngine.cs     grep -i -c =   0  identifiers=[]
ok   q22/c31 'rename'       RedirectTrackingEventHandler.cs      grep -i -c =   1  identifiers=[]
ok   q22/c31 'visitor'      RedirectTrackingEventHandler.cs      grep -i -c =   0  identifiers=[]
ok   q22/c31 'rename'       ContentFinderByRedirectUrl.cs        grep -i -c =   1  identifiers=[]
ok   q22/c31 'visitor'      ContentFinderByRedirectUrl.cs        grep -i -c =   0  identifiers=[]
ok   q26/c39 'admin'        UriExtensions.cs                     grep -i -c =   0  identifiers=[]
ok   q26/c39 'incoming'     UriExtensions.cs                     grep -i -c =   0  identifiers=[]
ok   q27/c40 'pipeline'     UmbracoModule.cs                     grep -i -c =   0  identifiers=[]
ok   q27/c40 'matching'     UmbracoModule.cs                     grep -i -c =   0  identifiers=[]
ok   q27/c40 'pipeline'     PublishedContentRequest.cs           grep -i -c =   0  identifiers=[]
ok   q27/c40 'matching'     PublishedContentRequest.cs           grep -i -c =   0  identifiers=[]
ok   q27/c40 'pipeline'     PublishedContentRequestEngine.cs     grep -i -c =   2  identifiers=[]
ok   q27/c40 'matching'     PublishedContentRequestEngine.cs     grep -i -c =   6  identifiers=[]
ok   q28/c41 'thrown'       ContentController.cs                 grep -i -c =   0  identifiers=[]
ok   q28/c41 'throws'       ContentController.cs                 grep -i -c =   0  identifiers=[]
ok   q29/c42 'rule'         ContentService.cs                    grep -i -c =   0  identifiers=[]
ok   q29/c42 'wording'      ContentService.cs                    grep -i -c =   0  identifiers=[]
ok   q29/c42 'rule'         ContentController.cs                 grep -i -c =   0  identifiers=[]
ok   q29/c42 'wording'      ContentController.cs                 grep -i -c =   0  identifiers=[]
ok   q30/c43 'detect'       MediaController.cs                   grep -i -c =   0  identifiers=[]
ok   q30/c43 'blocked'      MediaController.cs                   grep -i -c =   0  identifiers=[]
ok   q30/c43 'pick'         MediaController.cs                   grep -i -c =   0  identifiers=[]
ok   q32/c45 'rights'       ContentPostValidateAttribute.cs      grep -i -c =   0  identifiers=[]
ok   q32/c45 'brand'        ContentPostValidateAttribute.cs      grep -i -c =   0  identifiers=[]
ok   q34/c48 'disk'         macro.cs                             grep -i -c =   0  identifiers=[]
ok   q34/c48 'stale'        macro.cs                             grep -i -c =   0  identifiers=[]
ok   q34/c48 'webforms'     macro.cs                             grep -i -c =   0  identifiers=[]
ok   q35/c50 'feed'         library.cs                           grep -i -c =   0  identifiers=[]
ok   q35/c50 'period'       library.cs                           grep -i -c =   0  identifiers=[]
ok   q35/c50 'remote'       library.cs                           grep -i -c =   1  identifiers=[]
ok   q37/c52 'news'         FeedProxy.aspx.cs                    grep -i -c =   0  identifiers=[]
ok   q37/c52 'arbitrary'    FeedProxy.aspx.cs                    grep -i -c =   0  identifiers=[]
ok   q38/c53 'wrapper'      Document.cs                          grep -i -c =   0  identifiers=[]
ok   q38/c53 'v4'           Document.cs                          grep -i -c =   0  identifiers=[]
ok   q39/c55 'login'        macro.cs                             grep -i -c =   0  identifiers=[]
ok   q39/c55 'itself'       macro.cs                             grep -i -c =   0  identifiers=[]
ok   q39/c55 'richtext'     macro.cs                             grep -i -c =   0  identifiers=[]
LEAK q39/c55 'macro'        macro.cs                             grep -i -c = 403  identifiers=['AddMacroParameter', 'AddMacroResultToCache', 'AddMacroXmlNode', 'AddMacroXsltExtensions', 'CacheMacroAsString']
ok   q39/c55 'login'        macroResultWrapper.aspx.cs           grep -i -c =   0  identifiers=[]
ok   q39/c55 'itself'       macroResultWrapper.aspx.cs           grep -i -c =   0  identifiers=[]
ok   q39/c55 'richtext'     macroResultWrapper.aspx.cs           grep -i -c =   0  identifiers=[]
LEAK q39/c55 'macro'        macroResultWrapper.aspx.cs           grep -i -c =   6  identifiers=['GetMacro', 'Macro', 'macro', 'macroID', 'macroResultWrapper']
crux hits (identifier parts): 2
```


## 8. Sign-off table

The ten places where I exercised the most judgement (refreshed after §10: q08 and q23 are in, q39 is out because its wording is now a recorded decision). Each check is designed to take under two minutes with the file open at the lines given.

| # | q-id | question | file:lines | what to check | my confidence |
|---|---|---|---|---|---|
| 1 | q27 (c40) | For an ordinary front-end request, at which ASP.NET pipeline event does Umbraco take over, and where are the matching page and its template worked out before the request is handed to a handler? | UmbracoModule.cs:L519–L522 and L84–L147; PublishedContentRequest.cs:L91–L94; PublishedContentRequestEngine.cs:L104–L160 | Added file. Follow `app.PostResolveRequestCache` (L519) → `ProcessRequest` → `pcr.Prepare()` (L136) → `_engine.PrepareRequest()` → `FindDomain()` L121 and `FindPublishedContentAndTemplate()` L139. Confirm the engine deserves to be a correct file, and that keeping the word "template" is acceptable (FindTemplate exists at L667). | high |
| 2 | q18 (c26) | When content or media with an uploaded file is permanently deleted, at what point is the file removed from disk, and what happens to it if the surrounding transaction never completes? | ImageCropperPropertyEditor.cs:L282–L296 vs FileUploadPropertyEditor.cs:L184–L198; ScopeEventDispatcher.cs:L15–L32 | Added file. Confirm the three `Deleted +=` handlers are the same shape in both editors, and that "surrounding transaction never completes" is a fair description of `ScopeExitCompleted` only running on completion (base class L334–L340 if you want the literal rule). | high |
| 3 | q08 (c12) | When a unit of work finishes, what decides whether the repository changes it has been holding get written to the database or thrown away, and what happens when one scope is nested inside another? | ScopeUnitOfWork.cs:L125–L161 (Commit sets _completeScope L156) and L251–L259; Scope.cs:L329–L350 and L392–L428 | Rewritten in the final-fixes pass to say "unit of work" and "scope" outright. Check that the question still asks for behaviour (the completion flag) rather than a named member: Commit → _completeScope → _scope.Complete() on dispose; DisposeLastScope commits or aborts on _completed; ChildCompleted forces false when a nested scope did not complete. | high |
| 4 | q13 (c19) | The published content cache memoises URL-to-node lookups in both directions. Why are some entries kept only one way, usable for building outbound links but never for matching incoming requests? | PublishedContentCache.cs:L86–L109; RoutesCache.cs:L81–L92 | "kept only one way" replaces "trusted" because `trust` is the parameter name at L87. Check the comment at L102–L104 and that `Store` skips `_nodeIds` when trust is false. | high |
| 5 | q17 (c24) | When a page is deleted but not yet purged, how does Umbraco remember where it used to live so it can be restored to the right place later? | RelateOnTrashHandler.cs:L50–L94 | "deleted but not yet purged … where it used to live" avoids recycle bin / trashed / original (all identifiers). Check the comment at L80 and the relation created with `originalParentId`; decide whether "deleted" (which appears in `RelateParentDocumentOnDeleteAlias`) is acceptable as the trigger word. | high |
| 6 | q28 (c41) | In the back office, what happens when someone throws away a page that has already been thrown away once? | ContentController.cs:L800–L844 | "throws away a page that has already been thrown away once" is the plainest wording that avoids delete / trash / bin. Check the doc comment at L801 and the `IsTrashed` branch; decide if the wording is natural enough. | medium |
| 7 | q34 (c48) | If a developer edits a user control or XSLT file on disk, how does the old WebForms rendering path know to stop serving the stale cached output it built from it? | macro.cs:L723–L753 | The question no longer says "macro". Confirm "a user control or XSLT file … old WebForms rendering path … stale cached output" still points only here (the comparison `macroFile.LastWriteTime.CompareTo(dateMacroAdded)` at L744) and not to `content.cs` or `Umbraco.Web/Macros`. | medium-high |
| 8 | q23 (n2) | Where is PublishedContentRequest.UpdateOnMissingTemplate, which briefly lifts the read-only flag and calls the engine's UpdateRequestOnMissingTemplate? | PublishedContentRequest.cs:L104–L107, L109–L118, L123–L128 | Neighbour check re-done under the MakeAbsolute bar (§10). ConfigureRequest (obsolete, forwards to the engine), UpdateOnMissingTemplate (lifts _readonly, calls engine.UpdateRequestOnMissingTemplate, called only from Mvc/RenderRouteHandler.cs L376) and OnPreparing (raises the Preparing event, called from the engine L118) have no call relationship with each other. Decide whether "three façades over the same engine" is close enough to "different concern"; the Routing alternatives all fail harder (§10). | medium-high |
| 9 | q04 (c06) | Why does setting a member's password from code throw when the site uses a custom login system for members, and how does the code tell Umbraco's built-in one apart? | MemberService.cs:L90–L121; MembershipProviderExtensions.cs:L153–L161 | "custom login system for members" replaces "membership provider" (crux, leaks on both files). Check `if (provider.IsUmbracoMembershipProvider())` at L102 and the `NotSupportedException` branch; decide if the wording is clear enough. | high |
| 10 | q30 (c43) | When someone drags a batch of files onto the media section with the media type set to auto-detect, how does it pick a media type for each one, and what happens to files of a blocked type? | MediaController.cs:L689–L856 | "pick a media type for each one … files of a blocked type" replaces image / extension / allowed (all identifiers: `ImageFileTypes` L786, `notAllowedAtRoot`). Check the `ImageFileTypes.Contains(ext)` branch and the disallowed-extension path; confirm the question still asks what the method does. | medium-high |

Two further borderline decisions, not in the table because they are cuts rather than kept items: c33 (dropped as overlapping c29 and blocked on natural wording) and c47 (dropped after growing to three files). Both are reversible by restoring the candidate from `golden.candidates.json` with the Step 1 additions listed in §1.


## 9. What I did NOT do

- **No app runs.** I did not build or run RagAblation (`index`, `eval`, `diff`).
- **No database.** I did not connect to Postgres.
- **No results.** I did not list or open anything under `data/results/`.
- **No chunkers.** I did not use or read `src/RagAblation.Chunking`. Structural facts (line counts, member ranges, neighbours) come from `sed`, `grep`, `wc` and small Python scripts over the raw corpus files.
- **No golden.json.** I did not create, read or modify `data/golden.json`. It does not exist (`ls data/golden.json` at the start: no such file).
- **No commit.** Nothing was committed; the working tree is not a git repository at the project root anyway.
- **No code change.** The permitted loader change was not needed (see Step 0 at the top). `src/` and `tests/` are untouched.
- **Files written in the repo:** `data/golden.proposed.json` and this document only. Helper scripts (`cg`, `memend.py`, `shortscan.py`, `leak.py`, `striplib.py`, `build.py`, `verify.py`, `compose.py`) and intermediate outputs live in the session scratch directory.
- **One change outside the repo:** `brew install coreutils`, to get GNU `shuf` for Step 3.


## 10. Final fixes

Three follow-ups applied after the first review pass. Same hard rules; only `data/golden.proposed.json` and this document changed.

### q39 (c55): keep "macro"

Decision: "macro" is the Umbraco domain term and has no developer-English synonym, so excluding it would measure vocabulary avoidance rather than retrieval. The question text is unchanged. A `leakage_exception` field with that one-line rationale was added to q39. This is the **one deliberate crux-noun exception** in the set: every other semantic and cross-cutting question has an empty `found_as_identifier`.

### q08 (c12): remove "piece of work"

**Is unit of work / scope the mechanism?** No. The evidence shows the mechanism is the *completion flag*: `ScopeUnitOfWork.Commit` sets `_completeScope = true` (L156); `DisposeResources` turns that into `_scope.Complete()` (L256); `Scope.DisposeLastScope` calls `CompleteTransaction` or `AbortTransaction` on `_completed` (L392–L428); `Scope.Dispose` hands `_completed` to the parent's `ChildCompleted`, which forces `_completed = false` when a nested scope did not complete (L341–L350). None of those identifiers is in the question. "Unit of work" and "scope" name the object whose behaviour is being asked about, the same way "macro" does in q39, so q08 stays **cross-cutting**, `lsp_trivial` stays false, and no reinstatement from the cut list was needed.

| | wording |
|---|---|
| before | When repository inserts, updates and deletes are held back until a piece of work finishes, what decides whether they reach the database or are thrown away, and what happens when one such piece of work runs inside another? |
| after | When a unit of work finishes, what decides whether the repository changes it has been holding get written to the database or thrown away, and what happens when one scope is nested inside another? |

Crux-noun grep (identifier parts after stripping comments and literals; raw `grep -i -c` in brackets), against both correct files:

```
ok   nested        ScopeUnitOfWork.cs [0]   Scope.cs [3, comments only]
ok   thrown        ScopeUnitOfWork.cs [0]   Scope.cs [0]
ok   holding       ScopeUnitOfWork.cs [0]   Scope.cs [0]
ok   written       ScopeUnitOfWork.cs [0]   Scope.cs [0]
ok   finish        ScopeUnitOfWork.cs [0]   Scope.cs [0]
subject terms, recorded, not crux:
     scope         ScopeUnitOfWork.cs [25] CreateScope, IScope, ScopeUnitOfWork …   Scope.cs [122] AmbientScope, DisposeLastScope …
     unit of work  ScopeUnitOfWork.cs [8]  IScopeUnitOfWork, UnitOfWork …           Scope.cs [0]
```
*queued* (`Queue`, `Enqueue`), *committed* (`Commit`) and *disposed* (`DisposeResources`, `DisposeLastScope`) were kept out of the wording because they are the mechanism's own identifiers; "has been holding", "written", "thrown away" stand in for them. The `leakage_check` of q08 records the subject terms under `subject_terms_not_crux`.

### q23 (n2): neighbour check on PublishedContentRequest.UpdateOnMissingTemplate

Bar applied: the members immediately above **and** below must be a different concern, not helpers of the same operation (the bar that rejected `UriExtensions.MakeAbsolute`, whose neighbour below, `GetSafeQuery`, is called by it).

| position | member | lines | what it does |
|---|---|---|---|
| above | `ConfigureRequest()` | L104–L107 (doc L96–L102, `[Obsolete]`) | Legacy public entry point; forwards to `_engine.ConfigureRequest()`. Nothing in the indexed corpus calls it. |
| **q23** | `UpdateOnMissingTemplate()` | L112–L118 (doc L109–L111), body 4 lines | Clears `_readonly`, calls `_engine.UpdateRequestOnMissingTemplate()` (re-runs the 404 / template step), restores `_readonly`. Called only from `Mvc/RenderRouteHandler.cs` L376, after the request has already been prepared. |
| below | `OnPreparing()` | L123–L128 (doc L120–L122) | Raises the `Preparing` event and locks the Uri (`_readonlyUri = true`). Called from the engine at the start of `PrepareRequest` (L118). |

**Decision: passes, kept.** Neither neighbour is a helper of UpdateOnMissingTemplate's operation and it is not a helper of theirs: there is no call between the three, they have different callers (nobody / RenderRouteHandler / the engine) and different jobs (legacy entry point / post-preparation fix-up / event trigger). What they share is being thin façades over the engine, which is true of most of this class. Alternatives in the Web-routing subsystem (a replacement outside it would push the subsystem below 4) were checked by the same scan as Step 4 and all fail the bar harder:

- `DomainHelper.ExistsDomainInPath` L217–L220 (body 1): calls its neighbour below, `FindDomainInPath` L230–L241; helper of the same operation.
- `RedirectTrackingEventHandler.IsNotRoute` L298–L302 (body 2): called by its neighbour above, `CreateRedirect` L286–L296 (q22's evidence member); helper of the same operation.
- `SiteDomainHelper.MapDomain(Uri, DomainAndUri[])` L188–L194 (body 4): calls `GetQualifiedSites` (below) and the private `MapDomain` overload; helpers of the same operation.
- `NotFoundHandlerHelper.IsNotFoundHandlerEnabled<T>` L185–L188: between `GetNotFoundHandlers` and `GetNotFoundLastChanceHandler`, all not-found-handler resolution.
- `PublishedContentRequest.SetIs404` L509–L513 (body 2): neighbour below `SetRedirect` L536–L541 is a sibling response-outcome setter.

Short-member count stays at 4 (q03, q23, q36, q40). q23 is in the sign-off table (row 8) so the reviewer can overrule.

### Re-run of `test -f` and `grep -nF` on the final file

Same `verify.py` as section 7, run after the three changes above.

```
ok   src/Umbraco.Core/Events/DeleteEventArgs.cs
ok   src/Umbraco.Core/Events/EventExtensions.cs
ok   src/Umbraco.Core/Events/EventNameExtractor.cs
ok   src/Umbraco.Core/Events/ScopeEventDispatcher.cs
ok   src/Umbraco.Core/Events/ScopeEventDispatcherBase.cs
ok   src/Umbraco.Core/Events/SupersedeEventAttribute.cs
ok   src/Umbraco.Core/Persistence/PetaPoco.cs
ok   src/Umbraco.Core/Persistence/Repositories/SimilarNodeName.cs
ok   src/Umbraco.Core/Persistence/UnitOfWork/ScopeUnitOfWork.cs
ok   src/Umbraco.Core/Publishing/PublishingStrategy.cs
ok   src/Umbraco.Core/Scoping/Scope.cs
ok   src/Umbraco.Core/Security/MembershipProviderExtensions.cs
ok   src/Umbraco.Core/Services/ContentService.cs
ok   src/Umbraco.Core/Services/MediaService.cs
ok   src/Umbraco.Core/Services/MemberService.cs
ok   src/Umbraco.Core/Strategies/RelateOnTrashHandler.cs
ok   src/Umbraco.Core/UriExtensions.cs
ok   src/Umbraco.Web/Cache/CacheRefresherEventHandler.cs
ok   src/Umbraco.Web/Cache/DistributedCache.cs
ok   src/Umbraco.Web/Cache/DistributedCacheExtensions.cs
ok   src/Umbraco.Web/Cache/PageCacheRefresher.cs
ok   src/Umbraco.Web/Editors/ContentController.cs
ok   src/Umbraco.Web/Editors/ContentPostValidateAttribute.cs
ok   src/Umbraco.Web/Editors/MediaController.cs
ok   src/Umbraco.Web/PropertyEditors/FileUploadPropertyEditor.cs
ok   src/Umbraco.Web/PropertyEditors/ImageCropperPropertyEditor.cs
ok   src/Umbraco.Web/PublishedCache/XmlPublishedCache/PublishedContentCache.cs
ok   src/Umbraco.Web/PublishedCache/XmlPublishedCache/RoutesCache.cs
ok   src/Umbraco.Web/PublishedCache/XmlPublishedCache/XmlCacheFilePersister.cs
ok   src/Umbraco.Web/Routing/ContentFinderByNiceUrlAndTemplate.cs
ok   src/Umbraco.Web/Routing/ContentFinderByRedirectUrl.cs
ok   src/Umbraco.Web/Routing/PublishedContentRequest.cs
ok   src/Umbraco.Web/Routing/PublishedContentRequestEngine.cs
ok   src/Umbraco.Web/Routing/RedirectTrackingEventHandler.cs
ok   src/Umbraco.Web/UmbracoContext.cs
ok   src/Umbraco.Web/UmbracoModule.cs
ok   src/Umbraco.Web/umbraco.presentation/library.cs
ok   src/Umbraco.Web/umbraco.presentation/macro.cs
ok   src/Umbraco.Web/umbraco.presentation/umbraco/dashboard/FeedProxy.aspx.cs
ok   src/Umbraco.Web/umbraco.presentation/umbraco/macroResultWrapper.aspx.cs
ok   src/umbraco.cms/businesslogic/web/Document.cs
distinct paths: 41, failures: 0
```

```
OK   q01/c01   ContentService.cs:1953  range L1933-L1960
OK   q02/c02   ContentService.cs:1967  range L1962-L2003
OK   q03/c04   MediaService.cs:735  range L725-L737
OK   q04/c06   MemberService.cs:102  range L90-L121
OK   q04+1     MembershipProviderExtensions.cs:160  range L153-L161
OK   q05/c08   SimilarNodeName.cs:115  range L93-L116
OK   q06/c09   PetaPoco.cs:781  range L742-L789
OK   q07/c10   PetaPoco.cs:392  range L385-L402
OK   q08/c12   ScopeUnitOfWork.cs:256  range L245-L259
OK   q08+1     Scope.cs:409  range L392-L428
OK   q09/c13   DistributedCache.cs:29  range L29-L29
OK   q10/c15   XmlCacheFilePersister.cs:38  range L11-L38
OK   q11/c17   CacheRefresherEventHandler.cs:277  range L260-L280
OK   q11+1     DistributedCacheExtensions.cs:160  range L158-L161
OK   q11+2     PageCacheRefresher.cs:72,101  range L96-L107
OK   q12/c18   CacheRefresherEventHandler.cs:359  range L347-L360
OK   q13/c19   PublishedContentCache.cs:102  range L86-L109
OK   q13+1     RoutesCache.cs:91  range L81-L92
OK   q14/c20   PublishingStrategy.cs:530  range L527-L531
OK   q15/c22   EventExtensions.cs:28  range L18-L34
OK   q16/c23   EventNameExtractor.cs:35,102  range L26-L91
OK   q17/c24   RelateOnTrashHandler.cs:80,126  range L50-L94
OK   q18/c26   ScopeEventDispatcher.cs:30  range L15-L32
OK   q18+1     FileUploadPropertyEditor.cs:184  range L171-L198
OK   q18+2     ImageCropperPropertyEditor.cs:282  range L268-L296
OK   q19/c27   ScopeEventDispatcherBase.cs:148  range L129-L243
OK   q19+1     DeleteEventArgs.cs:7  range L7-L143
OK   q19+2     SupersedeEventAttribute.cs:11  range L5-L19
OK   q20/c28   ContentFinderByNiceUrlAndTemplate.cs:36  range L17-L77
OK   q21/c29   PublishedContentRequestEngine.cs:462  range L449-L508
OK   q22/c31   RedirectTrackingEventHandler.cs:295  range L286-L296
OK   q22+1     ContentFinderByRedirectUrl.cs:61  range L17-L63
OK   q23/n2    PublishedContentRequest.cs:116  range L109-L118
OK   q24/c35   UmbracoModule.cs:447  range L433-L472
OK   q25/c38   UmbracoContext.cs:134  range L91-L142
OK   q26/c39   UriExtensions.cs:36  range L15-L102
OK   q27/c40   UmbracoModule.cs:136  range L72-L147
OK   q27+1     PublishedContentRequest.cs:93  range L88-L94
OK   q27+2     PublishedContentRequestEngine.cs:139  range L104-L160
OK   q28/c41   ContentController.cs:801  range L800-L844
OK   q29/c42   ContentService.cs:2726  range L2717-L2748
OK   q29+1     ContentController.cs:1072  range L1056-L1109
OK   q30/c43   MediaController.cs:786  range L689-L856
OK   q31/c44   MediaController.cs:863  range L858-L919
OK   q32/c45   ContentPostValidateAttribute.cs:127  range L68-L160
OK   q33/c46   ContentController.cs:868  range L863-L899
OK   q34/c48   macro.cs:744  range L723-L753
OK   q35/c50   library.cs:1551  range L1533-L1556
OK   q36/c51   library.cs:1691  range L1683-L1693
OK   q37/c52   FeedProxy.aspx.cs:33  range L18-L60
OK   q38/c53   Document.cs:811  range L784-L822
OK   q39/c55   macro.cs:1712  range L1690-L1783
OK   q39+1     macroResultWrapper.aspx.cs:47  range L23-L49
OK   q40/n1    library.cs:1013  range L1006-L1014
snippet failures: 0
files in correct_files without an evidence entry: []
```

Crux-noun check after the changes: crux hits (identifier parts): 2 (the q39 "macro" exception, on both of its files).

