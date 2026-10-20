# Guestbook and art-delivery disk storage

## Scope and security

New guestbook images and delivery attachments use `MediaDiskStorage`, the same
atomic disk-writing implementation used by ordinary admin media uploads. Ordinary
media retains its existing public root and metadata. Protected images use a
**separate private root** and metadata in `GUESTBOOK_IMAGES` / `ART_DELIVERY_ASSETS`,
not `MEDIA_ASSETS`. They therefore cannot be fetched via `/api/client/media/{id}`
or accidentally removed by ordinary media's unreferenced-file cleanup.

Keep existing feature image endpoints. Guestbook reads check message and parent
visibility; delivery reads check UID/claim code, ready/delivered status and removed
state. Admin authorization and `no-store` responses are unchanged. No browser/API
contract changes or Web deployment are required. Do not create an IIS virtual
directory, static-file mapping, CDN route, or public share for the private root.

Guestbook files remain compressed WebP; delivery files remain lossless PNG. Migration
copies existing bytes without decoding/re-encoding. IDs, URLs, moderation, audit,
delivery status and business timestamps are unchanged. External links are untouched.

## Deployment prerequisites (manual)

1. Back up the DB and existing media. Record/verify the backup recovery procedure.
2. Configure API `appsettings`:

   ```json
   { "Media": { "PrivateRootPath": "D:\\ToBeClarifyData\\PrivateMedia" } }
   ```

   This is an example, not an assumed existing server path. Choose an absolute
   persistent folder outside **all** IIS site roots and deployment/sync/purge targets,
   and outside `Media:RootPath`. The API rejects a private root inside its own content
   root or overlapping its public media root; the operator must additionally verify
   other IIS sites/virtual directories do not publish it. Grant the API app-pool and
   maintenance identity read/write permissions; do not grant anonymous HTTP access.
   Back up this directory together with the DB. Every API instance must see the same
   persistent files. Keep storage directories operator-owned; links/reparse points
   in file paths are rejected.
3. Apply `db/migrations/20261020_03_private_image_files.sql` using the normal approved
   schema release process **before** deploying the new API. This is additive and
   repeatable; no blobs are deleted. The app does not auto-apply schema changes.
4. Deploy the API normally. No Web changes needed. All new images now write files
   and metadata only. Existing images with no storage path still read their DB blob.
   Ensure old API workers are stopped before migration, including overlapped IIS
   recycling workers; old workers cannot read new file-only rows.

## Explicit migration commands (run on server, same settings and identity)

Run from the API deployment directory, using the actual published DLL name:

```powershell
dotnet ToBeClarify.Api.dll --private-image-migration=copy
dotnet ToBeClarify.Api.dll --private-image-migration=verify
```

These commands load normal API settings but do not start HTTP/background workers.
`copy` creates files and records relative path, byte count and SHA-256; original DB
blobs remain. `verify` performs no file/DB writes and checks each file's length/hash
against metadata and any retained blob. Both include **all** guestbook image rows
(including hidden/detached images) and delivery image rows (including soft removed).
They use keyset batches and one-row transactions; at most one image row is locked
at a time. Failures exit nonzero; fix the cause and rerun. Deterministic hash paths
and verification make copy resumable. Do not run multiple maintenance instances
concurrently. For an exact final count/backup, use a short upload/moderation pause;
otherwise concurrent changes can make totals differ. Do not publish private paths
or credentials in a shared migration log.

Acceptance before cleanup: confirm migration totals; public/hidden guestbook images;
authorized/unauthorized, not-ready and removed delivery attachments; existing URLs;
new uploads; and that generic media URLs/IIS direct requests cannot fetch private
files. Verification failure must not be ignored or bypassed with legacy fallback.

Only **after** backup and explicit production acceptance, release the DB blob contents:

```powershell
dotnet ToBeClarify.Api.dll --private-image-migration=clear-legacy
dotnet ToBeClarify.Api.dll --private-image-migration=verify
```

`clear-legacy` verifies file bytes against both metadata and retained DB bytes under
a row lock immediately before setting that row's blob to NULL. It never deletes an
image row or file. It is resumable but **not** a single all-or-nothing transaction;
on error, already verified/cleared rows stay cleared. Legacy nullable columns remain
for a later approved schema cleanup. No new image bytes are stored in those columns.
Clearing blobs does not necessarily shrink MariaDB data files on disk; reclaiming
physical database space is a separate DBA operation, not part of this migration.

## Failure, deletion and retention

Disk and DB cannot share a transaction. Write complete files atomically **before**
recording references; retain an unreferenced file on rollback/ambiguous commit,
rather than risk deleting a committed image. Temporary writes are removed on normal
failure (process crashes may leave `.tmp` files). Report candidates without deletion:

```powershell
dotnet ToBeClarify.Api.dll --private-image-migration=report-orphans
```

This report is not permission to delete, and may include active uploads. Schedule
cleanup only with a separate approved retention policy, upload/maintenance pause,
reference recheck and backup. Guestbook image removal detaches the message's ID and
keeps image metadata/file for retention; access then fails the association check.
Delivery soft removal retains its metadata/file and blocks reads. Hiding messages
does not delete files. There is intentionally no automatic private-file deletion.

## Rollback

Before blob cleanup, original blobs remain for pre-existing images, but **new images
have only files**. Do not roll back to the old API binary without restoring file-only
rows to their legacy blob columns first via an approved recovery procedure. Retain
the new compatible reader or use a forward fix. After cleanup, recovery requires
the private files or paired backups; a DB-only restore is insufficient. Never remove
the private directory or old columns just to roll back code.
