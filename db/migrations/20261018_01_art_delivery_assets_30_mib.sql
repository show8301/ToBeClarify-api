-- Increase the stored artwork image capacity from MEDIUMBLOB to LONGBLOB.
-- The API still enforces a 30 MiB post-conversion PNG limit.
ALTER TABLE ART_DELIVERY_ASSETS
    MODIFY COLUMN FILE_BYTES LONGBLOB NULL;
