-- Additive. Apply after 20261020_03_private_image_files.sql and before this API.
-- No BLOB contents or existing paths are removed. Do not run automatically at startup.
ALTER TABLE ART_DELIVERY_ASSETS
  ADD COLUMN IF NOT EXISTS STORAGE_KEY VARCHAR(512) NULL;
