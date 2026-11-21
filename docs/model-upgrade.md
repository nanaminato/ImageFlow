# Model upgrade and execution guarantees — 2026-11-21

Official defaults are `gpt-6-luna` for Responses structured prompt planning and `gpt-image-2.6-flare` for Images PNG generation. `gpt-image-2.6-sunburst` is also supported. The catalogue was checked against official documentation on 2026-11-21:

- [Model deprecations](https://developers.openai.com/api/docs/deprecations): `gpt-image-1.5` and `gpt-image-1-mini` shut down 2026-12-01; `gpt-image-2` shuts down 2027-02-10.
- [GPT-6 Luna](https://developers.openai.com/api/docs/models/gpt-6-luna): Responses and structured outputs.
- [Image generation](https://developers.openai.com/api/docs/guides/image-generation): size, quality and complexity settings.
- [Images API contract](https://developers.openai.com/api/reference/resources/images/methods/generate): base64 output and raw usage.

## Configuration

`EngineOptions` binds configuration; `ModelCatalog` declares supported adapter capabilities. Add new models deliberately with contract tests. There is no universal model-option assumption, discovery-based trust or paid startup probe. Official output is intentionally restricted to PNG, with explicit size/quality/complexity. SVG/GIF, edits, streaming partial images and URL downloads are outside this adapter.

Compatible gateways require an explicit profile ID, HTTPS endpoint, separate credential environment-variable reference and declared model/size/quality/complexity/format sets. This declaration is the operator's assertion of a verified contract, not a remote capability check. Official retirement dates do not automatically apply to independent gateways. A gateway that omits complexity or does not support the declared contract needs a separate adapter rather than a silent parameter fallback.

Old local configuration is not rewritten or accepted through aliases. The ignored `appsettings.Local.json` can override repository defaults: review it before restarting. Update official model IDs, or explicitly declare a verified gateway profile. Changing the official defaults does not prove that the currently configured third-party gateway supports Luna or 2.6. Credentials remain environment/secure-local configuration only.

## Persisted execution

Every job snapshots provider, profile ID, endpoint, credential reference (name only), adapter version, model, size, quality, complexity, output format and attempt limit, along with immutable prompt text, estimate, quantity and budget limits. Worker requests are built from that snapshot. A different default model or quality affects new jobs only. Existing work can continue only while its saved model/options remain supported and its exact profile, endpoint, credential reference and adapter are available. Secret rotation under the same reference is allowed. There is no automatic model replacement or fallback to another service.

A saved estimate is bound to a fingerprint of the generation configuration. After changing that configuration, explicitly review/save the estimate in Engine settings before creating new jobs. Existing jobs are not repriced. Measured raw usage is independent of estimated exposure and is not an actual billing record. Planning remains synchronous and outside image-job budgets; planning usage history is still future work.

## Upgrading existing data

1. Stop the old API process and back up SQLite together with its images directory.
2. Review local configuration as above, build the new application, then start one API instance. Do not run different application versions against one database.
3. Startup applies `ExecutionProfiles`. Historical records, images, reviews, request IDs, usage and estimated reservations remain intact. Nonterminal old jobs become `NeedsAttention / ExecutionProfileRequired` because their complete execution destination/options were not recorded. No missing fields are filled from new defaults.
4. Recovery marks previously Running attempts Unknown and retains uncertain exposure. Reconcile provider output and billing records before considering replacement work. Existing manual-review rules remain in force; an incomplete execution profile cannot authorize a new request.
5. Explicitly create a new plan/job for new-model generation. This never edits or resends the old task. Do not delete the database, downgrade it, or edit old snapshots to bypass safeguards.

This preserves historical evidence without retaining an old execution compatibility path.

## Free verification

Run `dotnet test -c Release --no-restore`, frontend unit tests/build, and the Mock browser suite. Backend official-provider tests install an in-memory HTTP handler for every request; no sockets or paid service calls are used. Tests cover structured planning, immutable snapshots, valid PNG decompression/write/download, request IDs/raw usage, missing/corrupt output, no blind retry, profile/destination mismatch, changed defaults, estimate reconfirmation and migration/recovery.

PNG runtime validation checks the file envelope and CRCs, not full raster decoding. Tests decompress a known-valid real PNG fixture. Mock mode still produces labeled SVG previews. Mock tests cannot establish account permissions, gateway support, latency, image quality or actual billing. `scripts/test-provider.py --allow-paid` is a separate opt-in tool and was not run for this upgrade.
